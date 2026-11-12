using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;
using XCode;

namespace EFBM.Web.Services;

/// <summary>后台原始报文检索条件（FR-15）。</summary>
/// <remarks>
/// 只做过滤，不含排序与分页策略；分页在 <see cref="EventQueryService"/> 里统一收敛
/// （`Page ≥ 1`、`PageSize ∈ [1, 200]`），避免调用方传入超大页把数据库拖死。
/// <c>From</c> / <c>To</c> 作用于服务端<b>接收时间</b> <c>ReceivedAt</c>，含边界。
/// </remarks>
public sealed class EventQuery
{
    /// <summary>设备编号，精确匹配。</summary>
    public String DeviceId { get; set; }

    /// <summary>事件类型，精确匹配。</summary>
    public String Event { get; set; }

    /// <summary>幂等键，精确匹配。</summary>
    public String EventKey { get; set; }

    /// <summary>处理状态（见 <see cref="RawProcessStatus"/>），<c>null</c> 不限。</summary>
    public Int32? ProcessStatus { get; set; }

    /// <summary>人工处理状态，<c>null</c> 不限。</summary>
    public Int32? HandleStatus { get; set; }

    /// <summary>接收时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>接收时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>报文原文模糊匹配的关键词；为空则不做原文扫描。</summary>
    public String Keyword { get; set; }

    /// <summary>页码，从 1 开始。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>死信列表检索条件（FR-12）。</summary>
public sealed class DeadLetterQuery
{
    /// <summary>幂等键，精确匹配。</summary>
    public String EventKey { get; set; }

    /// <summary>消息追踪 ID，精确匹配。</summary>
    public String MessageId { get; set; }

    /// <summary>重放状态：0 未重放 / 1 已重放，<c>null</c> 不限。</summary>
    public Int32? ReplayStatus { get; set; }

    /// <summary>创建时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>创建时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>页码，从 1 开始。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>分页结果，含总数便于后台渲染页码。</summary>
/// <typeparam name="T">列表项类型。</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>符合条件总条数。</summary>
    public Int64 Total { get; init; }

    /// <summary>当前页码（已归一化）。</summary>
    public Int32 Page { get; init; }

    /// <summary>每页条数（已归一化）。</summary>
    public Int32 PageSize { get; init; }

    /// <summary>总页数。</summary>
    public Int32 TotalPages => PageSize <= 0 ? 0 : (Int32)((Total + PageSize - 1) / PageSize);

    /// <summary>构造空结果。</summary>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>空分页结果。</returns>
    public static PagedResult<T> Empty(Int32 page, Int32 pageSize) => new() { Page = page, PageSize = pageSize };
}

/// <summary>原始报文列表项：不含大字段原文，避免列表接口把几百 KB 的 JSON 一起吐出去。</summary>
public sealed class EventRawItem
{
    /// <summary>主键。</summary>
    public Int64 Id { get; init; }

    /// <summary>幂等键。</summary>
    public String EventKey { get; init; }

    /// <summary>消息追踪 ID。</summary>
    public String MessageId { get; init; }

    /// <summary>Stream 消息 ID。</summary>
    public String StreamId { get; init; }

    /// <summary>事件类型。</summary>
    public String Event { get; init; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; init; }

    /// <summary>设备侧时间原文。</summary>
    public String EventTimeRaw { get; init; }

    /// <summary>设备侧时间（解析后）。</summary>
    public DateTime EventTime { get; init; }

    /// <summary>服务端接收时间。</summary>
    public DateTime ReceivedAt { get; init; }

    /// <summary>落库完成时间；未落库为 <see cref="EfbmDefaults.UnrequeuedAt"/>。</summary>
    public DateTime PersistedAt { get; init; }

    /// <summary>处理状态原值。</summary>
    public Int32 ProcessStatus { get; init; }

    /// <summary>处理状态文案。</summary>
    public String ProcessStatusText => DescribeStatus(ProcessStatus);

    /// <summary>重复投递次数。</summary>
    public Int32 RepeatCount { get; init; }

    /// <summary>重放来源的原消息 ID。</summary>
    public String ReplayOf { get; init; }

    /// <summary>来源 IP。</summary>
    public String SourceIp { get; init; }

    /// <summary>字段字典版本。</summary>
    public String ParseVersion { get; init; }

    /// <summary>人工处理状态。</summary>
    public Int32 HandleStatus { get; init; }

