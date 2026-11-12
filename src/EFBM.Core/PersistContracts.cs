using System.Runtime.Serialization;

namespace EFBM.Core;

/// <summary>原始报文处理状态（<c>DeviceEventRaw.ProcessStatus</c>）。</summary>
public static class RawProcessStatus
{
    /// <summary>待处理：已插入但 handler 尚未全部完成（消费中断的重投据此接管）。</summary>
    public const Int32 Pending = 0;

    /// <summary>已落库：原始报文与各聚合表全部写入完成。</summary>
    public const Int32 Persisted = 1;

    /// <summary>死信：重试超限，已转入死信表等待人工重放。</summary>
    public const Int32 Dead = 2;

    /// <summary>已重放：后台人工重放后的新状态。</summary>
    public const Int32 Replayed = 3;
}

/// <summary>原始报文表的数据视图，Core 编排不依赖 XCode 实体（FR-20）。</summary>
public sealed class RawEventRecord
{
    /// <summary>主键；插入前为 0，由实现方回填。</summary>
    public Int64 Id { get; set; }

    /// <summary>全链路幂等键。</summary>
    public String EventKey { get; set; }

    /// <summary>消息追踪 ID。</summary>
    public String MessageId { get; set; }

    /// <summary>Redis Stream 消息 ID，消费时补记。</summary>
    public String StreamId { get; set; }

    /// <summary>事件类型。</summary>
    public String Event { get; set; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>设备侧时间原始字符串。</summary>
    public String EventTimeRaw { get; set; }

    /// <summary>设备侧事件时间 UTC。</summary>
    public DateTime EventTime { get; set; }

    /// <summary>未经改写的原始报文全文。</summary>
    public String RawJson { get; set; }

    /// <summary><c>data</c> 节点原文。</summary>
    public String DataJson { get; set; }

    /// <summary>未识别字段 JSON。</summary>
    public String ExtJson { get; set; }

    /// <summary>未识别字段名清单，逗号分隔。</summary>
    public String UnknownFields { get; set; }

    /// <summary>解析版本。</summary>
    public String ParseVersion { get; set; }

    /// <summary>解析告警编码。</summary>
    public String ParseWarning { get; set; }

    /// <summary>来源 IP。</summary>
    public String SourceIp { get; set; }

    /// <summary>服务端接收时间 UTC。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>处理状态，见 <see cref="RawProcessStatus"/>。</summary>
    public Int32 ProcessStatus { get; set; }

    /// <summary>同一 EventKey 重复投递次数（观测用，不影响幂等判定）。</summary>
    public Int32 RepeatCount { get; set; }

    /// <summary>重放来源 messageId。</summary>
    public String ReplayOf { get; set; }

    /// <summary>由解析结果构造待插入的原始报文记录。</summary>
    /// <param name="envelope">重解析得到的信封。</param>
    /// <param name="streamId">Stream 消息 ID。</param>
    /// <returns>待插入记录。</returns>
    public static RawEventRecord From(EventEnvelope envelope, String streamId) => new()
    {
        EventKey = envelope.EventKey,
        MessageId = envelope.MessageId,
        StreamId = streamId,
        Event = envelope.Event,
        DeviceId = envelope.DeviceId,
        EventTimeRaw = envelope.TimestampRaw,
        EventTime = envelope.EventTimeUtc,
        RawJson = envelope.RawJson,
        DataJson = envelope.DataJson,
        ExtJson = envelope.ExtJson,
        UnknownFields = envelope.UnknownFields,
        ParseVersion = envelope.ParseVersion,
        ParseWarning = envelope.ParseWarning,
        SourceIp = envelope.SourceIp,
        ReceivedAt = envelope.ReceivedAt,
        ProcessStatus = RawProcessStatus.Pending,
        ReplayOf = envelope.ReplayOf,
    };
}

/// <summary>并发插入同一 <c>EventKey</c> 时由实现方抛出（唯一索引冲突）。</summary>
[Serializable]
public sealed class DuplicateEventKeyException : Exception
{
    /// <summary>构造重复键异常。</summary>
    /// <param name="eventKey">冲突的幂等键。</param>
    public DuplicateEventKeyException(String eventKey) : base($"EventKey 已存在: {eventKey}") { EventKey = eventKey; }

