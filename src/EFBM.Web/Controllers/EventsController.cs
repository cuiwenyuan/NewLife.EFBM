using System.Text;
using EFBM.Core;
using EFBM.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewLife.Log;

namespace EFBM.Web.Controllers;

// 📋 生效: instructions=[newlife-global,cube,development] | skills=[cube-mvc-backend] | agent=default

/// <summary>唯一上报端点 <c>POST /api/events</c>（FR-1）。</summary>
/// <remarks>
/// 控制器只做 HTTP 适配：读原文 → 取凭据与来源 IP → 交给 <see cref="EventIngestService"/> → 组装响应，
/// 业务逻辑与数据层完全不依赖 ASP.NET Core（FR-20）。
///
/// 端点标记 <c>[AllowAnonymous]</c> 是为了绕开 Cube 的<b>后台用户</b>权限体系；
/// 设备凭据由 <see cref="RequestAuthenticator"/> 在接收层独立校验，二者互不相干。
///
/// 设备失败不重发，因此<b>每一条请求都会写入文本日志</b>（<c>logs/api/api-yyyyMMdd.log</c>，一行一条 JSON）：
/// 200 / 400 / 401 / 403 / 503 全都留原文与判定结果，线上排查与人工补录只依赖这一个文件。
/// 外网 HTTPS 由反向代理与启动自检保证，端点<b>不做 3xx 重定向</b>（设备通常不跟随重定向），
/// 仅在实际收到明文请求时记警告，不拒绝数据。
/// </remarks>
/// <example>
/// <code>
/// curl -X POST http://127.0.0.1:5000/api/events \
///   -H "Content-Type: application/json" -H "X-App-Key: key" -H "X-App-Secret: secret" \
///   -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-07-23T18:00:00+08:00","data":{"completedQty":1}}'
/// </code>
/// </example>
[ApiController]
[AllowAnonymous]
[Route(EfbmDefaults.EventsRoute)]
public sealed class EventsController : ControllerBase
{
    private readonly EventIngestService _ingest;
    private readonly EfbmOptions _options;
    private readonly IRequestLog _requestLog;

    /// <summary>构造上报端点。</summary>
    /// <param name="ingest">接收链路编排服务。</param>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="requestLog">请求日志；用于「请求体超限」这种未进入接收服务的拒绝也留痕。</param>
    public EventsController(EventIngestService ingest, EfbmOptions options, IRequestLog requestLog = null)
    {
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requestLog = requestLog;
    }