    /// <summary>人工处理人。</summary>
    public String HandleBy { get; init; }

    /// <summary>人工处理时间。</summary>
    public DateTime HandleAt { get; init; }

    /// <summary>处理状态原值转中文；未知值原样显示数字（需求说明 FR-27 同类要求）。</summary>
    /// <param name="status">状态原值。</param>
    /// <returns>展示文案。</returns>
    public static String DescribeStatus(Int32 status) => status switch
    {
        RawProcessStatus.Pending => "待处理",
        RawProcessStatus.Persisted => "已落库",
        RawProcessStatus.Dead => "死信",
        RawProcessStatus.Replayed => "已重放",
        _ => status.ToString(),
    };

    /// <summary>由实体构造列表项。</summary>
    /// <param name="entity">原始报文实体。</param>
    /// <returns>列表项。</returns>
    public static EventRawItem From(DeviceEventRaw entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        return new EventRawItem
        {
            Id = entity.Id,
            EventKey = entity.EventKey,
            MessageId = entity.MessageId,
            StreamId = entity.StreamId,
            Event = entity.Event,
            DeviceId = entity.DeviceId,
            EventTimeRaw = entity.EventTimeRaw,
            EventTime = entity.EventTime,
            ReceivedAt = entity.ReceivedAt,
            PersistedAt = entity.PersistedAt,
            ProcessStatus = entity.ProcessStatus,
            RepeatCount = entity.RepeatCount,
            ReplayOf = entity.ReplayOf,
            SourceIp = entity.SourceIp,
            ParseVersion = entity.ParseVersion,
            HandleStatus = entity.HandleStatus,
            HandleBy = entity.HandleBy,
            HandleAt = entity.HandleAt,
        };
    }
}

/// <summary>原始报文详情：在列表字段之外补齐原文与解析留痕（FR-15、FR-16、FR-27）。</summary>
public sealed class EventRawDetail
{
    /// <summary>列表字段。</summary>
    public EventRawItem Item { get; init; }

    /// <summary>设备原始报文全文，逐字未改写。</summary>
    public String RawJson { get; init; }

    /// <summary>解析出的 <c>data</c> 节点。</summary>
    public String DataJson { get; init; }

    /// <summary>扩展字段。</summary>
    public String ExtJson { get; init; }

    /// <summary>未识别字段。</summary>
    public String UnknownFields { get; init; }

    /// <summary>解析告警。</summary>
    public String ParseWarning { get; init; }

    /// <summary>人工处理备注。</summary>
    public String HandleRemark { get; init; }
}

/// <summary>死信列表项。</summary>
public sealed class DeadLetterItem
{
    /// <summary>主键。</summary>
    public Int64 Id { get; init; }

    /// <summary>消息追踪 ID。</summary>
    public String MessageId { get; init; }

    /// <summary>Stream 消息 ID。</summary>
    public String StreamId { get; init; }

    /// <summary>幂等键。</summary>
    public String EventKey { get; init; }

    /// <summary>重试次数。</summary>
    public Int32 RetryCount { get; init; }

    /// <summary>最后一次错误摘要。</summary>
    public String LastError { get; init; }

    /// <summary>重放状态：0 未重放 / 1 已重放。</summary>
    public Int32 ReplayStatus { get; init; }

    /// <summary>重放时间。</summary>
    public DateTime ReplayAt { get; init; }

    /// <summary>重放操作人。</summary>
    public String ReplayBy { get; init; }

    /// <summary>创建时间。</summary>
    public DateTime CreateTime { get; init; }

    /// <summary>报文中的设备编号（从消息体解析，损坏时为 <c>null</c>）。</summary>
    public String DeviceId { get; init; }

    /// <summary>报文中的事件类型（从消息体解析，损坏时为 <c>null</c>）。</summary>
    public String Event { get; init; }
}

/// <summary>后台只读检索：原始报文与死信的分页查询（FR-15、FR-12）。</summary>
/// <remarks>
/// 与消费链路一样只走 XCode 实体，不写 SQL 字符串；过滤条件用 <see cref="Expression"/> 组合，
/// 参数化由 XCode 负责。列表接口<b>不返回</b>原文大字段，原文只在详情接口返回。
/// </remarks>
public sealed class EventQueryService
{
    /// <summary>每页条数上限，防止一次拉全表。</summary>
    public const Int32 MaxPageSize = 200;

