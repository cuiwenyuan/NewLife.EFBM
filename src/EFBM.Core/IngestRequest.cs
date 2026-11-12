namespace EFBM.Core;

/// <summary>接入层交给接收服务的原始输入，不依赖 <c>HttpContext</c>（FR-20）。</summary>
public sealed class IngestRequest
{
    /// <summary>请求体原文，必须逐字保留到 <c>rawJson</c>。</summary>
    public String RawJson { get; set; }

    /// <summary>配置的 AppKey 请求头值。</summary>
    public String AppKey { get; set; }

    /// <summary>配置的 AppSecret 请求头值。</summary>
    public String AppSecret { get; set; }

    /// <summary>原始 <c>Authorization</c> 请求头，Basic 模式使用。</summary>
    public String Authorization { get; set; }

    /// <summary>来源 IP。</summary>
    public String SourceIp { get; set; }

    /// <summary>HTTP 方法；仅用于请求日志，接收逻辑不依赖。</summary>
    public String Method { get; set; }

    /// <summary>请求路径；仅用于请求日志。</summary>
    public String Path { get; set; }

    /// <summary>实际协议（http / https）；仅用于请求日志与外网明文排查。</summary>
    public String Scheme { get; set; }

    /// <summary>请求 Content-Type；仅用于请求日志。</summary>
    public String ContentType { get; set; }

    /// <summary>设备侧 User-Agent；仅用于请求日志。</summary>
    public String UserAgent { get; set; }

    /// <summary>链路追踪标识（<c>X-Request-Id</c> 或框架 TraceIdentifier）；仅用于请求日志。</summary>
    public String TraceId { get; set; }

    /// <summary>服务端接收时间 UTC；为 <see cref="DateTime.MinValue"/> 时取当前时间。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>预分配的消息追踪 ID；为空时生成新 UUID。</summary>
    public String MessageId { get; set; }

    /// <summary>重放来源 <c>messageId</c>；首次上报为空。</summary>
    public String ReplayOf { get; set; }
}

/// <summary>接收链路结果，控制器据此组装冻结的 <c>{code,message,data}</c> 响应体。</summary>
/// <remarks>
/// 成功恒为 200，三种落点用 <see cref="IngestResult.Message"/> 区分：
/// <c>accepted</c>（已入 Stream）、<c>accepted-by-fallback</c>（写本地兜底表）、
/// <c>accepted-by-log</c>（仅文本日志留痕，队列与兜底都不可用）。设备只按 HTTP code 判定，结构不因降级而变形。
/// </remarks>
public sealed class IngestResult
{
    /// <summary>HTTP 状态码：200 / 400 / 401 / 403 / 503。</summary>
    public Int32 StatusCode { get; set; }

    /// <summary>业务码：成功为 0，失败与 <see cref="StatusCode"/> 一致。</summary>
    public Int32 Code { get; set; }

    /// <summary>响应文案；不得含密钥或内部路径。</summary>
    public String Message { get; set; }

    /// <summary>消息追踪 ID，成功时写入响应。</summary>
    public String MessageId { get; set; }

    /// <summary>全链路幂等键，成功时写入响应。</summary>
    public String EventKey { get; set; }

    /// <summary>是否已成功写入共享 Stream。</summary>
    public Boolean Queued { get; set; }

    /// <summary>是否走了本地兜底暂存（仅 Redis 故障路径）。</summary>
    public Boolean Fallback { get; set; }

    /// <summary>本次请求是否已写入文本日志；<c>false</c> 表示该条数据没有任何本地留痕。</summary>
    public Boolean Logged { get; set; }

    /// <summary>解析告警编码（逗号分隔）；用于日志与后台发现设备新增字段。</summary>
    public String ParseWarning { get; set; }

    /// <summary>需要宿主写日志 / 高优告警的原因；正常入流为空。</summary>
    public String Warning { get; set; }

    /// <summary>实际组装的 Stream 消息体，供兜底回灌与排障使用。</summary>
    public StreamMessage StreamMessage { get; set; }

    /// <summary>是否被接收（200）。</summary>
    public Boolean IsSuccess => StatusCode == 200;

    /// <summary>构造拒绝结果：格式非法 400 / 凭据 401 / 禁止 403。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="message">响应文案。</param>
    /// <param name="warning">日志 / 告警原因。</param>
    /// <param name="streamMessage">已组装的 Stream 消息体；解析失败时为 <c>null</c>。</param>
    /// <returns>拒绝结果。</returns>
    public static IngestResult Rejected(Int32 statusCode, String message, String warning = null, StreamMessage streamMessage = null) => new()
    {
        StatusCode = statusCode,
        Code = statusCode,
        Message = message,
        MessageId = streamMessage?.MessageId,
        EventKey = streamMessage?.EventKey,
        StreamMessage = streamMessage,
        Warning = warning,
    };

    /// <summary>构造接收成功结果。</summary>
    /// <param name="message">已组装的 Stream 消息体。</param>
    /// <param name="queued">是否成功入流。</param>
    /// <param name="fallback">是否降级到本地暂存。</param>
    /// <param name="warning">降级原因，可为空。</param>
    /// <param name="logged">是否已写入文本日志。</param>
    /// <returns>200 结果。</returns>
    public static IngestResult Accepted(StreamMessage message, Boolean queued, Boolean fallback, String warning = null, Boolean logged = false) => new()
    {
        StatusCode = 200,
        Code = 0,
        Message = queued ? "accepted" : fallback ? "accepted-by-fallback" : logged ? "accepted-by-log" : "accepted",
        MessageId = message?.MessageId,
        EventKey = message?.EventKey,
        Queued = queued,
        Fallback = fallback,
        Logged = logged,
        StreamMessage = message,
        Warning = warning,
    };

    /// <summary>构造 503：队列、本地兜底与文本日志<b>全部</b>失效。</summary>
    /// <param name="warning">必须触发高优告警的详细原因。</param>
    /// <param name="message">已组装的 Stream 消息体；供日志重建与补录使用，可为空。</param>
    /// <returns>503 结果。</returns>
    public static IngestResult Unavailable(String warning, StreamMessage message = null) => new()
    {
        StatusCode = 503,
        Code = 503,
        Message = "queue-unavailable",
        MessageId = message?.MessageId,
        EventKey = message?.EventKey,
        StreamMessage = message,
        Warning = warning,
    };
}
