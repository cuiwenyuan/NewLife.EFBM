using EFBM.Data.Entities;
using NewLife.Log;
using XCode;

namespace EFBM.Web.Services;

/// <summary>手动关闭故障的结果。</summary>
public sealed class FaultCloseResult
{
    /// <summary>是否关闭成功。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因；成功时为 <c>null</c>。</summary>
    public String Reason { get; init; }

    /// <summary>关闭后的故障记录；成功时才有。</summary>
    public FaultRecord Fault { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="fault">关闭后的记录。</param>
    /// <returns>成功结果。</returns>
    public static FaultCloseResult Ok(FaultRecord fault) => new() { Success = true, Fault = fault };

    /// <summary>构造失败结果。</summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>失败结果。</returns>
    public static FaultCloseResult Fail(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>故障处置：未关闭故障的检索与「手动关闭」（FR-13、WEB-5b）。</summary>
/// <remarks>
/// 为什么需要手动关闭：设备侧 <c>FAULT_CLEARED</c> 可能永远不来（断电、掉网、设备报废），
/// 这类故障会一直挂在「未清除」里，把现场告警淹掉。
/// 手动关闭是把 <c>Status</c> 置 2（与设备恢复的 1 区分开）、<c>ClearedAt</c> 记 UTC 当前时间，
/// <b>不改 <c>Message</c> / <c>OccurredAt</c> 等设备事实</b>，并在审计日志里留下操作人与原因。
/// </remarks>
public sealed class FaultDisposalService
{
    /// <summary>状态：未清除（设备未上报恢复）。</summary>
    public const Int32 StatusOpen = 0;

    /// <summary>状态：设备上报恢复事件后自动清除。</summary>
    public const Int32 StatusCleared = 1;

    /// <summary>状态：人工关闭。</summary>
    public const Int32 StatusClosed = 2;

    /// <summary>每页条数上限。</summary>
    public const Int32 MaxPageSize = 200;

    /// <summary>按设备编号分页查询未关闭故障，按发生时间倒序（最早发生的排最前，先处理最老的）。</summary>
    /// <param name="deviceId">设备编号，为空表示全部。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResult<FaultRecord>> QueryOpenAsync(String deviceId, Int32 page, Int32 pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var index = Math.Max(1, page);
        var size = pageSize <= 0 ? 20 : Math.Min(pageSize, MaxPageSize);

        Expression exp = FaultRecord._.Status == StatusOpen;
        if (!String.IsNullOrWhiteSpace(deviceId)) exp &= FaultRecord._.DeviceId == deviceId.Trim();

        var total = FaultRecord.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(PagedResult<FaultRecord>.Empty(index, size));

        // 未关闭故障按「发生时间正序」：最老的先处理，避免新故障把旧故障挤到看不见
        var list = FaultRecord.FindAll(exp, FaultRecord._.OccurredAt.Asc(), null, (Int64)(index - 1) * size, size);

        return Task.FromResult(new PagedResult<FaultRecord>
        {
            Items = list.ToList(),
            Total = total,
            Page = index,
            PageSize = size,
        });
    }

    /// <summary>查询设备上未关闭的故障（用于关闭前展示给操作人确认）。</summary>
    /// <param name="id">故障主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>故障记录；不存在返回 <c>null</c>。</returns>
    public Task<FaultRecord> GetAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(id <= 0 ? null : FaultRecord.FindById(id));
    }

    /// <summary>手动关闭一条故障。</summary>
    /// <param name="id">故障主键。</param>
    /// <param name="remark">关闭原因，写进审计日志；必填，避免事后无法解释。</param>
    /// <param name="operatorName">操作人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关闭结果。</returns>
    public Task<FaultCloseResult> CloseAsync(Int64 id, String remark, String operatorName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (id <= 0) return Task.FromResult(FaultCloseResult.Fail("故障 ID 必须大于 0"));
        if (String.IsNullOrWhiteSpace(remark)) return Task.FromResult(FaultCloseResult.Fail("必须填写关闭原因（写进审计日志）"));

        var entity = FaultRecord.FindById(id);
        if (entity == null) return Task.FromResult(FaultCloseResult.Fail("故障记录不存在"));

        switch (entity.Status)
        {
            case StatusCleared:
                return Task.FromResult(FaultCloseResult.Fail("该故障已由设备恢复事件清除，无需手动关闭"));
            case StatusClosed:
                return Task.FromResult(FaultCloseResult.Fail("该故障已手动关闭，请勿重复操作"));
        }

        entity.Status = StatusClosed;
        // 与设备上报的清除时间保持同一口径：列注释明确是 UTC
        entity.ClearedAt = DateTime.UtcNow;
        entity.Update();

        XTrace.WriteLine("后台手动关闭故障：id={0} deviceId={1} occurredAt={2:yyyy-MM-dd HH:mm:ss} 操作人={3} 原因={4}",
            entity.Id, entity.DeviceId, entity.OccurredAt, operatorName, remark);

        return Task.FromResult(FaultCloseResult.Ok(entity));
    }

    /// <summary>故障持续时长文案：未清除时算到当前时间。</summary>
    /// <param name="fault">故障记录。</param>
    /// <returns>例如「1 天 2 小时」「35 分钟」。</returns>
    public static String DescribeDuration(FaultRecord fault)
    {
        if (fault == null) return "—";

        var end = fault.ClearedAt > DateTime.MinValue && fault.ClearedAt > fault.OccurredAt ? fault.ClearedAt : DateTime.UtcNow;
        var span = end - fault.OccurredAt;
        if (span < TimeSpan.Zero) return "—";

        if (span.TotalDays >= 1) return $"{(Int32)span.TotalDays} 天 {span.Hours} 小时";
        if (span.TotalHours >= 1) return $"{(Int32)span.TotalHours} 小时 {span.Minutes} 分钟";
        return $"{(Int32)span.TotalMinutes} 分钟";
    }

    /// <summary>故障状态文案。</summary>
    /// <param name="status">状态原值。</param>
    /// <returns>中文文案；未知值原样显示数字。</returns>
    public static String DescribeStatus(Int32 status) => status switch
    {
        StatusOpen => "未清除",
        StatusCleared => "已恢复",
        StatusClosed => "手动关闭",
        _ => status.ToString(),
    };
}
