using EFBM.Core;
using EFBM.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>健康检查端点，供 Nginx / IIS / systemd 与运维验证使用。</summary>
/// <remarks>
/// 返回进程与关键配置摘要，并额外探测 Redis 连通性、Stream 长度与本地兜底待回灌条数——
/// Beta 验收时「200 之后消息到底进没进 Redis」就靠这里回答，不必登服务器。
///
/// 探测失败<b>不返回 5xx</b>：进程仍然活着，且接收链路可能仍在降级收数据；
/// 由 <c>status=degraded</c> 与 <c>redis.error</c> 表达真实状态（P5 再按监控阈值分级）。
/// 落库消费组的 lag / PEL 需要消费者存在，随 P3 一并补。
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    private static readonly Object _cacheLock = new();
    private static DateTime _cachedAt;
    private static Int64 _cachedPending;
    private static String _cachedError;

    private readonly EfbmOptions _options;
    private readonly IEventQueue _queue;
    private readonly IRequestLogProbe _requestLog;

    /// <summary>构造健康检查控制器。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="queue">共享上报流；构造不发起网络 IO。</param>
    /// <param name="requestLog">请求日志状态探针，可为 <c>null</c>。</param>
    public HealthController(EfbmOptions options, IEventQueue queue, IRequestLogProbe requestLog = null)
    {
        _options = options;
        _queue = queue;
        _requestLog = requestLog;
    }

    /// <summary>返回健康状态、队列状态、请求日志状态与兜底积压。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康状态与关键运行指标。</returns>
    [HttpGet]
    public async Task<Object> Get(CancellationToken cancellationToken)
    {
        var queue = _queue as IEventQueueProbe;
        var status = queue == null ? null : await queue.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var fallback = ReadFallbackPending(cancellationToken);
        var log = _requestLog?.GetStatus();

        // 队列关闭（Beta 纯日志模式）不算故障；日志写失败才是真的没兜住，必须降级
        var degraded = (status != null && status.Enabled && !status.Connected) || (log?.Enabled == true && log.Failed > 0);

        return new
        {
            status = degraded ? "degraded" : "ok",
            framework = "net10.0",
            deployment = _options.Deployment,
            authMode = _options.AuthMode,
            streamKey = _options.StreamKey,
            persistGroup = _options.PersistGroup,
            autoTrimEnabled = _options.AutoTrimEnabled,
            localFallbackEnabled = _options.LocalFallbackEnabled,
            betaSeedEnabled = _options.Beta?.Enabled == true,
            redis = new
            {
                enabled = _options.RedisEnabled,
                server = _options.RedisServer,
                db = _options.RedisDb,
                connected = status?.Connected ?? false,
                streamLength = status?.Length ?? 0,
                error = status?.Error,
            },
            requestLog = new
            {
                enabled = log?.Enabled ?? false,
                directory = log?.Directory,
                currentFile = log?.CurrentFile,
                written = log?.Written ?? 0,
                failed = log?.Failed ?? 0,
                lastError = log?.LastError,
                lastWriteAt = log?.LastWriteAt,
            },
            fallback = new
            {
                pending = fallback.Pending,
                error = fallback.Error,
            },
            time = DateTime.UtcNow,
        };
    }

    /// <summary>读取本地兜底待回灌条数；5 秒缓存，避免健康检查被高频轮询时压到数据库。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待回灌条数与错误摘要。</returns>
    private static (Int64 Pending, String Error) ReadFallbackPending(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_cachedAt != default && (DateTime.UtcNow - _cachedAt).TotalSeconds < 5)
                return (_cachedPending, _cachedError);
        }

        Int64 pending = 0;
        String error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending = LocalQueueFallback.FindCount(LocalQueueFallback._.RequeuedAt == EfbmDefaults.UnrequeuedAt, null, null, 0, 0);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }

        lock (_cacheLock)
        {
            _cachedAt = DateTime.UtcNow;
            _cachedPending = pending;
            _cachedError = error;
        }

        return (pending, error);
    }
}
