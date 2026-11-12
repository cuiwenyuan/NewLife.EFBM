namespace EFBM.Core;

/// <summary>单条消息的消费结果，决定消费者是否 <c>XACK</c>。</summary>
public enum PersistOutcome
{
    /// <summary>首次落库完成：原始报文 + 聚合表全部写入，应确认。</summary>
    Persisted = 0,

    /// <summary>重复投递（PEL 重投 / 组重建重放 / 人工重放）：只累加 RepeatCount，应确认。</summary>
    Duplicate = 1,

    /// <summary>转入死信（重解析失败或重试超限）：已留档并告警，应确认。</summary>
    DeadLettered = 2,

    /// <summary>处理失败：由消费者重试，<b>不得确认</b>，消息留在 PEL 等待重投。</summary>
    Failed = 3,
}

/// <summary>消费落库编排：重解析 → EventKey 幂等 → 设备登记 → 分发 handler → 状态置位（FR-5~FR-9）。</summary>
/// <remarks>
/// 本服务<b>不依赖 ASP.NET Core 与 XCode 实体</b>，存储全部走抽象接口，与接收链路对称（FR-20）。
/// 消费者（<c>PersistConsumerService</c> / 日志回放工具）只负责「取消息 → 调本类 → 按结果 Ack」。
///
/// 幂等与恢复语义（需求说明 §5.2）：
/// ① 首次：<c>EventKey</c> 无记录 → 插入 <see cref="RawProcessStatus.Pending"/> → 执行 handler →
///    全部成功置 <see cref="RawProcessStatus.Persisted"/>；任一步抛异常则消息不确认、留 PEL 重投；
/// ② 重投命中 <c>Persisted</c>：仅 <c>RepeatCount++</c>，不重复写任何业务表（不覆盖原记录，FR-24）；
/// ③ 重投命中 <c>Pending</c>：上次处理到一半（进程崩溃等），重新执行 handler——各 handler 按
///    <c>EventKey</c> 查重且聚合值取大收敛，重放最终收敛为「落库一次」；
/// ④ 并发多实例同时插入同一 <c>EventKey</c>：唯一索引让后插者拿到
///    <see cref="DuplicateEventKeyException"/>，重新查询走 ② 或 ③，不需要分布式锁。
///
/// Stream 消息体只有 §3.6 的 9 个字段；<c>ParseWarning</c> / <c>ExtJson</c> / <c>UnknownFields</c>
/// 由本类用与接收层同一个 <see cref="EventEnvelopeParser"/> 从 <c>rawJson</c> 重解析得到。
/// </remarks>
public sealed class EventPersistService
{
    private readonly EfbmOptions _options;
    private readonly IRawEventStore _raw;
    private readonly IDeadLetterStore _deadLetter;
    private readonly IDeviceRegistry _devices;
    private readonly EventHandlerRegistry _registry;

    /// <summary>构造消费落库编排服务。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="raw">原始报文存储。</param>
    /// <param name="deadLetter">死信存储。</param>
    /// <param name="devices">设备登记。</param>
    /// <param name="registry">事件处理器注册表。</param>
    public EventPersistService(EfbmOptions options, IRawEventStore raw, IDeadLetterStore deadLetter, IDeviceRegistry devices, EventHandlerRegistry registry)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _raw = raw ?? throw new ArgumentNullException(nameof(raw));
        _deadLetter = deadLetter ?? throw new ArgumentNullException(nameof(deadLetter));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>处理一条 Stream 消息；任何 IO 失败都以 <c>false</c> / 异常形式交给上层决定重试。</summary>
    /// <param name="message">Stream 消息体。</param>
    /// <param name="streamId">Stream 消息 ID，落库补记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处理结果；<see cref="PersistOutcome.Failed"/> 时异常信息已在调用方上下文，消息不得确认。</returns>
    public async Task<PersistOutcome> PersistAsync(StreamMessage message, String streamId, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        // ① 重解析：与接收层同一个解析器。入流前已通过严格校验，此时失败说明消息体被损坏，
        //    属不可恢复错误 → 死信 + 确认，不让它永远卡在 PEL 里
        var context = new ParseContext
        {
            MessageId = message.MessageId,
            ReceivedAt = message.ReceivedAt == DateTime.MinValue ? DateTime.UtcNow : message.ReceivedAt,
            SourceIp = message.SourceIp,
            ReplayOf = message.ReplayOf,
        };
        if (!EventEnvelopeParser.TryParse(message.RawJson, context, out var envelope, out var parseError))
        {
            await DeadLetterCoreAsync(message, streamId, 0, $"重解析失败（消息体损坏）: {parseError}", cancellationToken).ConfigureAwait(false);
            return PersistOutcome.DeadLettered;
        }

        // ② EventKey 幂等判定
        var existing = await FindOrInsertAsync(envelope, streamId, cancellationToken).ConfigureAwait(false);
        if (existing != null && existing.ProcessStatus == RawProcessStatus.Persisted)
        {
            // 重复投递 / PEL 重投 / 组重建重放 / 人工重放：只观测，不覆盖任何已落库数据
            await _raw.IncrementRepeatAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            return PersistOutcome.Duplicate;
        }

        // ③ 设备自动登记（FR-13）：失败会抛异常 → 不确认 → 重投，登记最终一致
        await _devices.EnsureDeviceAsync(envelope.DeviceId, envelope.EventTimeUtc, cancellationToken).ConfigureAwait(false);

        // ④ 分发 handler：兜底 handler 保证未知事件也有归宿（FR-26）
        await _registry.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);

