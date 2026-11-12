using EFBM.Core.Security;
using EFBM.Web.Security;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>原始报文管理的后端接口（FR-15）：检索、原文查看、手动标记、重新入队重放、导出与清理。</summary>
/// <remarks>
/// 页面（`WEB-6b`）将复用这组接口；接口只做参数校验与结果包装，过滤 / 分页 / 重放 / 导出语义都在
/// <see cref="EventQueryService"/>、<see cref="EventReplayService"/> 与 <see cref="EventExportService"/>，可脱离 HTTP 单测。
///
/// 重放<b>不覆盖</b>任何已落库数据：新消息带 <c>replayOf</c>，<c>eventKey</c> 与原报文一致，
/// 消费端命中幂等只累加 <c>RepeatCount</c>（FR-24）。所有写操作都记审计日志并标注操作人。
///
/// 清理<b>必须凭导出凭证</b>（<c>POST export</c> 返回的 <c>ticketId</c>）执行，且只删导出过的那部分，
/// 没有凭证无法删除任何数据（FR-15「先导出再删」）。
/// </remarks>
[Route("api/admin/events")]
public sealed class AdminEventsController : AdminControllerBase
{
    private readonly EventQueryService _query;
    private readonly EventReplayService _replay;
    private readonly EventExportService _export;