    /// <summary>接收一次设备主动上报，写入共享流即返回 200。</summary>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>统一响应体与对应 HTTP 状态码。</returns>
    [HttpPost]
    public async Task<ObjectResult> Post(CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;

        // 请求体上限只用于防异常大包；正常逐件报文远小于该值，不会因此丢弃真实数据
        if (Request.ContentLength > _options.MaxBodyBytes)
        {
            XTrace.WriteLine("非法报文已丢弃：请求体 {0} 字节超过上限 {1}", Request.ContentLength, _options.MaxBodyBytes);

            // 报文没有读进内存，日志里留占位说明，避免整条记录缺失
            await WriteRejectedLogAsync(startedAt, $"<omitted:{Request.ContentLength} bytes>", "request too large").ConfigureAwait(false);
            return Reject(400, "request too large");
        }

        String rawJson;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, true, 1024, true))
        {
            rawJson = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Encoding.UTF8.GetByteCount(rawJson) > _options.MaxBodyBytes)
        {
            XTrace.WriteLine("非法报文已丢弃：实际请求体超过上限 {0} 字节", _options.MaxBodyBytes);

            await WriteRejectedLogAsync(startedAt, $"<omitted:{Encoding.UTF8.GetByteCount(rawJson)} bytes>", "request too large").ConfigureAwait(false);
            return Reject(400, "request too large");
        }

        WarnIfPlainHttp();

        // IP 白名单在读体之后校验：被拒的请求也要把原文写进日志，否则「谁被拦了」无从查证
        var sourceIp = ResolveSourceIp();
        if (!AccessPolicy.IsIpAllowed(sourceIp, AccessPolicy.ParseList(_options.AllowedIps)))
        {
            XTrace.WriteLine("拒绝来源 IP：{0}（不在 EFBM:AllowedIps 白名单内）", sourceIp);
            await WriteRejectedLogAsync(startedAt, rawJson, "source ip not allowed").ConfigureAwait(false);
            return Reject(403, "source ip not allowed");
        }

        var request = new IngestRequest
        {
            RawJson = rawJson,
            AppKey = Header(_options.AppKeyHeader),
            AppSecret = Header(_options.AppSecretHeader),
            Authorization = Header("Authorization"),
            HmacTimestamp = Header("X-Timestamp"),
            HmacSignature = Header("X-Signature"),
            SourceIp = sourceIp,
            ReceivedAt = startedAt,
            Method = Request.Method,
            Path = Request.Path.Value,
            Scheme = Request.Scheme,
            ContentType = Request.ContentType,
            UserAgent = Request.Headers.UserAgent.FirstOrDefault(),
            TraceId = Header("X-Request-Id") ?? HttpContext.TraceIdentifier,
        };

        var result = await _ingest.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        WriteLog(result, request);

        // Basic 模式按 RFC 7617 回送质询头；只影响标准客户端，设备按 code 判定不受影响
        if (result.StatusCode == 401 && String.Equals(AuthModes.Normalize(_options.AuthMode), AuthModes.Basic, StringComparison.OrdinalIgnoreCase))
            Response.Headers.WWWAuthenticate = "Basic realm=\"EFBM\"";

        return StatusCode(result.StatusCode, ApiResponse.From(result));
    }

    /// <summary>为未进入接收服务的拒绝补一条请求日志。</summary>
    /// <param name="receivedAt">接收时间。</param>
    /// <param name="rawJson">原文或其占位说明。</param>
    /// <param name="message">拒绝文案。</param>
    /// <returns>异步任务。</returns>
    private async Task WriteRejectedLogAsync(DateTime receivedAt, String rawJson, String message)
    {
        if (_requestLog == null) return;

        try
        {
            await _requestLog.WriteAsync(new RequestLogEntry
            {
                ReceivedAt = receivedAt,
                TraceId = Header("X-Request-Id") ?? HttpContext.TraceIdentifier,
                Method = Request.Method,
                Path = Request.Path.Value,
                Scheme = Request.Scheme,
                ContentType = Request.ContentType,
                UserAgent = Request.Headers.UserAgent.FirstOrDefault(),
                SourceIp = ResolveSourceIp(),
                AuthMode = _options.AuthMode,
                AppKey = RequestLogEntry.MaskKey(Header(_options.AppKeyHeader)),
                StatusCode = 400,
                Code = 400,
                Message = message,
                ElapsedMs = (Int64)(DateTime.UtcNow - receivedAt).TotalMilliseconds,
                RawJson = rawJson,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 日志故障不能改变响应码：设备按 400 判定这次推送失败，人工仍可从框架日志里找线索
            XTrace.WriteLine("请求日志写入异常（不影响响应码）：{0}", ex.Message);
        }
    }

    /// <summary>构造不涉及接收服务的本地拒绝响应。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="message">响应文案。</param>
    /// <returns>统一响应体。</returns>
    private ObjectResult Reject(Int32 statusCode, String message) =>
        StatusCode(statusCode, ApiResponse.From(IngestResult.Rejected(statusCode, message)));

    /// <summary>读取请求头；Header 名由配置决定，重命名后无需改代码。</summary>
    /// <param name="name">请求头名。</param>
    /// <returns>请求头值；不存在返回 <c>null</c>。</returns>
    private String Header(String name) =>
        String.IsNullOrEmpty(name) ? null : Request.Headers[name].FirstOrDefault();

    /// <summary>解析来源 IP：可信反代时优先 <c>X-Forwarded-For</c> 首段，否则取 TCP 对端地址。</summary>
    /// <returns>来源 IP 字符串。</returns>
    private String ResolveSourceIp()
    {
        if (_options.TrustForwardedHeaders)
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!String.IsNullOrEmpty(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (first.Length > 0) return first;
            }

            var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!String.IsNullOrWhiteSpace(realIp)) return realIp.Trim();
        }

        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null) return String.Empty;
        return remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4().ToString() : remote.ToString();
    }

    /// <summary>外网配置下收到明文请求只记警告不拒绝；3xx 会让不跟随重定向的设备永久丢数据。</summary>
    private void WarnIfPlainHttp()
    {
        if (!_options.RequireHttps) return;

        var proto = _options.TrustForwardedHeaders ? Request.Headers["X-Forwarded-Proto"].FirstOrDefault() : null;
        var secure = Request.IsHttps || String.Equals(proto, "https", StringComparison.OrdinalIgnoreCase);
        if (!secure) XTrace.WriteLine("警告：外网部署收到非 HTTPS 上报，请检查反向代理与设备侧地址配置；本次仍正常接收");
    }

    /// <summary>按结果级别记日志：降级记警告，4xx/5xx 保留原文供人工补录。</summary>
    /// <param name="result">接收结果。</param>
    /// <param name="request">原始输入。</param>
    private static void WriteLog(IngestResult result, IngestRequest request)
    {
        if (result.IsSuccess)
        {
            if (result.Fallback) XTrace.WriteLine("警告：Redis 不可用，已写本地兜底仍返回 200。{0}", result.Warning);
            return;
        }

        if (result.StatusCode == 400)
        {
            // 请求方保证格式，丢弃即永久缺口：原文必须留在日志里，便于人工补录
            XTrace.WriteLine("非法报文已丢弃：{0} | 原文={1}", result.Warning ?? result.Message, request.RawJson);
            return;
        }

        // 401 / 403 / 503 属高优告警：设备不会重发
        XTrace.WriteLine("高优告警：HTTP {0} {1} | 原文={2}", result.StatusCode, result.Warning ?? result.Message, request.RawJson);
    }
}