        // ⑤ 全部成功才置位：此后重投只走 ②，业务表不会被二次写入
        await _raw.MarkPersistedAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        return PersistOutcome.Persisted;
    }

    /// <summary>把一条处理不了的消息转入死信表并标记原始报文（若已插入）。</summary>
    /// <param name="message">Stream 消息体。</param>
    /// <param name="streamId">Stream 消息 ID。</param>
    /// <param name="retryCount">累计重试次数。</param>
    /// <param name="error">最后一次错误摘要（不含密钥）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；死信写入失败抛异常，此时调用方<b>不应确认</b>消息，下一轮继续尝试。</returns>
    public async Task DeadLetterAsync(StreamMessage message, String streamId, Int32 retryCount, String error, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        await DeadLetterCoreAsync(message, streamId, retryCount, error, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>死信落库的公共实现：先写死信表，再把已插入的原始报文置 Dead。</summary>
    private async Task DeadLetterCoreAsync(StreamMessage message, String streamId, Int32 retryCount, String error, CancellationToken cancellationToken)
    {
        // 消息体 JSON 原样入死信，重放时可完整还原（设备原文在其 rawJson 字段内）
        await _deadLetter.SaveAsync(new DeadLetterRecord
        {
            MessageId = message.MessageId,
            StreamId = streamId,
            EventKey = message.EventKey,
            RawJson = EventIngestService.Serialize(message),
            RetryCount = retryCount,
            LastError = error,
        }, cancellationToken).ConfigureAwait(false);

        // 已插入的原始报文置死信状态；未插入（重解析失败）则跳过
        if (!String.IsNullOrEmpty(message.EventKey))
        {
            var raw = await _raw.FindByEventKeyAsync(message.EventKey, cancellationToken).ConfigureAwait(false);
            if (raw != null && raw.ProcessStatus != RawProcessStatus.Persisted)
                await _raw.MarkDeadAsync(raw.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>查找或插入原始报文；并发撞唯一键时重新查询接管。</summary>
    private async Task<RawEventRecord> FindOrInsertAsync(EventEnvelope envelope, String streamId, CancellationToken cancellationToken)
    {
        var existing = await _raw.FindByEventKeyAsync(envelope.EventKey, cancellationToken).ConfigureAwait(false);
        if (existing != null) return existing;

        try
        {
            return await _raw.InsertAsync(RawEventRecord.From(envelope, streamId), cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateEventKeyException)
        {
            // 多实例并发插入同一 EventKey：唯一索引兜底，后插者重新查询走重复 / 接管路径
            existing = await _raw.FindByEventKeyAsync(envelope.EventKey, cancellationToken).ConfigureAwait(false);
            if (existing != null) return existing;

            // 插入报「重复键」但查不到记录，属于存储实现异常，上抛让消息不确认
            throw new InvalidOperationException($"EventKey 插入冲突但查询不到: {envelope.EventKey}");
        }
    }
}
