using System.Text.Json;
using EFBM.Core.Security;
using NewLife.Data;
using NewLife.Log;
using XCode;
using XCode.Membership;

namespace EFBM.Web.Services;

/// <summary>一条后台操作审计记录（FR-15 / AUTH-3b）。</summary>
public sealed class AuditRecord
{
    /// <summary>审计分类，固定写进 Cube 日志表的 <c>Category</c>，便于与系统其他日志区分。</summary>
    public const String Category = "EFBM后台";

    /// <summary>动作中文名（清理 / 重放报文 / 重放死信 / 标记 / 导出）。</summary>
    public String Action { get; init; }

    /// <summary>目标主键（原始报文或死信 ID），无则为 <c>null</c>。</summary>
    public Int64? TargetId { get; init; }

    /// <summary>是否成功。</summary>
    public Boolean Success { get; init; }

    /// <summary>操作人登录名。</summary>
    public String Operator { get; init; }

    /// <summary>来源 IP。</summary>
    public String Ip { get; init; }

    /// <summary>请求追踪 ID，便于与服务日志对齐。</summary>
    public String TraceId { get; init; }

    /// <summary>结果摘要（如「删除 1234 条」、新消息 ID）。</summary>
    public String Summary { get; init; }

    /// <summary>参数摘要（时间范围、条数上限等），不含任何密钥。</summary>
    public String Remark { get; init; }

    /// <summary>发生时间。</summary>
    public DateTime Time { get; init; }
}

/// <summary>审计日志查询条件。</summary>
public sealed class AuditQuery
{
    /// <summary>动作（精确匹配，可空）。</summary>
    public String Action { get; set; }

    /// <summary>操作人（模糊匹配，可空）。</summary>
    public String Operator { get; set; }

    /// <summary>起始时间（含，可空）。</summary>
    public DateTime? From { get; set; }

    /// <summary>结束时间（含，可空）。</summary>
    public DateTime? To { get; set; }

    /// <summary>是否成功；<c>null</c> 表示不筛选。</summary>
    public Boolean? Success { get; set; }

    /// <summary>页码，从 1 开始。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数，上限 200。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>审计日志分页结果。</summary>
public sealed class AuditPage
{
    /// <summary>总数。</summary>
    public Int64 Total { get; init; }

    /// <summary>当前页页码。</summary>
    public Int32 Page { get; init; }

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; init; }

    /// <summary>当前页记录。</summary>
    public IReadOnlyList<AuditRecord> Items { get; init; } = [];
}

/// <summary>审计日志读写抽象（便于单测替身）。</summary>
public interface IAuditStore
{
    /// <summary>写一条审计；实现必须<b>不抛异常</b>——审计失败不能把业务操作带崩。</summary>
    /// <param name="record">审计记录。</param>
    void Write(AuditRecord record);

    /// <summary>分页查询审计。</summary>
    /// <param name="query">查询条件。</param>
    /// <returns>分页结果。</returns>
    AuditPage Query(AuditQuery query);
}

/// <summary>基于 Cube <c>Log</c> 表的审计存储（Membership 库，后台已有日志页可看）。</summary>
/// <remarks>
/// 为什么不另建表：Cube 的 <c>Log</c> 已是「操作留痕」的既成事实标准（后台「日志」页直接可查），
/// 字段也够用——<c>Category</c> 区分来源、<c>Action</c> 存动作、<c>LinkID</c> 存目标主键、
/// <c>Success</c> 存结果、<c>Ex1</c> 存结果摘要、<c>Remark</c> 存参数摘要。
/// 另建表要多一份实体生成与迁移，收益仅是字段名更贴切，不值。
/// </remarks>
public sealed class CubeAuditStore : IAuditStore
{
    /// <summary>Ex4 字段的最大长度（取自实体元数据，取不到时按 200 保守处理）。</summary>
    private static readonly Int32 Ex4Length = FieldLength("Ex4", 200);

