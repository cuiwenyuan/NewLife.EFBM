using EFBM.Core;
using Microsoft.Extensions.DependencyInjection;
using NewLife.Log;

namespace EFBM.Web.Services;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>本地兜底回灌服务：Redis 恢复后把暂存消息重新 <c>XADD</c> 到共享流（ING-4b）。</summary>
/// <remarks>
/// 每轮先查兜底表，<b>没有待回灌记录就不解析 <see cref="IEventQueue"/></b>：
/// 因此未装 Redis 的开发机启动宿主也不会被连接异常刷屏，且不影响启动。
///
/// 重新入流保留原 <c>messageId</c> / <c>eventKey</c>：对外部消费组而言与首次投递等价，
/// 由各自按 <c>EventKey</c> 幂等；本系统落库组同样靠唯一约束兜住，不会产生第二条原始报文。
/// 消息体无法解析时只累加 <c>RetryCount</c> 并告警，<b>绝不删除兜底记录</b>（FR-22）。
/// </remarks>
public sealed class FallbackRequeueService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILocalFallbackStore _store;
    private readonly EfbmOptions _options;

    /// <summary>构造回灌服务。</summary>
    /// <param name="services">服务提供器，用于惰性取得队列实例。</param>
    /// <param name="store">本地兜底存储。</param>
    /// <param name="options">EFBM 配置节。</param>
    public FallbackRequeueService(IServiceProvider services, ILocalFallbackStore store, EfbmOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>周期扫描并回灌，直到宿主停止。</summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    /// <returns>异步任务。</returns>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 纯日志模式不写兜底表，也就没有回灌任务可做；直接退出，避免每 30 秒白扫一次库
        if (!_options.RedisEnabled)
        {
            XTrace.WriteLine("共享上报流已关闭（EFBM:RedisEnabled=false），本地兜底回灌服务不启用");
            return Task.CompletedTask;
        }

        return RunAsync(stoppingToken);
    }

    /// <summary>回灌主循环。</summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task RunAsync(CancellationToken stoppingToken)
    {
        XTrace.WriteLine("本地兜底回灌服务已启动：间隔={0}s / 批量={1}条", _options.FallbackRequeueIntervalSeconds, _options.FallbackRequeueBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RequeueOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // 单轮失败不能让后台服务退出，否则 Redis 恢复后永远不再回灌
                XTrace.WriteLine("本地兜底回灌扫描异常：{0}: {1}", ex.GetType().Name, ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.FallbackRequeueIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>执行一轮回灌。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task RequeueOnceAsync(CancellationToken cancellationToken)
    {
        var entries = await _store.TakePendingAsync(_options.FallbackRequeueBatchSize, cancellationToken).ConfigureAwait(false);
        if (entries == null || entries.Count == 0) return;

        IEventQueue queue;
        try
        {
            queue = _services.GetRequiredService<IEventQueue>();
        }
        catch (Exception ex)
        {
            XTrace.WriteLine("本地兜底待回灌 {0} 条，但队列不可用：{1}: {2}", entries.Count, ex.GetType().Name, ex.Message);
            return;
        }

        XTrace.WriteLine("本地兜底开始回灌 {0} 条", entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = EventIngestService.Deserialize(entry.RawJson);
            if (message == null)
            {
                await _store.MarkRetryAsync(entry.Id, "消息体 JSON 无法解析", cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await queue.AddAsync(message, cancellationToken).ConfigureAwait(false);
                await _store.MarkRequeuedAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                await _store.MarkRetryAsync(entry.Id, $"{ex.GetType().Name}: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