    /// <summary>按条件分页查询原始报文，按主键倒序（最新在前）。</summary>
    /// <param name="query">检索条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResult<EventRawItem>> QueryAsync(EventQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new EventQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);
        var exp = BuildExpression(query);

        // 时间区间倒挂属于调用方错误，直接返回空集，不把 SQL 交给数据库去解释
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Task.FromResult(PagedResult<EventRawItem>.Empty(page, size));

        var total = DeviceEventRaw.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(PagedResult<EventRawItem>.Empty(page, size));

        var list = DeviceEventRaw.FindAll(exp, DeviceEventRaw._.Id.Desc(), null, (Int64)(page - 1) * size, size);

        return Task.FromResult(new PagedResult<EventRawItem>
        {
            Items = list.Select(EventRawItem.From).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>读取单条原始报文详情（含原文与解析留痕）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情；记录不存在返回 <c>null</c>。</returns>
    public Task<EventRawDetail> GetAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = DeviceEventRaw.FindById(id);
        if (entity == null) return Task.FromResult<EventRawDetail>(null);

        return Task.FromResult(new EventRawDetail
        {
            Item = EventRawItem.From(entity),
            RawJson = entity.RawJson,
            DataJson = entity.DataJson,
            ExtJson = entity.ExtJson,
            UnknownFields = entity.UnknownFields,
            ParseWarning = entity.ParseWarning,
            HandleRemark = entity.HandleRemark,
        });
    }

    /// <summary>按条件分页查询死信，按主键倒序。</summary>
    /// <param name="query">检索条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResult<DeadLetterItem>> QueryDeadLettersAsync(DeadLetterQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new DeadLetterQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);

        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Task.FromResult(PagedResult<DeadLetterItem>.Empty(page, size));

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.EventKey) ? null : DeadLetter._.EventKey == query.EventKey.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.MessageId) ? null : DeadLetter._.MessageId == query.MessageId.Trim());
        exp = And(exp, query.ReplayStatus.HasValue ? DeadLetter._.ReplayStatus == query.ReplayStatus.Value : null);
        exp = And(exp, query.From.HasValue ? DeadLetter._.CreateTime >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? DeadLetter._.CreateTime <= query.To.Value : null);

        var total = DeadLetter.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(PagedResult<DeadLetterItem>.Empty(page, size));

        var list = DeadLetter.FindAll(exp, DeadLetter._.Id.Desc(), null, (Int64)(page - 1) * size, size);

        return Task.FromResult(new PagedResult<DeadLetterItem>
        {
            Items = list.Select(ToItem).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>读取单条死信。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>死信实体；不存在返回 <c>null</c>。</returns>
    public Task<DeadLetter> GetDeadLetterAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeadLetter.FindById(id));
    }

    /// <summary>把过滤条件组合成 XCode 表达式；全部为空时返回 <c>null</c>（不限）。</summary>
    private static Expression BuildExpression(EventQuery query)
    {
        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : DeviceEventRaw._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.Event) ? null : DeviceEventRaw._.Event == query.Event.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.EventKey) ? null : DeviceEventRaw._.EventKey == query.EventKey.Trim());
        exp = And(exp, query.ProcessStatus.HasValue ? DeviceEventRaw._.ProcessStatus == query.ProcessStatus.Value : null);
        exp = And(exp, query.HandleStatus.HasValue ? DeviceEventRaw._.HandleStatus == query.HandleStatus.Value : null);
        exp = And(exp, query.From.HasValue ? DeviceEventRaw._.ReceivedAt >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? DeviceEventRaw._.ReceivedAt <= query.To.Value : null);
        exp = And(exp, String.IsNullOrWhiteSpace(query.Keyword) ? null : DeviceEventRaw._.RawJson.Contains(query.Keyword.Trim()));
        return exp;
    }

    /// <summary>以 AND 叠加条件，任一侧为空时返回非空的一侧。</summary>
    private static Expression And(Expression left, Expression right)
    {
        if (left == null) return right;
        if (right == null) return left;
        return left & right;
    }

    /// <summary>归一化每页条数。</summary>
    private static Int32 ClampSize(Int32 size) => size <= 0 ? 20 : Math.Min(size, MaxPageSize);

    /// <summary>实体转死信列表项，顺带解析设备与事件便于后台定位。</summary>
    private static DeadLetterItem ToItem(DeadLetter entity)
    {
        // 死信里存的是 Stream 消息体 JSON；损坏时只丢这两个展示字段，不影响列表本身
        var message = SafeDeserialize(entity.RawJson);

        return new DeadLetterItem
        {
            Id = entity.Id,
            MessageId = entity.MessageId,
            StreamId = entity.StreamId,
            EventKey = entity.EventKey,
            RetryCount = entity.RetryCount,
            LastError = entity.LastError,
            ReplayStatus = entity.ReplayStatus,
            ReplayAt = entity.ReplayAt,
            ReplayBy = entity.ReplayBy,
            CreateTime = entity.CreateTime,
            DeviceId = message?.DeviceId,
            Event = message?.Event,
        };
    }

    /// <summary>消息体反序列化失败不抛异常，列表照常展示。</summary>
    private static StreamMessage SafeDeserialize(String json)
    {
        try { return EventIngestService.Deserialize(json); }
        catch (Exception) { return null; }
    }
}

