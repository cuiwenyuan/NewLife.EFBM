namespace EFBM.Core;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>一次 <c>POST /api/events</c> 的请求日志条目；Beta 阶段以文本日志作为数据的暂存真相源。</summary>
/// <remarks>
/// <b>绝不写入 AppSecret 与 <c>Authorization</c></b>：只记录脱敏后的 AppKey 与设备号。
/// <see cref="RawJson"/> 保留请求体原文（逐字符），设备失败不重发时靠它人工补录。
/// 字段全部可空，解析失败 / 读取异常也要能落一条记录。
/// </remarks>
public sealed class RequestLogEntry
{
    /// <summary>服务端接收时间（UTC）。</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>链路追踪标识，便于与框架日志对齐；Beta 用 <c>messageId</c> 或请求头 <c>X-Request-Id</c>。</summary>
    public String TraceId { get; set; }

    /// <summary>HTTP 方法，固定为 <c>POST</c>。</summary>
    public String Method { get; set; }

    /// <summary>请求路径，固定为 <c>/api/events</c>。</summary>
    public String Path { get; set; }

    /// <summary>实际协议（<c>http</c> / <c>https</c>）；外网收到明文时可据此排查代理配置。</summary>
    public String Scheme { get; set; }

    /// <summary>请求 Content-Type。</summary>
    public String ContentType { get; set; }

    /// <summary>设备侧 User-Agent，排障时用于区分设备型号 / 固件。</summary>
    public String UserAgent { get; set; }

    /// <summary>来源 IP（可信代理时取 <c>X-Forwarded-For</c> 首段）。</summary>
    public String SourceIp { get; set; }

    /// <summary>报文中的设备号；解析失败时为空。</summary>
    public String DeviceId { get; set; }

    /// <summary>报文中的事件名；解析失败时为空。</summary>
    public String Event { get; set; }

    /// <summary>消息追踪 ID（UUID），成功解析后生成。</summary>
    public String MessageId { get; set; }

    /// <summary>幂等键 <c>SHA1(event|deviceId|timestamp)</c>。</summary>
    public String EventKey { get; set; }

    /// <summary>本次请求实际使用的认证模式。</summary>
    public String AuthMode { get; set; }

    /// <summary>脱敏后的 AppKey（保留首尾少量字符）。</summary>
    public String AppKey { get; set; }

    /// <summary>HTTP 状态码。</summary>
    public Int32 StatusCode { get; set; }

    /// <summary>业务码（成功为 0）。</summary>
    public Int32 Code { get; set; }

    /// <summary>响应文案。</summary>
    public String Message { get; set; }

    /// <summary>是否已写入共享 Stream。</summary>
    public Boolean Queued { get; set; }

    /// <summary>是否走了本地兜底暂存。</summary>
    public Boolean Fallback { get; set; }

    /// <summary>告警 / 拒绝原因；正常入流为空。</summary>
    public String Warning { get; set; }

    /// <summary>解析告警编码（逗号分隔，见 <see cref="ParseWarnings"/>）；用于发现设备新增字段与未知状态。</summary>
    public String ParseWarning { get; set; }

    /// <summary>同步链路耗时（毫秒），含鉴权查库与入流。</summary>
    public Int64 ElapsedMs { get; set; }

    /// <summary>请求体原文；超限或未读取时为说明性占位（如 <c>&lt;omitted:10485760 bytes&gt;</c>）。</summary>
    public String RawJson { get; set; }

    /// <summary>脱敏 AppKey：保留前 4 位与后 2 位，长度不足则整体掩码。</summary>
    /// <param name="appKey">原始 AppKey。</param>
    /// <returns>可安全写入日志的字符串。</returns>
    public static String MaskKey(String appKey)
    {
        if (String.IsNullOrEmpty(appKey)) return null;
        if (appKey.Length <= 8) return "****";

        return $"{appKey.Substring(0, 4)}****{appKey.Substring(appKey.Length - 2)}";
    }

