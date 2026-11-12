namespace EFBM.Core;

/// <summary>单个消费组的运行时指标（对应 <c>XINFO GROUPS</c> / <c>XPENDING</c> / <c>XINFO CONSUMERS</c>）。</summary>
/// <remarks>
/// <c>Lag</c> 是「该组还没读到的消息条数」，现场最关心的就是它：持续增长说明消费能力不足或消费者挂了。
/// Redis 6.2 不提供 <c>lag</c> 字段，因此这里用「按已投递 ID 采样计数」的方式估算，
/// 采样上限见 <see cref="QueueMetricsAnalyzer.LagSampleLimit"/>；触顶时 <see cref="LagCapped"/> 为 <c>true</c>，
/// 页面显示为「≥N」，避免拿一个编造出来的精确数字骗人。
/// </remarks>
public sealed class QueueGroupMetric
{
    /// <summary>消费组名。</summary>
    public String Name { get; init; }

    /// <summary>组内注册的消费者个数。</summary>
    public Int32 Consumers { get; init; }

    /// <summary>最近 <see cref="QueueMetricsAnalyzer.ZombieIdleSeconds"/> 秒内取过消息的消费者个数。</summary>
    public Int32 ActiveConsumers { get; init; }

    /// <summary>未确认消息数（PEL 深度）。</summary>
    public Int64 Pending { get; init; }

    /// <summary>PEL 中最小的消息 ID；为空表示没有未确认消息。</summary>
    public String PendingMinId { get; init; }

    /// <summary>消费者最长空闲秒数；没有消费者时为 <c>null</c>。</summary>
    public Int64? MaxConsumerIdleSeconds { get; init; }

    /// <summary>最后投递的消息 ID。</summary>
    public String LastDeliveredId { get; init; }

    /// <summary>尚未投递给该组的消息条数。</summary>
    public Int64 Lag { get; init; }

    /// <summary>是否因采样上限而只能给出下限。</summary>
    public Boolean LagCapped { get; init; }

    /// <summary>是否本系统的落库组；页面据此高亮。</summary>
    public Boolean IsPersistGroup { get; init; }

    /// <summary>是否积压最多的组。</summary>
    public Boolean IsSlowest { get; set; }

    /// <summary>是否僵尸组：有积压但消费者全部长时间空闲，或根本没有消费者。</summary>
    public Boolean IsZombie { get; set; }

    /// <summary>诊断提示，例如「无消费者且 lag=1200，需检查外部系统」。</summary>
    public String Advice { get; set; }

    /// <summary>积压文案，触顶时带下限符号。</summary>
    public String LagText => LagCapped ? $"≥{Lag}" : Lag.ToString();

    /// <summary>消费者空闲文案。</summary>
    public String IdleText => MaxConsumerIdleSeconds switch
    {
        null => "—",
        < 60 => $"{MaxConsumerIdleSeconds} 秒",
        < 3600 => $"{MaxConsumerIdleSeconds / 60} 分钟",
        _ => $"{MaxConsumerIdleSeconds / 3600.0:F1} 小时",
    };
}

/// <summary>安全裁剪最近一次的运行结果，供监控页展示「裁剪水位」。</summary>
public sealed class TrimSnapshot
{
    /// <summary>是否启用了安全裁剪。</summary>
    public Boolean Enabled { get; init; }

    /// <summary>最近一次执行时间；从未执行时为 <see cref="DateTime.MinValue"/>。</summary>
    public DateTime LastRun { get; init; }

    /// <summary>本次裁剪水位（Stream ID）；未裁剪时为空。</summary>
    public String WatermarkId { get; init; }

    /// <summary>本次删除条数。</summary>
    public Int64 Trimmed { get; init; }

    /// <summary>跳过 / 失败原因。</summary>
    public String Message { get; init; }
}

/// <summary>共享上报流的整体运行指标，后台「消费组监控」页一次性取全。</summary>
public sealed class QueueMetrics
{
    /// <summary>是否启用共享流；纯日志模式为 <c>false</c>。</summary>
    public Boolean Enabled { get; init; } = true;

    /// <summary>是否连接成功。</summary>
    public Boolean Connected { get; init; }

    /// <summary>失败原因摘要；成功时为 <c>null</c>。</summary>
    public String Error { get; init; }

