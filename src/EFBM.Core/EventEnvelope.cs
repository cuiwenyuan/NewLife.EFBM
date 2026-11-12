namespace EFBM.Core;

/// <summary>一次上报的解析结果（信封 + 接收端生成字段）。</summary>
/// <remarks>
/// 本类型是<b>解析产物而非反序列化目标</b>：接收层用 <c>JsonDocument</c> 按名取值，
/// <c>data</c> 以原始 JSON 字符串承载（<see cref="DataJson"/>），禁止用强类型 DTO 反序列化，
/// 否则 <c>System.Text.Json</c> 会静默丢弃设备侧新增字段（需求说明 §3.7、FR-25）。
/// </remarks>
public class EventEnvelope
{
    /// <summary>事件类型，来自 <c>event</c>；未知值原样保留并走兜底 handler。</summary>
    public String Event { get; set; }

    /// <summary>设备编号，来自 <c>deviceId</c>。</summary>
    public String DeviceId { get; set; }

    /// <summary>设备侧时间<b>原始字符串</b>，参与 <c>EventKey</c> 计算，不得先做时区归一化。</summary>
    public String TimestampRaw { get; set; }

    /// <summary>设备侧事件时间，转换后的 UTC 值；无法解析时由接收层用 <c>ReceivedAt</c> 兜底。</summary>
    public DateTime EventTimeUtc { get; set; }

    /// <summary><c>data</c> 节点原文；无 <c>data</c> 时为空字符串。</summary>
    public String DataJson { get; set; }

    /// <summary>未识别字段的 JSON 对象（来自 <c>data</c>），字段固化前的临时载体。</summary>
    public String ExtJson { get; set; }

    /// <summary>未识别字段名清单（逗号分隔），含顶层未知字段，供后台新增字段监测汇总。</summary>
    public String UnknownFields { get; set; }

    /// <summary>未经改写的原始报文全文，长期真相源。</summary>
    public String RawJson { get; set; }

    /// <summary>接收端生成的消息追踪 ID（UUID），不参与去重。</summary>
    public String MessageId { get; set; }

    /// <summary>全链路幂等键 <c>SHA1(event|deviceId|timestamp)</c>。</summary>
    public String EventKey { get; set; }

    /// <summary>来源 IP，反向代理场景取 <c>X-Forwarded-For</c>。</summary>
    public String SourceIp { get; set; }

    /// <summary>服务端接收时间 UTC。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>解析版本，当前为 <see cref="EfbmDefaults.ParseVersion"/>。</summary>
    public String ParseVersion { get; set; } = EfbmDefaults.ParseVersion;

    /// <summary>解析告警编码（逗号分隔），见 <see cref="ParseWarnings"/>。</summary>
    public String ParseWarning { get; set; }

    /// <summary>重放来源 <c>messageId</c>；首次上报为空。</summary>
    public String ReplayOf { get; set; }
}