    /// <summary>序列化为单行 JSON（JSONL 的一行）；字段顺序固定、空值省略。</summary>
    /// <remarks>
    /// 由 Core 统一提供，保证「写入」与「回放工具」使用同一套字段名；
    /// 因此格式改动必须同步《Doc/Beta运行手册.md》§5 的字段表。
    /// </remarks>
    /// <param name="includeRaw">是否包含请求体原文；关闭后无法离线补录。</param>
    /// <returns>单行、无换行的 JSON 文本。</returns>
    public String ToJson(Boolean includeRaw = true)
    {
        using var stream = new MemoryStream(512);
        // 日志是给人看也给 grep 用的：不转义中文与引号（\u5931 这类编码没法排查），控制字符仍按 JSON 规则转义
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", ReceivedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            Write(writer, "trace", TraceId);
            Write(writer, "method", Method);
            Write(writer, "path", Path);
            Write(writer, "scheme", Scheme);
            Write(writer, "ip", SourceIp);
            Write(writer, "ua", UserAgent);
            Write(writer, "ctype", ContentType);
            writer.WriteNumber("status", StatusCode);
            writer.WriteNumber("code", Code);
            Write(writer, "msg", Message);
            writer.WriteBoolean("queued", Queued);
            writer.WriteBoolean("fallback", Fallback);
            Write(writer, "messageId", MessageId);
            Write(writer, "eventKey", EventKey);
            Write(writer, "event", Event);
            Write(writer, "deviceId", DeviceId);
            Write(writer, "auth", AuthMode);
            Write(writer, "appKey", AppKey);
            Write(writer, "parseWarning", ParseWarning);
            Write(writer, "warn", Warning);
            writer.WriteNumber("ms", ElapsedMs);
            if (includeRaw) Write(writer, "raw", RawJson);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>写入非空字符串字段；空值直接省略，避免日志里满屏 <c>null</c>。</summary>
    /// <param name="writer">JSON 写入器。</param>
    /// <param name="name">字段名。</param>
    /// <param name="value">字段值。</param>
    private static void Write(Utf8JsonWriter writer, String name, String value)
    {
        if (!String.IsNullOrEmpty(value)) writer.WriteString(name, value);
    }
}

/// <summary>请求日志写入抽象；实现位于宿主层（当前为按日滚动的文本文件）。</summary>
/// <remarks>
/// 返回 <c>false</c> 表示这条记录<b>没有落到磁盘</b>，调用方据此判断「日志兜底」是否成立。
/// 实现<b>不得抛出异常</b>：日志故障不能反过来让接收链路失败。
/// </remarks>
public interface IRequestLog
{
    /// <summary>写入一条请求日志。</summary>
    /// <param name="entry">日志条目。</param>
    /// <param name="cancellationToken">取消令牌；为保证不丢数据，接收链路传入 <see cref="CancellationToken.None"/>。</param>
    /// <returns>成功落盘返回 <c>true</c>；未启用或写入失败返回 <c>false</c>。</returns>
    Task<Boolean> WriteAsync(RequestLogEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>请求日志运行状态，供 <c>/health</c> 与运维排查使用。</summary>
public sealed class RequestLogStatus
{
    /// <summary>是否启用。</summary>
    public Boolean Enabled { get; set; }

    /// <summary>日志目录绝对路径。</summary>
    public String Directory { get; set; }

    /// <summary>当前正在写入的文件绝对路径。</summary>
    public String CurrentFile { get; set; }

    /// <summary>累计成功写入条数（进程启动后）。</summary>
    public Int64 Written { get; set; }

    /// <summary>累计写入失败条数（进程启动后）。失败意味着该条数据没有磁盘留痕，需立即处理。</summary>
    public Int64 Failed { get; set; }

    /// <summary>最近一次错误摘要。</summary>
    public String LastError { get; set; }

    /// <summary>最近一次成功写入时间（UTC）。</summary>
    public DateTime LastWriteAt { get; set; }
}

/// <summary>请求日志状态探针；健康检查据此回答「日志还在不在写」。</summary>
public interface IRequestLogProbe
{
    /// <summary>读取状态快照；不得抛异常。</summary>
    /// <returns>状态快照。</returns>
    RequestLogStatus GetStatus();
}