    /// <summary>冲突的幂等键。</summary>
    public String EventKey { get; }

    /// <summary>序列化构造。</summary>
    /// <param name="info">序列化信息。</param>
    /// <param name="context">上下文。</param>
    private DuplicateEventKeyException(SerializationInfo info, StreamingContext context) : base(info, context)
        => EventKey = info.GetString(nameof(EventKey));
}

/// <summary>原始报文表访问抽象：幂等判定与状态流转的唯一入口（FR-6、FR-7）。</summary>
/// <remarks>
/// 实现<b>不必</b>把 Insert 与后续 handler 写入包在一个数据库事务里：EventKey 唯一约束 +
/// <see cref="RawProcessStatus.Pending"/> 状态让中断的消息在重投时被重新执行，
/// 各 handler 自身按 EventKey 查重，重放语义收敛为「最终落库一次」。
/// </remarks>
public interface IRawEventStore
{
    /// <summary>按幂等键查找原始报文。</summary>
    /// <param name="eventKey">幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录视图；不存在返回 <c>null</c>。</returns>
    Task<RawEventRecord> FindByEventKeyAsync(String eventKey, CancellationToken cancellationToken = default);

    /// <summary>插入一条待处理记录。</summary>
    /// <param name="record">记录视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>插入后的记录（含主键）；并发撞唯一索引时抛 <see cref="DuplicateEventKeyException"/>。</returns>
    Task<RawEventRecord> InsertAsync(RawEventRecord record, CancellationToken cancellationToken = default);

    /// <summary>标记已落库（handler 全部成功后调用）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task MarkPersistedAsync(Int64 id, CancellationToken cancellationToken = default);

    /// <summary>标记死信（重试超限转入死信表时调用）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task MarkDeadAsync(Int64 id, CancellationToken cancellationToken = default);

    /// <summary>重复投递观测：累加 <c>RepeatCount</c>，不改变任何业务数据。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task IncrementRepeatAsync(Int64 id, CancellationToken cancellationToken = default);
}

/// <summary>死信记录视图。</summary>
public sealed class DeadLetterRecord
{
    /// <summary>消息追踪 ID。</summary>
    public String MessageId { get; set; }

    /// <summary>Stream 消息 ID。</summary>
    public String StreamId { get; set; }

    /// <summary>幂等键。</summary>
    public String EventKey { get; set; }

    /// <summary>原始消息体 JSON（设备原文在其 rawJson 字段内）。</summary>
    public String RawJson { get; set; }

    /// <summary>累计重试次数。</summary>
    public Int32 RetryCount { get; set; }

    /// <summary>最后一次错误摘要。</summary>
    public String LastError { get; set; }
}

/// <summary>死信表访问抽象（FR-12：超限消息先入死信再确认，绝不自动丢弃）。</summary>
public interface IDeadLetterStore
{
    /// <summary>保存一条死信。</summary>
    /// <param name="record">死信记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；实现失败抛异常，由调用方决定是否仍确认消息。</returns>
    Task SaveAsync(DeadLetterRecord record, CancellationToken cancellationToken = default);
}

/// <summary>设备登记抽象：首次上报自动建设备，LastReportTime 只前进不后退（FR-13）。</summary>
public interface IDeviceRegistry
{
    /// <summary>确保设备存在并推进最后上报时间。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="eventTimeUtc">本条事件时间 UTC。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；自动登记关闭且设备不存在时静默跳过。</returns>
    Task EnsureDeviceAsync(String deviceId, DateTime eventTimeUtc, CancellationToken cancellationToken = default);
}