    /// <summary>Stream key。</summary>
    public String StreamKey { get; init; }

    /// <summary>本系统落库消费组名。</summary>
    public String PersistGroup { get; init; }

    /// <summary>Stream 当前长度（<c>XLEN</c>）。</summary>
    public Int64 Length { get; init; }

    /// <summary>首条消息 ID。</summary>
    public String FirstId { get; init; }

    /// <summary>末条消息 ID。</summary>
    public String LastId { get; init; }

    /// <summary>最后写入时间。</summary>
    public DateTime LastGenerated { get; init; }

    /// <summary>Redis 版本。</summary>
    public String RedisVersion { get; init; }

    /// <summary>Redis 已用内存（人类可读，例如 <c>894.43K</c>）。</summary>
    public String UsedMemory { get; init; }

    /// <summary>Redis 角色：<c>master</c> / <c>slave</c> / 未知时为空。</summary>
    /// <remarks>健康检查据此判断「连的是主节点还是从节点」：连到从节点时写入会失败，必须能一眼看出来。</remarks>
    public String RedisRole { get; init; }

    /// <summary>Redis 当前客户端连接数。</summary>
    public Int32? ConnectedClients { get; init; }

    /// <summary>写入速率（条/秒），基于两次采样差值；首次采样为 <c>null</c>。</summary>
    public Double? PublishRate { get; init; }

    /// <summary>采样时间。</summary>
    public DateTime SampledAt { get; init; }

    /// <summary>安全裁剪快照；Redis 不可用时为默认快照而不是 <c>null</c>。</summary>
    /// <remarks>
    /// 这里给默认值是刻意的：监控页要在 Redis 挂掉时照常打开，
    /// 若该项为 <c>null</c>，视图里任何 <c>trim.Xxx</c> 都会把「看故障的页面」变成 500。
    /// </remarks>
    public TrimSnapshot Trim { get; init; } = new();

    /// <summary>各组指标。</summary>
    public IReadOnlyList<QueueGroupMetric> Groups { get; init; } = Array.Empty<QueueGroupMetric>();

    /// <summary>全部组未确认消息合计。</summary>
    public Int64 TotalPending => Groups.Sum(e => e.Pending);

    /// <summary>全部组积压合计。</summary>
    public Int64 TotalLag => Groups.Sum(e => e.Lag);

    /// <summary>是否有组被判定为僵尸组。</summary>
    public Boolean HasZombie => Groups.Any(e => e.IsZombie);

    /// <summary>构造一个「不可用」快照：Redis 未启用或连接失败。</summary>
    /// <param name="error">原因摘要。</param>
    /// <param name="streamKey">Stream key。</param>
    /// <param name="enabled">是否启用共享流。</param>
    /// <returns>不可用快照。</returns>
    public static QueueMetrics Unavailable(String error, String streamKey, Boolean enabled = true) => new()
    {
        Enabled = enabled,
        Connected = false,
        Error = error,
        StreamKey = streamKey,
        SampledAt = DateTime.Now,
    };
}

/// <summary>共享上报流指标探测：页面只依赖这个接口，测试可用替身。</summary>
/// <remarks>与 <see cref="IEventQueueProbe"/> 分开：健康检查只要长度，监控页要各组明细，实现方不得抛异常。</remarks>
public interface IQueueMetricsProbe
{
    /// <summary>读取一份完整指标快照。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指标快照；Redis 不可用时返回带错误摘要的快照。</returns>
    Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>消费组指标的后处理：最慢组、僵尸组与写入速率的纯函数实现。</summary>
/// <remarks>
/// 放在 Core 里是为了可测：判定逻辑一旦写进控制器就只能靠页面点一遍来验证，
/// 而阈值判断恰恰是最容易写反的地方（把空闲时间当活跃时间、把 pending 当 lag 等）。
/// </remarks>
public static class QueueMetricsAnalyzer
{
    /// <summary>lag 采样上限：超过只报下限，避免一次拉几百 KB 报文把监控页拖慢。</summary>
    public const Int32 LagSampleLimit = 200;

    /// <summary>消费者空闲超过该秒数即视为不活跃。</summary>
    public const Int32 ZombieIdleSeconds = 300;

