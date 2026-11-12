namespace EFBM.Core;

/// <summary>写入 Redis Stream <c>efbm:events</c> 的消息体（需求说明 §3.6）。</summary>
/// <remarks>
/// 一次 <c>XADD</c>、每个消费组各拿一份全量。属性名在序列化时使用 camelCase，
/// 与《Doc/上报字段字典.md》§2.1 的字段名一致；<c>rawJson</c> 必须与设备原始报文逐字一致。
/// </remarks>
public class StreamMessage
{
    /// <summary>消息追踪 ID（UUID）。</summary>
    public String MessageId { get; set; }

    /// <summary>全链路幂等键，外部系统据此去重。</summary>
    public String EventKey { get; set; }

    /// <summary>事件类型。</summary>
    public String Event { get; set; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>设备侧时间原始字符串。</summary>
    public String Timestamp { get; set; }

    /// <summary>未经改写的原始报文全文，新字段由此自动透传给所有消费组。</summary>
    public String RawJson { get; set; }

    /// <summary>服务端接收时间 UTC。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>来源 IP。</summary>
    public String SourceIp { get; set; }

    /// <summary>重放时指向原 <c>messageId</c>，首次为空。</summary>
    public String ReplayOf { get; set; }

    /// <summary>由解析后的信封构造 Stream 消息体。</summary>
    /// <param name="envelope">接收层解析结果。</param>
    /// <returns>可直接 <c>XADD</c> 的消息体。</returns>
    public static StreamMessage FromEnvelope(EventEnvelope envelope)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));

        return new StreamMessage
        {
            MessageId = envelope.MessageId,
            EventKey = envelope.EventKey,
            Event = envelope.Event,
            DeviceId = envelope.DeviceId,
            Timestamp = envelope.TimestampRaw,
            RawJson = envelope.RawJson,
            ReceivedAt = envelope.ReceivedAt,
            SourceIp = envelope.SourceIp,
            ReplayOf = envelope.ReplayOf,
        };
    }
}
