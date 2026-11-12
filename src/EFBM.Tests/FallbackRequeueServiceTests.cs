using System.Diagnostics;
using EFBM.Core;
using EFBM.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Tests;

/// <summary>本地兜底回灌闭环测试：Redis 恢复后重新入流、失败只重试不删记录（FR-11、FR-22、验收用例 C8）。</summary>
public class FallbackRequeueServiceTests
{
    /// <summary>等待后台服务完成第一轮扫描，避免依赖固定 sleep。</summary>
    /// <param name="condition">完成条件。</param>
    /// <param name="timeoutMs">最长等待毫秒数。</param>
    /// <returns>条件达成返回 <c>true</c>。</returns>
    private static async Task<Boolean> WaitAsync(Func<Boolean> condition, Int32 timeoutMs = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    /// <summary>构造回灌服务与依赖。</summary>
    /// <param name="store">兜底存储替身。</param>
    /// <param name="queue">队列替身。</param>
    /// <param name="resolved">队列是否被解析过。</param>
    /// <returns>回灌服务。</returns>
    private static FallbackRequeueService Create(FakeFallbackStore store, FakeEventQueue queue, out Func<Boolean> resolved)
    {
        var flag = false;
        var services = new ServiceCollection();
        services.AddSingleton<IEventQueue>(_ => { flag = true; return queue; });
        var provider = services.BuildServiceProvider();
        var options = new EfbmOptions { FallbackRequeueIntervalSeconds = 3600, FallbackRequeueBatchSize = 10 };
        resolved = () => flag;
        return new FallbackRequeueService(provider, store, options);
    }

    /// <summary>构造一条待回灌记录。</summary>
    /// <param name="raw">消息体 JSON。</param>
    /// <returns>兜底记录。</returns>
    private static FallbackEntry Entry(String raw) => new()
    {
        Id = 7,
        MessageId = "m-1",
        EventKey = "k-1",
        RawJson = raw,
        RetryCount = 0,
    };

    [Fact(DisplayName = "回灌_有待回灌记录_重新XADD且保留原messageId")]
    public async Task Requeue_ReaddsMessageAndMarksDone()
    {
        var store = new FakeFallbackStore();
        var queue = new FakeEventQueue();
        var message = new StreamMessage { MessageId = "m-1", EventKey = "k-1", RawJson = "{\"event\":\"FAULT_CLEARED\"}" };
        store.Pending.Add(Entry(EventIngestService.Serialize(message)));
        var service = Create(store, queue, out var resolved);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await WaitAsync(() => store.Requeued.Count == 1));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(resolved());
        Assert.Single(queue.Added);
        Assert.Equal("m-1", queue.Added[0].MessageId);
        Assert.Equal("{\"event\":\"FAULT_CLEARED\"}", queue.Added[0].RawJson);
        Assert.Empty(store.Retried);
    }

    [Fact(DisplayName = "回灌_队列仍不可用_只累加重试次数不删记录")]
    public async Task Requeue_QueueDown_OnlyRetries()
    {
        var store = new FakeFallbackStore();
        var queue = new FakeEventQueue { ThrowOnAdd = true };
        var message = new StreamMessage { MessageId = "m-1", RawJson = "{}" };
        store.Pending.Add(Entry(EventIngestService.Serialize(message)));
        var service = Create(store, queue, out _);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await WaitAsync(() => store.Retried.Count == 1));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Empty(store.Requeued);
        Assert.Contains(7L, store.Retried);
    }

    [Fact(DisplayName = "回灌_消息体无法解析_记重试且绝不删除或丢弃")]
    public async Task Requeue_BadPayload_OnlyRetries()
    {
        var store = new FakeFallbackStore();
        var queue = new FakeEventQueue();
        store.Pending.Add(Entry("not json"));
        var service = Create(store, queue, out _);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await WaitAsync(() => store.Retried.Count == 1));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Empty(queue.Added);
        Assert.Empty(store.Requeued);
    }

    [Fact(DisplayName = "回灌_无待回灌记录_完全不解析队列也不连Redis")]
    public async Task Requeue_NoPending_DoesNotResolveQueue()
    {
        var store = new FakeFallbackStore();
        var service = Create(store, new FakeEventQueue(), out var resolved);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.False(resolved());
    }
}
