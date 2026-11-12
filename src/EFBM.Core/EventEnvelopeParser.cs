using System.Globalization;
using System.Text.Json;

namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>解析上下文：接收端生成、与设备报文无关的字段。</summary>
public sealed class ParseContext
{
    /// <summary>消息追踪 ID（UUID）；为空时由解析器生成。</summary>
    public String MessageId { get; set; }

    /// <summary>服务端接收时间 UTC；为 <see cref="DateTime.MinValue"/> 时取 <c>DateTime.UtcNow</c>。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>来源 IP，反向代理场景取 <c>X-Forwarded-For</c> 首段。</summary>
    public String SourceIp { get; set; }

    /// <summary>重放来源 <c>messageId</c>；首次上报为空。</summary>
    public String ReplayOf { get; set; }
}

/// <summary>严格解析信封 + 宽容解析业务字段（需求说明 §3.5、§3.7，FR-3 / FR-25）。</summary>
/// <remarks>
/// <b>只拦两类错误</b>：JSON 无法解析（含尾随逗号 / 注释 / 非对象根节点），
/// 以及 <c>event</c> / <c>deviceId</c> / <c>timestamp</c> 三个必填字段缺失、为空或不是字符串。
/// 其余一切情况（未知事件、未知状态、类型不符、嵌套对象、数组、新增字段、时间偏差）都不拒绝，
/// 只累加 <c>ParseWarning</c> 编码后正常入流——设备失败不重发，多拒绝一条就是永久缺口。
///
/// <c>data</c> 以 <see cref="JsonElement"/> 树按名取值并保留 <c>GetRawText()</c> 原文，
/// <b>禁止用强类型 DTO 反序列化</b>：否则 System.Text.Json 会静默丢弃设备侧新增字段。
///
/// 本类是纯函数，P2 接收层与 P3 消费者（从 <c>rawJson</c> 重解析）共用，
/// 因此 Stream 消息体无需新增 parseWarning / extJson 等字段，外部系统的契约保持不变。
/// </remarks>
/// <example>
/// <code>
/// if (!EventEnvelopeParser.TryParse(rawJson, new ParseContext { SourceIp = ip }, out var env, out var error))
///     return BadRequest(error);
/// var message = StreamMessage.FromEnvelope(env);
/// </code>
/// </example>
public static class EventEnvelopeParser
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    private static readonly String[] EnvelopeFields = ["event", "deviceId", "timestamp", "data"];

    /// <summary>尝试解析一次上报报文。</summary>
    /// <param name="rawJson">未经改写的原始报文全文。</param>
    /// <param name="context">接收端生成的上下文；可为 <c>null</c>。</param>
    /// <param name="envelope">解析成功时输出的信封。</param>
    /// <param name="error">解析失败时输出的可记日志原因（不含密钥）。</param>
    /// <returns>报文可入流返回 <c>true</c>；格式非法返回 <c>false</c>，由接入层判 400 丢弃。</returns>
    public static Boolean TryParse(String rawJson, ParseContext context, out EventEnvelope envelope, out String error)
    {
        envelope = null;
        error = null;

        if (String.IsNullOrWhiteSpace(rawJson))
        {
            error = "请求体为空";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson, DocOptions);
        }
        catch (JsonException ex)
        {
            // 尾随逗号、注释、字符串未闭合等：格式由请求方保证，直接丢弃不猜测
            error = $"JSON 格式非法: {ex.Message}";
            return false;
        }
        catch (ArgumentException ex)
        {
            error = $"请求体非法: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "报文根节点必须是 JSON 对象";
                return false;
            }

            var top = ReadProperties(root);
            if (!TryGetRequiredString(top, "event", out var eventName, out error)) return false;
            if (!TryGetRequiredString(top, "deviceId", out var deviceId, out error)) return false;
            if (!TryGetRequiredString(top, "timestamp", out var timestampRaw, out error)) return false;

            var warnings = new List<String>();
            var unknown = new List<String>();
            void Warn(String code) { if (!warnings.Contains(code)) warnings.Add(code); }

            // 顶层未登记字段同样是「新增字段」：原样留存、计入 UnknownFields
            foreach (var name in top.Names)
            {
                if (Array.IndexOf(EnvelopeFields, name) >= 0) continue;
                Warn(ParseWarnings.UnknownField);
                if (!unknown.Contains(name)) unknown.Add(name);
            }
            if (top.HasDuplicate) Warn(ParseWarnings.DuplicateKey);

            if (!EventNames.IsKnown(eventName)) Warn(ParseWarnings.UnknownEvent);

            var dataJson = String.Empty;
            var extJson = String.Empty;
            if (top.Last.TryGetValue("data", out var dataElement))
            {
                // GetRawText 保留原始顺序 / 空格 / 数值写法，是 DataJson 与 RawJson 的兜底真相源
                dataJson = dataElement.GetRawText();
                if (dataElement.ValueKind != JsonValueKind.Object)
                {
                    Warn(ParseWarnings.DataNotObject);
                }
                else
                {
                    var data = ReadProperties(dataElement);
                    if (data.HasDuplicate) Warn(ParseWarnings.DuplicateKey);

                    var fields = EventFieldRegistry.GetFields(eventName);
                    var ext = new Dictionary<String, JsonElement>(StringComparer.Ordinal);
                    foreach (var pair in data.Last)
                    {
                        var field = fields.FirstOrDefault(e => String.Equals(e.Name, pair.Key, StringComparison.Ordinal));
                        if (field == null)
                        {
                            // 未知字段整体（含嵌套对象 / 数组）写入 ExtJson，字段清单供后台监测
                            Warn(ParseWarnings.UnknownField);
                            if (!unknown.Contains(pair.Key)) unknown.Add(pair.Key);
                            ext[pair.Key] = pair.Value;
                            continue;
                        }

                        CheckKnownField(field, pair.Value, Warn);
                    }

                    foreach (var field in fields)
                    {
                        if (field.Required && !data.Last.ContainsKey(field.Name)) Warn(ParseWarnings.MissingField);
                    }

                    // 该事件业务上应有 status 时，缺失 / 空值 / 未命中字典都只打 W104
                    if (fields.Any(e => e.Name == "status" && e.Required))
                    {
                        data.Last.TryGetValue("status", out var statusElement);
                        var status = statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() : null;
                        if (String.IsNullOrWhiteSpace(status) || !EventFieldRegistry.IsKnownStatus(status))
                            Warn(ParseWarnings.UnknownStatus);
                    }

                    if (ext.Count > 0) extJson = JsonSerializer.Serialize(ext);
                }
            }
            else
            {
                // FAULT_CLEARED 与未知事件本来就无 data；已知事件缺业务字段只打 W100
                foreach (var field in EventFieldRegistry.GetFields(eventName))
                {
                    if (field.Required) Warn(ParseWarnings.MissingField);
                }
            }

            var receivedAt = context?.ReceivedAt ?? DateTime.MinValue;
            if (receivedAt == DateTime.MinValue) receivedAt = DateTime.UtcNow;

            DateTime eventTimeUtc;
            if (DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                eventTimeUtc = dto.UtcDateTime;
            else
            {
                Warn(ParseWarnings.BadTimestamp);
                eventTimeUtc = receivedAt;
            }
            if (!HasTimeZoneOffset(timestampRaw)) Warn(ParseWarnings.BadTimestamp);

            envelope = new EventEnvelope
            {
                Event = eventName,
                DeviceId = deviceId,
                TimestampRaw = timestampRaw,
                EventTimeUtc = eventTimeUtc,
                DataJson = dataJson,
                RawJson = rawJson,
                ExtJson = extJson,
                UnknownFields = unknown.Count > 0 ? String.Join(",", unknown) : String.Empty,
                MessageId = String.IsNullOrEmpty(context?.MessageId) ? Guid.NewGuid().ToString("D") : context.MessageId,
                EventKey = EventKeyBuilder.Compute(eventName, deviceId, timestampRaw),
                SourceIp = context?.SourceIp,
                ReceivedAt = receivedAt,
                ParseVersion = EfbmDefaults.ParseVersion,
                ParseWarning = ParseWarnings.Join(warnings.ToArray()),
                ReplayOf = context?.ReplayOf,
            };

            return true;
        }
    }

    /// <summary>枚举对象属性，保留出现顺序并检测重复键；重复时按「最后一次取值」为准（W108）。</summary>
    /// <param name="element">JSON 对象节点。</param>
    /// <returns>属性集合。</returns>
    private static Properties ReadProperties(JsonElement element)
    {
        var result = new Properties();
        foreach (var prop in element.EnumerateObject())
        {
            if (result.Last.ContainsKey(prop.Name)) result.HasDuplicate = true;
            result.Last[prop.Name] = prop.Value;
            result.Names.Add(prop.Name);
        }
        return result;
    }

    /// <summary>读取三个必填字符串字段；类型不符 / 空值一律视为格式非法。</summary>
    /// <param name="props">顶层属性集合。</param>
    /// <param name="name">字段名。</param>
    /// <param name="value">输出的字符串原值。</param>
    /// <param name="error">输出的错误原因。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    private static Boolean TryGetRequiredString(Properties props, String name, out String value, out String error)
    {
        value = null;
        error = null;

        if (!props.Last.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            error = $"必填字段 {name} 缺失或不是字符串";
            return false;
        }

        value = element.GetString();
        if (String.IsNullOrWhiteSpace(value))
        {
            error = $"必填字段 {name} 为空";
            return false;
        }

        return true;
    }

    /// <summary>已登记字段的宽松类型校验：只打告警，绝不拒绝。</summary>
    /// <param name="field">字段字典元数据。</param>
    /// <param name="value">报文中该字段的 JSON 节点。</param>
    /// <param name="warn">告警收集委托。</param>
    private static void CheckKnownField(EventFieldRegistry.Field field, JsonElement value, Action<String> warn)
    {
        if (field.JsonType == "String")
        {
            if (value.ValueKind != JsonValueKind.String) warn(ParseWarnings.TypeMismatch);
            return;
        }

        if (field.JsonType == "Number(Int32)")
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                warn(ParseWarnings.TypeMismatch);
                return;
            }
            if (!value.TryGetInt32(out var number) || number < 0) warn(ParseWarnings.BadNumber);
            return;
        }

        if (field.JsonType == "Object" && value.ValueKind != JsonValueKind.Object)
            warn(ParseWarnings.TypeMismatch);
    }

    /// <summary>判断 ISO 8601 字符串是否显式带时区偏移（Z 或 ±HH:mm）。</summary>
    /// <param name="value">设备侧时间原始字符串。</param>
    /// <returns>带偏移返回 <c>true</c>；无偏移打 W105 但仍按解析值入库。</returns>
    private static Boolean HasTimeZoneOffset(String value)
    {
        if (String.IsNullOrEmpty(value)) return false;
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;

        var t = value.IndexOfAny(['T', 't']);
        if (t < 0) return false;

        var tail = value[(t + 1)..];
        var p = tail.IndexOfAny(['+', '-']);
        return p >= 0 && tail.Length - p >= 5;   // +08:00 或 +0800
    }

    /// <summary>对象属性集合：最后一次同名取值 + 原始顺序 + 是否重复。</summary>
    private sealed class Properties
    {
        /// <summary>按字段名索引，重复键保留最后一次取值。</summary>
        public Dictionary<String, JsonElement> Last { get; } = new(StringComparer.Ordinal);

        /// <summary>属性出现顺序。</summary>
        public List<String> Names { get; } = [];

        /// <summary>是否存在重复键。</summary>
        public Boolean HasDuplicate { get; set; }
    }
}
