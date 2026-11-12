using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Consumers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>健康检查端点，供 Nginx / IIS / systemd 与运维验证使用。</summary>
/// <remarks>
/// 回答三个问题：<b>进程活着吗</b>（HTTP 200）、<b>依赖通吗</b>（数据库 / Redis）、
/// <b>数据真的在动吗</b>（消费者最后取数时间、各组 lag、兜底积压）。
/// 每条检查单独给结论（<c>checks</c>），整体再汇总成 <c>ok</c> / <c>degraded</c>。
///
/// 探测失败<b>不返回 5xx</b>：进程仍然活着，且接收链路可能仍在降级收数据；
/// 由 <c>status=degraded</c> 与各段 <c>error</c> 表达真实状态。
/// Redis 启用但消费者停摆属于高危（上报仍 200、数据只堆在 Stream），同样纳入 degraded。
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    /// <summary>消费者超过该秒数没取到消息即视为疑似停摆。</summary>
    public const Int32 ConsumerStallSeconds = 600;

    private static readonly Object _cacheLock = new();
    private static DateTime _cachedAt;
    private static Int64 _cachedPending;
    private static String _cachedPendingError;
    private static DateTime _dbCachedAt;
    private static Boolean _dbCachedOk;
    private static String _dbCachedError;

    private readonly EfbmOptions _options;
    private readonly IEventQueue _queue;
    private readonly IRequestLogProbe _requestLog;
    private readonly PersistConsumerState _consumer;
    private readonly IQueueMetricsProbe _probe;

    /// <summary>构造健康检查控制器。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="queue">共享上报流；构造不发起网络 IO。</param>
    /// <param name="requestLog">请求日志状态探针，可为 <c>null</c>。</param>
    /// <param name="consumer">落库消费者指标，可为 <c>null</c>。</param>
    /// <param name="probe">消费组指标探测，可为 <c>null</c>（纯日志模式）。</param>
    public HealthController(EfbmOptions options, IEventQueue queue, IRequestLogProbe requestLog = null, PersistConsumerState consumer = null, IQueueMetricsProbe probe = null)
    {
        _options = options;
        _queue = queue;
        _requestLog = requestLog;
        _consumer = consumer;
        _probe = probe;
    }

    /// <summary>返回健康状态、依赖探测、消费者指标、各组积压、请求日志状态与兜底积压。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康状态与关键运行指标。</returns>
    [HttpGet]
    public async Task<Object> Get(CancellationToken cancellationToken)
    {
        var queue = _queue as IEventQueueProbe;
        var status = queue == null ? null : await queue.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var fallback = ReadFallbackPending(cancellationToken);
        var database = ProbeDatabase(cancellationToken);
        var log = _requestLog?.GetStatus();
        var consumer = _consumer?.Snapshot();

        // 指标探测失败不抛异常（实现方约定），因此可以放心调用；纯日志模式直接跳过
        var metrics = _options.RedisEnabled && _probe != null
            ? await _probe.GetMetricsAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var now = DateTime.UtcNow;
        var stalled = _options.RedisEnabled && _consumer != null && _consumer.Running
            && _consumer.LastTakeAt.HasValue
            && (now - _consumer.LastTakeAt.Value).TotalSeconds > ConsumerStallSeconds
            && (metrics?.Length ?? 0) > 0;

        var checks = new List<HealthCheckResult>();

        checks.Add(Check("database", database.Ok, database.Ok ? "连接正常" : database.Error));

        if (status != null && status.Enabled)
            checks.Add(Check("redis", status.Connected, status.Connected ? "连接正常" : status.Error));

        // 连到从节点时写入会失败，这是「上报一直 503」的常见原因，单列一条
        if (metrics is { Connected: true, RedisRole.Length: > 0 })
        {
            var isSlave = String.Equals(metrics.RedisRole, "slave", StringComparison.OrdinalIgnoreCase);
            checks.Add(Check("redis-role", !isSlave, isSlave ? "当前连接的是从节点，写入会失败" : $"当前角色 {metrics.RedisRole}"));
        }

        if (_options.RedisEnabled && _consumer != null)
            checks.Add(Check("consumer", _consumer.Running && !stalled,
                !_consumer.Running ? "落库消费者未运行：上报仍会 200，但数据只堆在 Stream" : stalled ? $"消费者已 {ConsumerStallSeconds} 秒未取到消息，疑似停摆" : "运行中"));

        if (_options.RedisEnabled && metrics is { Connected: true })
            checks.Add(Check("groups", !metrics.HasZombie, metrics.HasZombie ? "存在僵尸组：有积压但无人消费" : "各组消费正常"));

        if (log?.Enabled == true)
            checks.Add(Check("request-log", log.Failed == 0, log.Failed == 0 ? "写入正常" : $"写失败 {log.Failed} 次：{log.LastError}"));

        if (fallback.Pending > 0)
            checks.Add(Check("fallback", fallback.Error == null, $"待回灌 {fallback.Pending} 条" + (fallback.Error == null ? String.Empty : $"；读取失败：{fallback.Error}")));

        var degraded = checks.Any(e => !e.Ok);

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
            hmacEnabled = _options.HmacEnabled,
            allowedIps = AccessPolicy.ParseList(_options.AllowedIps).Length,
            queryApiEnabled = !String.IsNullOrWhiteSpace(_options.QueryApiKey),
            database = new
            {
                ok = database.Ok,
                error = database.Error,
            },
            redis = new
            {
                enabled = _options.RedisEnabled,
                server = _options.RedisServer,
                db = _options.RedisDb,
                connected = status?.Connected ?? false,
                streamLength = status?.Length ?? 0,
                version = metrics?.RedisVersion,
                role = metrics?.RedisRole,
                usedMemory = metrics?.UsedMemory,
                connectedClients = metrics?.ConnectedClients,
                error = status?.Error,
            },
            consumer = new
            {
                enabled = _options.RedisEnabled,
                running = _consumer?.Running ?? false,
                stalled,
                lastTakeAt = _consumer?.LastTakeAt,
                snapshot = consumer,
            },
            groups = metrics?.Groups.Select(e => new
            {
                name = e.Name,
                consumers = e.Consumers,
                pending = e.Pending,
                lag = e.Lag,
                lagCapped = e.LagCapped,
                isZombie = e.IsZombie,
                isPersistGroup = e.IsPersistGroup,
            }).ToList(),
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
            checks,
            time = now,
        };
    }

    /// <summary>构造一条检查结论。</summary>
    /// <param name="name">检查项名。</param>
    /// <param name="ok">是否通过。</param>
    /// <param name="message">说明。</param>
    /// <returns>检查结论。</returns>
    private static HealthCheckResult Check(String name, Boolean ok, String message) => new()
    {
        Name = name,
        Ok = ok,
        Status = ok ? "ok" : "degraded",
        Message = message,
    };

    /// <summary>探活数据库：执一次 count 查询，5 秒缓存，避免高频轮询压库。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否可用与错误摘要。</returns>
    private static (Boolean Ok, String Error) ProbeDatabase(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_dbCachedAt != default && (DateTime.UtcNow - _dbCachedAt).TotalSeconds < 5)
                return (_dbCachedOk, _dbCachedError);
        }

        Boolean ok;
        String error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Device.FindCount(null, null, null, 0, 1);
            ok = true;
        }
        catch (Exception ex)
        {
            ok = false;
            error = $"{ex.GetType().Name}: {ex.Message}";
        }

        lock (_cacheLock)
        {
            _dbCachedAt = DateTime.UtcNow;
            _dbCachedOk = ok;
            _dbCachedError = error;
        }

        return (ok, error);
    }

    /// <summary>读取本地兜底待回灌条数；5 秒缓存，避免健康检查被高频轮询时压到数据库。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待回灌条数与错误摘要。</returns>
    private static (Int64 Pending, String Error) ReadFallbackPending(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_cachedAt != default && (DateTime.UtcNow - _cachedAt).TotalSeconds < 5)
                return (_cachedPending, _cachedPendingError);
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
            _cachedPendingError = error;
        }

        return (pending, error);
    }
}

/// <summary>单项健康检查结论。</summary>
/// <remarks>
/// 用显式类型而不是匿名对象：<c>checks</c> 要被监控系统按名字取值，
/// 匿名类型在 <c>List&lt;Object&gt;</c> 里既不好判读也容易被后续改动悄悄改掉结构。
/// </remarks>
public sealed class HealthCheckResult
{
    /// <summary>检查项名，例如 <c>database</c> / <c>redis</c> / <c>consumer</c>。</summary>
    public String Name { get; init; }

    /// <summary>单项结论：<c>ok</c> 或 <c>degraded</c>。</summary>
    public String Status { get; init; }

    /// <summary>是否通过。</summary>
    public Boolean Ok { get; init; }

    /// <summary>面向运维的说明。</summary>
    public String Message { get; init; }
}