    /// <summary>标注最慢组 / 僵尸组，并给出诊断建议。</summary>
    /// <param name="metrics">指标快照，原地修改各组标记。</param>
    /// <param name="idleSeconds">僵尸判定阈值（秒），默认 <see cref="ZombieIdleSeconds"/>。</param>
    /// <returns>传入的快照，便于链式调用。</returns>
    public static QueueMetrics Annotate(QueueMetrics metrics, Int32 idleSeconds = ZombieIdleSeconds)
    {
        if (metrics?.Groups == null || metrics.Groups.Count == 0) return metrics;

        // 最慢组：lag 最大者；全部为 0 时不标注，免得到处都是「最慢」
        var slowest = metrics.Groups.OrderByDescending(e => e.Lag).ThenByDescending(e => e.Pending).FirstOrDefault();
        if (slowest != null && slowest.Lag > 0) slowest.IsSlowest = true;

        foreach (var group in metrics.Groups)
        {
            // 僵尸组有两类：有积压却没人消费；有积压但消费者全部长时间空闲
            var noConsumer = group.Consumers == 0 && group.Lag > 0;
            var allIdle = group.Consumers > 0 && group.Lag > 0 && group.MaxConsumerIdleSeconds >= idleSeconds;

            group.IsZombie = noConsumer || allIdle;
            group.Advice = group switch
            {
                { IsZombie: true, Consumers: 0 } => "有积压但没有消费者，检查外部系统的消费进程",
                { IsZombie: true } => $"有积压且消费者已空闲 {group.IdleText}，检查消费进程是否卡死",
                { Pending: > 0, Consumers: 0 } => "存在未确认消息但消费者已下线，消息会停留在 PEL",
                { LagCapped: true } => "积压超过采样上限，页面只显示下限",
                _ => null,
            };
        }

        return metrics;
    }

    /// <summary>按两次采样计算写入速率（条/秒）；时间差不合法或首次采样时返回 <c>null</c>。</summary>
    /// <param name="previousLength">上次长度。</param>
    /// <param name="previousAt">上次采样时间。</param>
    /// <param name="length">本次长度。</param>
    /// <param name="now">本次采样时间。</param>
    /// <returns>速率；无法计算时为 <c>null</c>。</returns>
    public static Double? Rate(Int64 previousLength, DateTime previousAt, Int64 length, DateTime now)
    {
        var seconds = (now - previousAt).TotalSeconds;
        if (seconds < 0.5) return null;

        // 长度变小说明中间发生了裁剪，差值没有意义，宁可显示「—」也不给出负速率
        var delta = length - previousLength;
        return delta < 0 ? null : Math.Round(delta / seconds, 2);
    }
}

/// <summary>Stream 消息 ID 的比较与换算。</summary>
/// <remarks>
/// Stream ID 形如 <c>毫秒-序号</c>，两侧都是 64 位整数，逐段比较即可；
/// 直接按字符串比较会在位数变化时出错（<c>"9-0" &gt; "10-0"</c>），所以单独收在这里并有单测。
/// </remarks>
public static class StreamIds
{
    /// <summary>起始 ID，表示「从最早一条开始」。</summary>
    public const String Zero = "0-0";

    /// <summary>比较两个 Stream ID。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>左小于右返回负数，相等返回 0，大于返回正数。</returns>
    public static Int32 Compare(String left, String right)
    {
        var (lm, ls) = Split(left);
        var (rm, rs) = Split(right);

        var result = lm.CompareTo(rm);
        return result != 0 ? result : ls.CompareTo(rs);
    }

    /// <summary>用毫秒时间戳构造 Stream ID（序号固定为 0），用于裁剪水位的可读展示。</summary>
    /// <param name="milliseconds">Unix 毫秒。</param>
    /// <returns>形如 <c>1730000000000-0</c> 的 ID。</returns>
    public static String FromMilliseconds(Int64 milliseconds) => $"{milliseconds}-0";

    /// <summary>拆分毫秒与序号；非法格式按 0 处理，不抛异常。</summary>
    private static (Int64 Ms, Int64 Seq) Split(String id)
    {
        if (String.IsNullOrEmpty(id)) return (0, 0);

        var index = id.IndexOf('-');
        if (index < 0) return (ToInt64(id), 0);

        return (ToInt64(id[..index]), ToInt64(id[(index + 1)..]));
    }

    private static Int64 ToInt64(String value) => Int64.TryParse(value, out var result) ? result : 0;
}
