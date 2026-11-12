using System.Text.Json;

namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>接收链路编排：严格解析 → 设备鉴权 → 生成两个键 → 一次 <c>XADD</c> → 立即 200，并把每条请求写入文本日志。</summary>
/// <remarks>
/// 本服务<b>不依赖 ASP.NET Core 与数据库</b>，控制器只做 HTTP 适配，为二期换宿主留余地（FR-20）。
/// 正常路径没有任何数据库写操作；本地写入只发生在 Redis 故障降级与请求日志两处。
///
/// 设备按 HTTP code 判定成功且失败不重发，因此：
/// ① 400 只用于 JSON 格式 / 三个必填字段非法，其他内容一律入流；
/// ② 401 / 403 / 503 由宿主记高优告警，便于人工从日志补录原文；
/// ③ Redis 故障时降级写本地暂存仍返回 200，且降级写入使用 <see cref="CancellationToken.None"/>，
///    即使请求中途断开也要把数据保住；
/// ④ <b>Beta 语义</b>：队列与本地兜底都失效、但文本日志成功落盘时，仍返回 200
///    （<c>message=accepted-by-log</c>、<c>queued=false</c>），数据在日志里可人工补录；
///    只有队列、兜底、日志三者全失败才 503。
/// </remarks>
/// <example>
/// <code>
/// var result = await ingest.IngestAsync(new IngestRequest { RawJson = body, AppKey = key, SourceIp = ip });
/// return StatusCode(result.StatusCode, ApiResponse.From(result));
/// </code>
/// </example>
public sealed class EventIngestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly EfbmOptions _options;
    private readonly IEventQueue _queue;
    private readonly RequestAuthenticator _authenticator;
    private readonly ILocalFallbackStore _fallback;
    private readonly IRequestLog _requestLog;

    /// <summary>构造接收服务。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="queue">共享上报流。</param>
    /// <param name="credentialStore">设备凭据读取实现。</param>
    /// <param name="fallback">本地兜底暂存；可为 <c>null</c>（关闭兜底时）。</param>
    /// <param name="requestLog">请求日志；可为 <c>null</c>（测试或关闭日志时）。</param>
    public EventIngestService(EfbmOptions options, IEventQueue queue, IDeviceCredentialStore credentialStore, ILocalFallbackStore fallback, IRequestLog requestLog = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _authenticator = new RequestAuthenticator(credentialStore ?? throw new ArgumentNullException(nameof(credentialStore)));
        _fallback = fallback;
        _requestLog = requestLog;
    }

    /// <summary>接收一次上报，写入日志并给出 HTTP 结果。</summary>
    /// <param name="request">控制器提取的原始输入。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>接收结果；<see cref="IngestResult.IsSuccess"/> 为 <c>true</c> 时设备视为推送成功。</returns>
    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var result = await IngestCoreAsync(request, cancellationToken).ConfigureAwait(false);

        return await WriteRequestLogAsync(request, result).ConfigureAwait(false);
    }

    /// <summary>解析 → 鉴权 → 入流 / 兜底，不含日志写入。</summary>
    /// <param name="request">原始输入。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>接收结果。</returns>
    private async Task<IngestResult> IngestCoreAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        var context = new ParseContext
        {
            MessageId = request.MessageId,
            ReceivedAt = request.ReceivedAt,
            SourceIp = request.SourceIp,
            ReplayOf = request.ReplayOf,
        };

        if (!EventEnvelopeParser.TryParse(request.RawJson, context, out var envelope, out var parseError))
            return IngestResult.Rejected(400, "invalid json", $"非法报文已丢弃: {parseError}");

        var parseWarning = envelope.ParseWarning;

        var auth = await _authenticator.AuthenticateAsync(new AuthRequest
        {
            AuthMode = _options.AuthMode,
            IsInternet = _options.IsInternet(),
            AppKey = request.AppKey,
            AppSecret = request.AppSecret,
            Authorization = request.Authorization,
            DeviceId = envelope.DeviceId,
        }, cancellationToken).ConfigureAwait(false);

        // 先组装消息体：401 / 403 也要在日志里留下 event / deviceId / 两个键，便于定位是哪台设备在打
        var message = StreamMessage.FromEnvelope(envelope);

        if (!auth.IsSuccess)
        {
            // 只记 deviceId 与来源 IP，绝不记录 AppSecret / Authorization
            var warning = $"鉴权失败({auth.StatusCode}) {auth.Message}: deviceId={envelope.DeviceId} ip={request.SourceIp} event={envelope.Event}";
            var rejected = IngestResult.Rejected(auth.StatusCode, auth.Message, warning, message);
            rejected.ParseWarning = parseWarning;
            return rejected;
        }

        try
        {
            await _queue.AddAsync(message, cancellationToken).ConfigureAwait(false);
            var accepted = IngestResult.Accepted(message, true, false);
            accepted.ParseWarning = parseWarning;
            return accepted;
        }
        catch (Exception ex)
        {
            // Beta 纯日志模式不尝试入流，告警文案保持简短，便于在日志文件里直接扫读
            var warning = !_options.RedisEnabled
                ? "Redis 未启用（EFBM:RedisEnabled=false），未尝试写入共享流"
                : $"XADD 失败 messageId={message.MessageId} eventKey={message.EventKey}: {ex.GetType().Name}: {ex.Message}";

            if (_options.LocalFallbackEnabled && _options.RedisEnabled && _fallback != null)
            {
                try
                {
                    // 请求可能已被设备断开，但数据必须保住：降级写入不带请求取消令牌
                    await _fallback.SaveAsync(message, CancellationToken.None).ConfigureAwait(false);
                    var fallbackResult = IngestResult.Accepted(message, false, true, warning);
                    fallbackResult.ParseWarning = parseWarning;
                    return fallbackResult;
                }
                catch (Exception fallbackEx)
                {
                    warning += $" | 本地兜底写入失败: {fallbackEx.GetType().Name}: {fallbackEx.Message}";
                }
            }
            else
            {
                // 纯日志模式不写兜底表：日志已是唯一真相源，重复落库只会让待回灌记录无限堆积
                warning += _options.RedisEnabled ? " | 本地兜底未启用" : " | 纯日志模式，由文本日志留痕";
            }

            var unavailable = IngestResult.Unavailable(warning, message);
            unavailable.ParseWarning = parseWarning;
            return unavailable;
        }
    }

    /// <summary>把本次请求写入文本日志，并在队列 / 兜底都失败时用日志兜住数据。</summary>
    /// <param name="request">原始输入。</param>
    /// <param name="result">接收结果。</param>
    /// <returns>最终结果；日志落盘则 503 升级为 200 <c>accepted-by-log</c>。</returns>
    private async Task<IngestResult> WriteRequestLogAsync(IngestRequest request, IngestResult result)
    {
        if (_requestLog == null) return result;

        // 队列、兜底都失败时，日志能否落盘决定最终响应码；因此日志里要写「升级后」的状态，
        // 否则会出现「响应 200、日志却记 503」的自相矛盾。写失败时日志本来也不存在，不会留下错误记录。
        var upgraded = result.StatusCode == 503;
        var final = upgraded
            ? IngestResult.Accepted(result.StreamMessage, false, false, result.Warning, true)
            : result;

        Boolean logged;
        try
        {
            // 日志写入不带请求取消令牌：设备断开也要留痕
            logged = await _requestLog.WriteAsync(BuildEntry(request, final), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 接口约定实现方不得抛异常；这里再兜一层，日志故障绝不能变成 500
            logged = false;
            final.Warning = $"{final.Warning} | 请求日志写入异常: {ex.GetType().Name}: {ex.Message}".TrimStart(' ', '|');
        }

        if (!logged) return result;

        final.Logged = true;
        return final;
    }

    /// <summary>组装请求日志条目；只做取值与脱敏，不做任何 IO。</summary>
    /// <param name="request">原始输入。</param>
    /// <param name="result">接收结果。</param>
    /// <returns>日志条目。</returns>
    private RequestLogEntry BuildEntry(IngestRequest request, IngestResult result)
    {
        var message = result.StreamMessage;
        var receivedAt = request.ReceivedAt == default ? DateTime.UtcNow : request.ReceivedAt;
        var elapsed = (Int64)(DateTime.UtcNow - receivedAt).TotalMilliseconds;
        if (elapsed < 0) elapsed = 0;

        return new RequestLogEntry
        {
            ReceivedAt = receivedAt,
            TraceId = request.TraceId,
            Method = request.Method,
            Path = request.Path,
            Scheme = request.Scheme,
            ContentType = request.ContentType,
            UserAgent = request.UserAgent,
            SourceIp = request.SourceIp,
            DeviceId = message?.DeviceId,
            Event = message?.Event,
            MessageId = message?.MessageId ?? result.MessageId,
            EventKey = message?.EventKey ?? result.EventKey,
            AuthMode = _options.AuthMode,
            AppKey = RequestLogEntry.MaskKey(request.AppKey),
            StatusCode = result.StatusCode,
            Code = result.Code,
            Message = result.Message,
            Queued = result.Queued,
            Fallback = result.Fallback,
            Warning = result.Warning,
            ParseWarning = result.ParseWarning,
            ElapsedMs = elapsed,
            RawJson = request.RawJson,
        };
    }

    /// <summary>把 Stream 消息体序列化为兜底存储与回灌共用的 JSON。</summary>
    /// <param name="message">Stream 消息体。</param>
    /// <returns>camelCase JSON 文本。</returns>
    public static String Serialize(StreamMessage message) => JsonSerializer.Serialize(message, JsonOptions);

    /// <summary>把兜底存储中的 JSON 还原为 Stream 消息体。</summary>
    /// <param name="json">兜底记录中的消息体 JSON。</param>
    /// <returns>消息体；无法解析返回 <c>null</c>，由回灌服务记录并跳过。</returns>
    public static StreamMessage Deserialize(String json)
    {
        if (String.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<StreamMessage>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
