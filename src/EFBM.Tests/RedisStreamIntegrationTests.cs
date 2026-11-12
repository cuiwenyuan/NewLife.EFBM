using EFBM.Core;
using EFBM.Web.Queue;
using NewLife.Caching;

namespace EFBM.Tests;

/// <summary>本机 Redis 可用性探测：不可用时把集成测试标记为跳过，而不是让整个测试套件变红。</summary>
internal static class RedisProbe
{
    /// <summary>连接串；与开发环境默认一致。</summary>
    public const String Server = "127.0.0.1:6379";

    private static readonly Lazy<Boolean> _available = new(Detect);

    /// <summary>本机 Redis 是否可用。</summary>
    public static Boolean Available => _available.Value;

    private static Boolean Detect()
    {
        try
        {
            using var redis = new FullRedis(Server, null, 0);
            redis.Timeout = 1000;

            return redis.Execute<Boolean>(rc => rc.Ping());
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>需要真实 Redis 的测试：没有 Redis 时自动跳过并在结果里说明原因。</summary>
public sealed class RedisFactAttribute : FactAttribute
{
    /// <summary>构造时探测；不可用则设置 <see cref="FactAttribute.Skip"/>。</summary>
    public RedisFactAttribute()
    {
        if (!RedisProbe.Available) Skip = $"本机没有可用的 Redis（{RedisProbe.Server}），跳过该集成测试";
    }
}

/// <summary>真实 Redis 的多消费组语义（TST-2b）：组间广播、新组从头重放、裁剪不碰未确认消息。</summary>
/// <remarks>
/// 这三条是共享流设计的立足点，也是最容易被「框架默认行为」破坏的地方：
/// ① 一次 <c>XADD</c> 后每个组各拿一份全量，组间确认互不影响；
/// ② 新组（含误删后重建）<b>从头部开始</b>读，宁可重复也不能漏，重复由 <c>EventKey</c> 收敛；
/// ③ 裁剪只能动所有组都已确认的部分，有未确认消息时一条都不能删。
///
/// 每个用例用独立 Stream key，结束时删除，互不干扰。
/// </remarks>
public sealed class RedisStreamIntegrationTests
{
    /// <summary>在指定 Stream 上构造一个队列（每个用例自建 key，结束时删除）。</summary>
    private static (RedisEventQueue Queue, EfbmOptions Options) CreateQueue(String key, String group)
    {
        var options = new EfbmOptions
        {
            RedisEnabled = true,
            RedisServer = RedisProbe.Server,
            StreamKey = key,
            PersistGroup = group,
            SafeTrimEnabled = true,
            SafeTrimMinRetentionDays = 0,
        };

        return (new RedisEventQueue(options), options);
    }

    /// <summary>生成独立 Stream key。</summary>
    private static String NewKey() => $"efbm:test:{Guid.NewGuid():N}";

    /// <summary>构造一条消息。</summary>
    private static StreamMessage CreateMessage() => new()
    {
        MessageId = Guid.NewGuid().ToString("D"),
        EventKey = Guid.NewGuid().ToString("N"),
        Event = EventNames.OrderCompleted,
        DeviceId = "FBOX-REDIS-01",
        Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        RawJson = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-REDIS-01\"}",
        ReceivedAt = DateTime.UtcNow,
        SourceIp = "127.0.0.1",
    };

    /// <summary>删除测试用的 Stream key。</summary>
    private static void RemoveKey(String key)
    {
        try
        {
            using var redis = new FullRedis(RedisProbe.Server, null, 0);
            redis.Remove(key);
        }
        catch (Exception)
        {
            // 清理失败不影响结论
        }
    }

    [RedisFact(DisplayName = "Redis集成_一次写入两个组各拿一份全量")]
    public async Task Broadcast_TwoGroupsBothReceive()
    {
        var key = NewKey();
        var (queueA, optionsA) = CreateQueue(key, EfbmDefaults.PersistGroup);
        var (queueB, _) = CreateQueue(key, "efbm-test-b");

        try
        {
            var message = CreateMessage();
            await queueA.AddAsync(message);

            // 先让 A 组建好并读一次，确保 B 组是在「消息已存在」之后才创建的
            var takenA = await queueA.TakeAsync(optionsA.PersistGroup, "consumer-a", 10);
            Assert.Single(takenA);
            Assert.Equal(message.EventKey, takenA[0].Message.EventKey);

            var takenB = await queueB.TakeAsync("efbm-test-b", "consumer-b", 10);
            Assert.Single(takenB);
            Assert.Equal(message.EventKey, takenB[0].Message.EventKey);

            // A 组确认不影响 B 组的 PEL
            await queueA.AckAsync(optionsA.PersistGroup, takenA[0].StreamId);
            var again = await queueA.TakeAsync(optionsA.PersistGroup, "consumer-a", 10);
            Assert.Empty(again);
        }
        finally
        {
            queueA.Dispose();
            queueB.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_新组从头部重放历史消息")]
    public async Task NewGroup_ReplaysFromStart()
    {
        var key = NewKey();
        var (queueA, optionsA) = CreateQueue(key, EfbmDefaults.PersistGroup);
        var (queueB, _) = CreateQueue(key, "efbm-test-late");

        try
        {
            var first = CreateMessage();
            await queueA.AddAsync(first);

            // A 组把消息读走并确认，证明「已被消费过」；晚加入的组仍然要能读到
            var takenA = await queueA.TakeAsync(optionsA.PersistGroup, "consumer-a", 10);
            foreach (var item in takenA) await queueA.AckAsync(optionsA.PersistGroup, item.StreamId);

            // 晚加入的组（等价于「组被误删后重建」）：FromLastOffset=false，从头读
            var taken = await queueB.TakeAsync("efbm-test-late", "consumer-b", 10);

            Assert.Single(taken);
            Assert.Equal(first.EventKey, taken[0].Message.EventKey);
        }
        finally
        {
            queueA.Dispose();
            queueB.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_有未确认消息时裁剪一条都不删")]
    public async Task Trim_KeepsPending()
    {
        var key = NewKey();
        var (queue, options) = CreateQueue(key, EfbmDefaults.PersistGroup);

        try
        {
            for (var i = 0; i < 3; i++) await queue.AddAsync(CreateMessage());

            var taken = await queue.TakeAsync(options.PersistGroup, "consumer-t", 10);
            Assert.Equal(3, taken.Count);

            // 一条都不确认：PEL 里有未确认消息，安全边界停在第一条上，不能删
            var report = await queue.TrimSafeAsync();

            Assert.True(report.Ran || report.SkippedReason != null);
            Assert.Equal(0, report.TrimmedCount);

            var metrics = await queue.GetMetricsAsync();
            Assert.Equal(3, metrics.Length);
        }
        finally
        {
            queue.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_全部确认后按水位裁剪已消费部分")]
    public async Task Trim_RemovesAcked()
    {
        var key = NewKey();
        var (queue, options) = CreateQueue(key, EfbmDefaults.PersistGroup);

        try
        {
            for (var i = 0; i < 3; i++) await queue.AddAsync(CreateMessage());

            var taken = await queue.TakeAsync(options.PersistGroup, "consumer-t", 10);
            foreach (var item in taken) await queue.AckAsync(options.PersistGroup, item.StreamId);

            var report = await queue.TrimSafeAsync();

            Assert.True(report.Ran, report.SkippedReason ?? report.ErrorMessage ?? "应执行裁剪");
            Assert.True(report.TrimmedCount > 0, "已全部确认时应删除已消费的部分");

            var metrics = await queue.GetMetricsAsync();
            Assert.True(metrics.Length < 3, $"裁剪后长度应小于 3，实际 {metrics.Length}");
        }
        finally
        {
            queue.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_指标探测能读出组与消费者")]
    public async Task Metrics_ReadsGroups()
    {
        var key = NewKey();
        var (queue, options) = CreateQueue(key, EfbmDefaults.PersistGroup);

        try
        {
            await queue.AddAsync(CreateMessage());
            await queue.TakeAsync(options.PersistGroup, "consumer-m", 10);

            var metrics = await queue.GetMetricsAsync();

            Assert.True(metrics.Connected);
            Assert.Equal(1, metrics.Length);
            var group = Assert.Single(metrics.Groups);
            Assert.Equal(options.PersistGroup, group.Name);
            Assert.True(group.IsPersistGroup);
            Assert.Equal(1, group.Pending);
            Assert.NotNull(metrics.RedisVersion);
        }
        finally
        {
            queue.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_接管旧消费者遗留的未确认消息")]
    public async Task ClaimStale_TakesOverDeadConsumer()
    {
        var key = NewKey();
        var (queue, options) = CreateQueue(key, EfbmDefaults.PersistGroup);

        try
        {
            // 模拟「上一轮进程」：consumer-old 读取后就被杀掉，消息留在它的 PEL 里没确认
            await queue.AddAsync(CreateMessage());
            var takenOld = await queue.TakeAsync(options.PersistGroup, "consumer-old", 10);
            Assert.Single(takenOld);

            // 新进程只读自己的 PEL，拿不到这条消息——不接管就永远不会被处理
            var takenNew = await queue.TakeAsync(options.PersistGroup, "consumer-new", 10);
            Assert.Empty(takenNew);

            // 空闲阈值内（别人可能还在处理）不接管
            Assert.Equal(0, await queue.ClaimStaleAsync(options.PersistGroup, "consumer-new", 600_000, 100));

            // 阈值设 0 视作不启用，避免误把在飞的消息抢走
            Assert.Equal(0, await queue.ClaimStaleAsync(options.PersistGroup, "consumer-new", 0, 100));

            // 判定为空闲超时后接管，接管到的消息立刻出现在新消费者的 PEL 里
            var claimed = await queue.ClaimStaleAsync(options.PersistGroup, "consumer-new", 1, 100);
            Assert.Equal(1, claimed);

            var takenAgain = await queue.TakeAsync(options.PersistGroup, "consumer-new", 10);
            Assert.Single(takenAgain);
            Assert.Equal(takenOld[0].Message.EventKey, takenAgain[0].Message.EventKey);

            // 确认后 PEL 清空，不会重复接管
            await queue.AckAsync(options.PersistGroup, takenAgain[0].StreamId);
            Assert.Equal(0, await queue.ClaimStaleAsync(options.PersistGroup, "consumer-new", 1, 100));
        }
        finally
        {
            queue.Dispose();
            RemoveKey(key);
        }
    }

    [RedisFact(DisplayName = "Redis集成_接管翻页_一次调用抢完一页以上")]
    public async Task ClaimStale_PagesThroughBacklog()
    {
        var key = NewKey();
        var (queue, options) = CreateQueue(key, EfbmDefaults.PersistGroup);

        try
        {
            // 一次强杀就可能留下整批遗留：单页 100 条，这里造 250 条，验证一次调用能翻页抢完
            for (var i = 0; i < 250; i++) await queue.AddAsync(CreateMessage());

            var takenOld = await queue.TakeAsync(options.PersistGroup, "consumer-dead", 250);
            Assert.Equal(250, takenOld.Count);

            var claimed = await queue.ClaimStaleAsync(options.PersistGroup, "consumer-alive", 1, 100);

            Assert.Equal(250, claimed);
            // 抢完都在自己手上，下一轮读取能全部取出
            var takenNew = await queue.TakeAsync(options.PersistGroup, "consumer-alive", 300);
            Assert.Equal(250, takenNew.Count);
        }
        finally
        {
            queue.Dispose();
            RemoveKey(key);
        }
    }
}
