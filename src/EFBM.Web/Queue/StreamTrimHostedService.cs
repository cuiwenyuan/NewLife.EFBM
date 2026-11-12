using EFBM.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EFBM.Web.Queue;

/// <summary>安全裁剪后台任务：周期性按各组最小已确认 ID 执行 <c>XTRIM MINID</c>，避免 Stream 无限增长。</summary>
/// <remarks>
/// 自门控：仅当 <c>EFBM:RedisEnabled=true</c> 且 <c>EFBM:SafeTrimEnabled=true</c> 时才启动循环；
/// 否则仅打印一条日志后退出，不占用资源。Redis 版本低于 6.2（不支持 <c>XTRIM MINID</c>）时
/// 首次执行即检测到并停止循环，避免反复报错。
/// </remarks>
public sealed class StreamTrimHostedService : IHostedService
{
    private readonly IStreamTrimmer _trimmer;
    private readonly EfbmOptions _options;
    private readonly ILogger<StreamTrimHostedService> _logger;

    private PeriodicTimer _timer;
    private CancellationTokenSource _cts;
    private Boolean _unsupportedLogged;

    /// <summary>构造后台任务。</summary>
    public StreamTrimHostedService(IStreamTrimmer trimmer, EfbmOptions options, ILogger<StreamTrimHostedService> logger)
    {
        _trimmer = trimmer ?? throw new ArgumentNullException(nameof(trimmer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>按需启动裁剪循环；未启用时直接返回。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.RedisEnabled || !_options.SafeTrimEnabled)
        {
            _logger.LogInformation(
                "安全裁剪后台任务未启动：RedisEnabled={Redis}, SafeTrimEnabled={Safe}（XTRIM MINID 需要 Redis ≥ 6.2）",
                _options.RedisEnabled, _options.SafeTrimEnabled);
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SafeTrimIntervalSeconds));
        _ = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>停止循环并释放定时器。</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(ct))
            {
                var report = await _trimmer.TrimSafeAsync(ct);

                if (report.Unsupported)
                {
                    if (!_unsupportedLogged)
                    {
                        _logger.LogWarning(
                            "Redis 不支持 XTRIM MINID（需 ≥ 6.2），安全裁剪停止。详情：{Message}", report.ErrorMessage);
                        _unsupportedLogged = true;
                    }
                    break;
                }

                if (report.Applied)
                {
                    _logger.LogInformation(
                        "安全裁剪完成：消费组数={Groups}, 安全边界={Safe}ms, 保留下限={Floor}ms, 实际裁剪点={Effective}ms, 删除={Trimmed}",
                        report.GroupCount, report.SafeMs, report.FloorMs, report.EffectiveMs, report.TrimmedCount);
                }
                else if (!String.IsNullOrEmpty(report.SkippedReason))
                {
                    _logger.LogDebug("安全裁剪跳过：{Reason}", report.SkippedReason);
                }
                else if (!String.IsNullOrEmpty(report.ErrorMessage))
                {
                    _logger.LogError("安全裁剪异常：{Message}", report.ErrorMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安全裁剪循环异常，已退出");
        }
    }
}