    /// <summary>Remark 字段的最大长度。</summary>
    private static readonly Int32 RemarkLength = FieldLength("Remark", 500);

    /// <summary>从实体元数据取字段长度；框架表结构变化时退回保守值。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="fallback">取不到时的默认长度。</param>
    /// <returns>字段长度。</returns>
    private static Int32 FieldLength(String name, Int32 fallback)
    {
        try
        {
            var field = Log.Meta.Fields.FirstOrDefault(f => String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            return field is { Length: > 0 } ? field.Length : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <inheritdoc />
    public void Write(AuditRecord record)
    {
        if (record == null) return;

        try
        {
            new Log
            {
                Category = AuditRecord.Category,
                Action = record.Action,
                LinkID = record.TargetId ?? 0,
                Success = record.Success,
                UserName = record.Operator,
                // Ex1 是整型（框架保留），摘要放 Ex4；Remark 存参数。
                // 长度按实体元数据截断：超长会被 XCode 直接判为写入失败，审计反而丢了
                Ex4 = Truncate(record.Summary, Ex4Length),
                Remark = Truncate(record.Remark, RemarkLength),
                TraceId = record.TraceId,
                CreateUser = record.Operator,
                CreateIP = record.Ip,
                CreateTime = record.Time == default ? DateTime.Now : record.Time,
            }.Insert();
        }
        catch (Exception ex)
        {
            // 审计写不进去也要让业务继续（清理已经执行了，不能因为日志失败回滚成「不知道删没删」），
            // 但必须留痕到应用日志，方便事后补账
            XTrace.WriteException(ex);
        }
    }

    /// <inheritdoc />
    public AuditPage Query(AuditQuery query)
    {
        query ??= new AuditQuery();

        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.PageSize is <= 0 or > 200 ? 20 : query.PageSize;

        var where = new WhereExpression();
        where &= Log._.Category == AuditRecord.Category;
        if (!String.IsNullOrWhiteSpace(query.Action)) where &= Log._.Action == query.Action.Trim();
        if (!String.IsNullOrWhiteSpace(query.Operator)) where &= Log._.UserName.Contains(query.Operator.Trim());
        if (query.From.HasValue) where &= Log._.CreateTime >= query.From.Value;
        if (query.To.HasValue) where &= Log._.CreateTime <= query.To.Value;
        if (query.Success.HasValue) where &= Log._.Success == query.Success.Value;

        var pager = new PageParameter
        {
            PageIndex = page,
            PageSize = size,
            Sort = "CreateTime",
            Desc = true,
            RetrieveTotalCount = true,
        };
        var list = Log.FindAll(where, pager);

        return new AuditPage
        {
            Total = pager.TotalCount,
            Page = page,
            PageSize = size,
            Items = [.. list.Select(ToRecord)],
        };
    }

    /// <summary>把日志实体还原成审计记录。</summary>
    /// <param name="entity">日志实体。</param>
    /// <returns>审计记录。</returns>
    private static AuditRecord ToRecord(Log entity) => new()
    {
        Action = entity.Action,
        TargetId = entity.LinkID is 0 ? null : entity.LinkID,
        Success = entity.Success,
        Operator = entity.UserName,
        Ip = entity.CreateIP,
        TraceId = entity.TraceId,
        Summary = entity.Ex4,
        Remark = entity.Remark,
        Time = entity.CreateTime,
    };

    /// <summary>截断超长文本，避免一条审计把日志表撑爆。</summary>
    /// <param name="text">原文。</param>
    /// <param name="max">最大长度。</param>
    /// <returns>截断后的文本。</returns>
    private static String Truncate(String text, Int32 max)
        => String.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max];

    /// <summary>把任意结果对象压成一行摘要（供 <see cref="AuditRecord.Summary"/> 使用）。</summary>
    /// <param name="value">结果数据。</param>
    /// <returns>JSON 文本；失败返回类型名。</returns>
    public static String Summarize(Object value)
    {
        if (value == null) return null;

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (Exception)
        {
            return value.GetType().Name;
        }
    }
}