    /// <summary>构造原始报文管理接口。</summary>
    /// <param name="query">只读检索服务。</param>
    /// <param name="replay">重放与标记服务。</param>
    /// <param name="export">导出与清理服务。</param>
    /// <param name="identity">当前请求身份。</param>
    public AdminEventsController(EventQueryService query, EventReplayService replay, EventExportService export, IAdminIdentityAccessor identity)
        : base(identity)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _export = export ?? throw new ArgumentNullException(nameof(export));
    }

    /// <summary>按设备 / 事件 / 幂等键 / 处理状态 / 接收时间 / 关键词分页检索原始报文。</summary>
    /// <param name="query">检索条件，来自查询字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页列表（不含报文原文大字段）。</returns>
    [HttpGet]
    [RequireAdminOperation(AdminOperation.QueryEvents)]
    public async Task<ObjectResult> List([FromQuery] EventQuery query, CancellationToken cancellationToken)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Failure(400, "from 不能晚于 to");

        var data = await _query.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return Success(data);
    }

    /// <summary>读取一条原始报文的详情，含报文原文、<c>data</c>、扩展与未识别字段。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或 404。</returns>
    [HttpGet("{id:long}")]
    [RequireAdminOperation(AdminOperation.ViewEventDetail)]
    public async Task<ObjectResult> Get(Int64 id, CancellationToken cancellationToken)
    {
        var detail = await _query.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return detail == null ? Failure(404, "记录不存在") : Success(detail);
    }

    /// <summary>手动标记处理状态（0 未处理 / 1 已处理 / 2 忽略），不改动报文内容。</summary>
    /// <param name="id">主键。</param>
    /// <param name="request">标记内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标记结果或 404。</returns>
    [HttpPost("{id:long}/mark")]
    [RequireAdminOperation(AdminOperation.MarkEvent)]
    [Audit("标记处理状态")]
    public async Task<ObjectResult> Mark(Int64 id, [FromBody] MarkRequest request, CancellationToken cancellationToken)
    {
        request ??= new MarkRequest();

        var ok = await _replay.MarkAsync(id, request.HandleStatus, request.Remark, OperatorName, cancellationToken).ConfigureAwait(false);
        return ok ? Success(new { id, request.HandleStatus }) : Failure(404, "记录不存在");
    }

    /// <summary>把一条原始报文重新写入共享流（广播所有消费组，带 <c>replayOf</c>）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新消息 ID 与 Stream 消息 ID，或失败原因。</returns>
    [HttpPost("{id:long}/replay")]
    [RequireAdminOperation(AdminOperation.ReplayEvent)]
    [RequireConfirm(AdminOperation.ReplayEvent)]
    [Audit("重放原始报文")]
    public async Task<ObjectResult> Replay(Int64 id, CancellationToken cancellationToken)
    {
        var result = await _replay.ReplayEventAsync(id, OperatorName, cancellationToken).ConfigureAwait(false);
        return BuildResult(result);
    }

    /// <summary>把重放结果翻译成 HTTP 结果：不存在 404、前置条件不满足 409、队列异常 503。</summary>
    /// <param name="result">重放结果。</param>
    /// <returns>统一结构响应。</returns>
    private ObjectResult BuildResult(ReplayResult result)
    {
        if (result.Success) return Success(result);

        var status = result.Reason switch
        {
            "记录不存在" => 404,
            _ when result.Reason.StartsWith("Redis 未启用", StringComparison.Ordinal) => 409,
            _ when result.Reason.StartsWith("记录缺少报文原文", StringComparison.Ordinal) => 409,
            _ => 503,
        };

        return Failure(status, result.Reason);
    }

    /// <summary>按接收时间范围批量重放（STR-4b），每条带 <c>replayOf</c>，广播所有消费组。</summary>
    /// <param name="request">范围与条数上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中 / 尝试 / 成功 / 失败统计与错误摘要。</returns>
    [HttpPost("replay-range")]
    [RequireAdminOperation(AdminOperation.ReplayEvent)]
    [RequireConfirm(AdminOperation.ReplayEvent)]
    [Audit("按范围批量重放")]
    public async Task<ObjectResult> ReplayRange([FromBody] ReplayRangeRequest request, CancellationToken cancellationToken)
    {
        request ??= new ReplayRangeRequest();
        if (!request.From.HasValue || !request.To.HasValue)
            return Failure(400, "必须同时提供 from 与 to（接收时间范围）");
        if (request.From > request.To)
            return Failure(400, "from 不能晚于 to");

        var result = await _replay
            .ReplayRangeAsync(request.From.Value, request.To.Value, request.Limit, OperatorName, cancellationToken)
            .ConfigureAwait(false);

        return result.Success ? Success(result) : Failure(409, result.Reason);
    }

    /// <summary>按接收时间范围导出原始报文为 CSV，返回导出凭证（清理的前置条件）。</summary>
    /// <param name="from">下界（含）。</param>
    /// <param name="to">上界（含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭证 ID、条数、文件哈希与过期时间。</returns>
    [HttpPost("export")]
    [RequireAdminOperation(AdminOperation.ExportEvents)]
    [Audit("导出原始报文")]
    public async Task<ObjectResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue) return Failure(400, "必须同时提供 from 与 to（接收时间范围）");
        if (from > to) return Failure(400, "from 不能晚于 to");

        var result = await _export.ExportAsync(from.Value, to.Value, OperatorName, cancellationToken).ConfigureAwait(false);

        return result.Success ? Success(result) : Failure(409, result.Reason);
    }

    /// <summary>下载已导出的 CSV 文件（凭凭证，不改动数据）。</summary>
    /// <param name="ticketId">导出凭证 ID。</param>
    /// <returns>文件流；凭证无效 404。</returns>
    [HttpGet("export/{ticketId}/download")]
    [RequireAdminOperation(AdminOperation.DownloadExport)]
    public IActionResult Download(String ticketId)
    {
        var ticket = _export.FindTicket(ticketId);
        if (ticket == null) return NotFound();
        if (!System.IO.File.Exists(ticket.FilePath)) return NotFound();

        return PhysicalFile(ticket.FilePath, "text/csv; charset=utf-8", Path.GetFileName(ticket.FilePath));
    }

    /// <summary>凭导出凭证删除该范围已导出的原始报文（FR-15「先导出再删」，无自动清理任务）。</summary>
    /// <param name="request">凭证 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除条数与导出条数；凭证无效 409。</returns>
    [HttpPost("cleanup")]
    [RequireAdminOperation(AdminOperation.CleanupEvents)]
    [RequireConfirm(AdminOperation.CleanupEvents)]
    [Audit("清理原始报文")]
    public async Task<ObjectResult> Cleanup([FromBody] CleanupRequest request, CancellationToken cancellationToken)
    {
        request ??= new CleanupRequest();

        var result = await _export.CleanupAsync(request.TicketId, OperatorName, cancellationToken).ConfigureAwait(false);
        return result.Success ? Success(result) : Failure(409, result.Reason);
    }
}

/// <summary>批量重放请求体。</summary>
public sealed class ReplayRangeRequest
{
    /// <summary>接收时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>接收时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>最多重放条数；<c>≤0</c> 取默认 500，硬上限 2000。</summary>
    public Int32 Limit { get; set; }
}

/// <summary>清理请求体。</summary>
public sealed class CleanupRequest
{
    /// <summary>导出凭证 ID。</summary>
    public String TicketId { get; set; }
}

/// <summary>手动标记请求体。</summary>
public sealed class MarkRequest
{
    /// <summary>处理状态：0 未处理 / 1 已处理 / 2 忽略。</summary>
    public Int32 HandleStatus { get; set; }

    /// <summary>处理备注。</summary>
    public String Remark { get; set; }
}