/// <summary>重放结果。</summary>
public sealed class ReplayResult
{
    /// <summary>是否已写入共享流。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因；成功时为 <c>null</c>。</summary>
    public String Reason { get; init; }

    /// <summary>本次重放生成的新消息 ID。</summary>
    public String MessageId { get; init; }

    /// <summary>原消息 ID（写入 <c>replayOf</c>）。</summary>
    public String ReplayOf { get; init; }

    /// <summary>幂等键（与原报文一致，消费端据此收敛）。</summary>
    public String EventKey { get; init; }

    /// <summary>Stream 消息 ID。</summary>
    public String StreamId { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="message">已写入的消息体。</param>
    /// <param name="streamId">XADD 返回的消息 ID。</param>
    /// <returns>成功结果。</returns>
    public static ReplayResult Ok(StreamMessage message, String streamId) => new()
    {
        Success = true,
        MessageId = message.MessageId,
        ReplayOf = message.ReplayOf,
        EventKey = message.EventKey,
        StreamId = streamId,
    };

    /// <summary>构造失败结果。</summary>
    /// <param name="reason">失败原因（面向运维，不含密钥）。</param>
    /// <returns>失败结果。</returns>
    public static ReplayResult Fail(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>后台重放：把已落库报文或死信重新写入共享流（FR-24、FR-12、FR-15）。</summary>
/// <remarks>
/// 重放的语义是「<b>再发一次，不覆盖任何已落库数据</b>」：
/// ① 生成新的 <c>MessageId</c>，<c>EventKey</c> 保持与原报文一致，因此消费端命中幂等后只累加
///    <c>RepeatCount</c>，已落库的聚合值不会被改写；死信记录（<c>ProcessStatus=Dead</c>）重放后
///    会重新执行 handler 并收敛为「落库一次」；
/// ② 消息写入共享流 <c>XADD</c> 一次，<b>所有消费组</b>各拿一份（外部系统同样能看到，带 <c>replayOf</c> 可追溯）；
/// ③ 纯日志模式（<c>RedisEnabled=false</c>）没有消费者，重放无意义，直接拒绝并提示改用 <c>--replay</c> 补录；
/// ④ 每次重放都写审计日志（操作人、幂等键、新旧消息 ID），死信重放同时回写 <c>ReplayStatus</c>。
/// </remarks>
public sealed class EventReplayService
{
    private readonly EfbmOptions _options;
    private readonly IEventQueue _queue;

    /// <summary>构造重放服务。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="queue">共享上报流。</param>
    public EventReplayService(EfbmOptions options, IEventQueue queue)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>重放一条原始报文。</summary>
    /// <param name="id">原始报文主键。</param>
    /// <param name="operatorName">操作人（后台登录名）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重放结果。</returns>
    public async Task<ReplayResult> ReplayEventAsync(Int64 id, String operatorName, CancellationToken cancellationToken = default)
    {
        var entity = DeviceEventRaw.FindById(id);
        if (entity == null) return ReplayResult.Fail("记录不存在");

        var blocked = CheckEnabled();
        if (blocked != null) return blocked;

        if (String.IsNullOrWhiteSpace(entity.RawJson)) return ReplayResult.Fail("记录缺少报文原文，无法重放");

        var message = new StreamMessage
        {
            MessageId = Guid.NewGuid().ToString("D"),
            EventKey = entity.EventKey,
            Event = entity.Event,
            DeviceId = entity.DeviceId,
            Timestamp = entity.EventTimeRaw,
            RawJson = entity.RawJson,
            ReceivedAt = entity.ReceivedAt == DateTime.MinValue ? DateTime.UtcNow : entity.ReceivedAt,
            SourceIp = entity.SourceIp,
            // 已是重放产生的记录时，继续指向最初的原消息，避免链条越套越深
            ReplayOf = String.IsNullOrWhiteSpace(entity.ReplayOf) ? entity.MessageId : entity.ReplayOf,
        };

        return await AddAsync(message, operatorName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>重放一条死信。</summary>
    /// <param name="id">死信主键。</param>
    /// <param name="operatorName">操作人（后台登录名）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重放结果。</returns>
    public async Task<ReplayResult> ReplayDeadLetterAsync(Int64 id, String operatorName, CancellationToken cancellationToken = default)
    {
        var entity = DeadLetter.FindById(id);
        if (entity == null) return ReplayResult.Fail("死信记录不存在");

        var blocked = CheckEnabled();
        if (blocked != null) return blocked;

        // 死信存的是 Stream 消息体 JSON（原始报文在其 rawJson 内），损坏则无法还原
        var original = EventIngestService.Deserialize(entity.RawJson);
        if (original == null) return ReplayResult.Fail("死信消息体已损坏，无法还原为可重放的消息");

        var message = new StreamMessage
        {
            MessageId = Guid.NewGuid().ToString("D"),
            EventKey = String.IsNullOrWhiteSpace(original.EventKey) ? entity.EventKey : original.EventKey,
            Event = original.Event,
            DeviceId = original.DeviceId,
            Timestamp = original.Timestamp,
            RawJson = original.RawJson,
            ReceivedAt = original.ReceivedAt == DateTime.MinValue ? DateTime.UtcNow : original.ReceivedAt,
            SourceIp = original.SourceIp,
            ReplayOf = String.IsNullOrWhiteSpace(original.MessageId) ? entity.MessageId : original.MessageId,
        };

        var result = await AddAsync(message, operatorName, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return result;

        // 写入成功才记录重放留痕；失败保持未重放，运维可再试
        entity.ReplayStatus = 1;
        entity.ReplayAt = DateTime.Now;
        entity.ReplayBy = operatorName;
        entity.Update();

        return result;
    }

    /// <summary>手动标记一条原始报文的处理状态（FR-15）。</summary>
    /// <param name="id">原始报文主键。</param>
    /// <param name="handleStatus">处理状态：0 未处理 / 1 已处理 / 2 忽略。</param>
    /// <param name="remark">处理备注。</param>
    /// <param name="operatorName">操作人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否标记成功。</returns>
    public Task<Boolean> MarkAsync(Int64 id, Int32 handleStatus, String remark, String operatorName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = DeviceEventRaw.FindById(id);
        if (entity == null) return Task.FromResult(false);

        entity.HandleStatus = handleStatus;
        entity.HandleRemark = remark;
        entity.HandleBy = operatorName;
        entity.HandleAt = DateTime.Now;
        entity.Update();

        XTrace.WriteLine("后台标记报文：id={0} eventKey={1} 状态={2} 操作人={3} 备注={4}",
            entity.Id, entity.EventKey, handleStatus, operatorName, remark);

        return Task.FromResult(true);
    }

    /// <summary>校验重放前置条件：共享流必须启用，否则重放的消息没有消费者。</summary>
    private ReplayResult CheckEnabled()
    {
        if (!_options.RedisEnabled)
            return ReplayResult.Fail("Redis 未启用（EFBM:RedisEnabled=false）：重放需要写入共享流；纯日志模式请用 --replay 补录历史日志");

        return null;
    }

    /// <summary>写入共享流并记录审计日志。</summary>
    private async Task<ReplayResult> AddAsync(StreamMessage message, String operatorName, CancellationToken cancellationToken)
    {
        String streamId;
        try
        {
            streamId = await _queue.AddAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 队列不可用：如实返回原因，不谎报成功；重试仍由运维发起
            XTrace.WriteLine("后台重放失败：eventKey={0} 操作人={1} {2}: {3}",
                message.EventKey, operatorName, ex.GetType().Name, ex.Message);
            return ReplayResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }

        XTrace.WriteLine("后台重放已入流：eventKey={0} 新messageId={1} 原messageId={2} streamId={3} 操作人={4}",
            message.EventKey, message.MessageId, message.ReplayOf, streamId, operatorName);

        return ReplayResult.Ok(message, streamId);
    }
}
