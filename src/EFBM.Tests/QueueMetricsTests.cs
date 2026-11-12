using EFBM.Core;

namespace EFBM.Tests;

/// <summary>消费组指标后处理与 Stream ID 换算：纯函数，不碰 Redis。</summary>
/// <remarks>
/// 这块逻辑决定「页面上哪个组标红、哪个组标黄」，阈值判断最容易写反，
/// 所以全部做成纯函数在这里逐条钉死，而不是靠开一次页面用眼睛看。
/// </remarks>
public class QueueMetricsTests
{
    private static QueueGroupMetric Group(String name, Int32 consumers, Int32 active, Int64 pending, Int64 lag, Int64? idle)
        => new()
        {
            Name = name,
            Consumers = consumers,
            ActiveConsumers = active,
            Pending = pending,
            Lag = lag,
            MaxConsumerIdleSeconds = idle,
        };

    [Fact(DisplayName = "指标后处理_没有消费组时原样返回")]
    public void Annotate_NoGroups()
    {
        var metrics = new QueueMetrics { Connected = true };

        Assert.Same(metrics, QueueMetricsAnalyzer.Annotate(metrics));
        Assert.False(metrics.HasZombie);
    }

    [Fact(DisplayName = "指标后处理_只给 lag 最大的组打最慢标记")]
    public void Annotate_MarksSlowestOnly()
    {
        var metrics = new QueueMetrics
        {
            Groups =
            [
                Group("persist", 2, 2, 0, 10, 1),
                Group("third-party", 1, 1, 0, 800, 2),
                Group("audit", 1, 1, 0, 3, 1),
            ],
        };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.Equal(1, metrics.Groups.Count(e => e.IsSlowest));
        Assert.True(metrics.Groups.Single(e => e.Name == "third-party").IsSlowest);
        Assert.False(metrics.HasZombie);
    }

    [Fact(DisplayName = "指标后处理_全部无积压时不打最慢标记")]
    public void Annotate_NoLag_NoSlowest()
    {
        var metrics = new QueueMetrics { Groups = [Group("a", 1, 1, 0, 0, 1), Group("b", 1, 1, 0, 0, 1)] };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.DoesNotContain(metrics.Groups, e => e.IsSlowest);
    }

    [Fact(DisplayName = "指标后处理_有积压且无消费者判为僵尸组")]
    public void Annotate_ZombieWhenNoConsumer()
    {
        var metrics = new QueueMetrics { Groups = [Group("third-party", 0, 0, 0, 1200, null)] };

        QueueMetricsAnalyzer.Annotate(metrics);

        var group = metrics.Groups[0];
        Assert.True(group.IsZombie);
        Assert.Contains("没有消费者", group.Advice);
        Assert.True(metrics.HasZombie);
    }

    [Fact(DisplayName = "指标后处理_有积压且消费者全部空闲超阈值判为僵尸组")]
    public void Annotate_ZombieWhenAllIdle()
    {
        var metrics = new QueueMetrics { Groups = [Group("order", 3, 0, 5, 40, 600)] };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.True(metrics.Groups[0].IsZombie);
        Assert.Contains("空闲", metrics.Groups[0].Advice);
    }

    [Fact(DisplayName = "指标后处理_无积压时有空闲消费者不算僵尸组")]
    public void Annotate_IdleWithoutLag_NotZombie()
    {
        var metrics = new QueueMetrics { Groups = [Group("order", 1, 0, 0, 0, 86400)] };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.False(metrics.Groups[0].IsZombie);
    }

    [Fact(DisplayName = "指标后处理_未确认消息且消费者下线给出PEL提示")]
    public void Annotate_PendingWithoutConsumer_Advice()
    {
        var metrics = new QueueMetrics { Groups = [Group("order", 0, 0, 7, 0, null)] };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.False(metrics.Groups[0].IsZombie);
        Assert.Contains("PEL", metrics.Groups[0].Advice);
    }

    [Fact(DisplayName = "指标后处理_积压采样触顶时提示只看得到下限")]
    public void Annotate_LagCapped_Advice()
    {
        var metrics = new QueueMetrics
        {
            Groups = [new QueueGroupMetric { Name = "order", Consumers = 1, ActiveConsumers = 1, Lag = 200, LagCapped = true, MaxConsumerIdleSeconds = 1 }],
        };

        QueueMetricsAnalyzer.Annotate(metrics);

        Assert.Equal("≥200", metrics.Groups[0].LagText);
        Assert.Contains("采样上限", metrics.Groups[0].Advice);
    }

    [Fact(DisplayName = "指标后处理_空闲文案按秒分时切换")]
    public void IdleText_Formats()
    {
        Assert.Equal("—", Group("a", 0, 0, 0, 0, null).IdleText);
        Assert.Equal("42 秒", Group("a", 1, 1, 0, 0, 42).IdleText);
        Assert.Equal("5 分钟", Group("a", 1, 1, 0, 0, 300).IdleText);
        Assert.Equal("2.0 小时", Group("a", 1, 1, 0, 0, 7200).IdleText);
    }

    [Fact(DisplayName = "指标后处理_合计积压与未确认")]
    public void Totals()
    {
        var metrics = new QueueMetrics
        {
            Groups = [Group("a", 1, 1, 2, 10, 1), Group("b", 1, 1, 3, 20, 1)],
        };

        Assert.Equal(5, metrics.TotalPending);
        Assert.Equal(30, metrics.TotalLag);
    }

    [Fact(DisplayName = "不可用快照_带错误摘要且未连接")]
    public void Unavailable_Snapshot()
    {
        var metrics = QueueMetrics.Unavailable("连不上", "efbm:events");

        Assert.False(metrics.Connected);
        Assert.True(metrics.Enabled);
        Assert.Equal("连不上", metrics.Error);
        Assert.Empty(metrics.Groups);
        Assert.NotEqual(default(DateTime), metrics.SampledAt);
    }

    [Fact(DisplayName = "写入速率_按两次采样差值计算")]
    public void Rate_Computes()
    {
        var start = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Local);

        Assert.Equal(2.5, QueueMetricsAnalyzer.Rate(100, start, 150, start.AddSeconds(20)) ?? -1, 3);
    }

    [Fact(DisplayName = "写入速率_间隔过短或发生裁剪时不编数字")]
    public void Rate_ReturnsNull()
    {
        var start = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Local);

        Assert.Null(QueueMetricsAnalyzer.Rate(100, start, 150, start.AddMilliseconds(200)));
        Assert.Null(QueueMetricsAnalyzer.Rate(100, start, 90, start.AddSeconds(10)));
    }

    [Fact(DisplayName = "StreamID_按毫秒与序号逐段比较")]
    public void StreamIds_Compare()
    {
        Assert.True(StreamIds.Compare("9-0", "10-0") < 0);
        Assert.True(StreamIds.Compare("10-0", "10-1") < 0);
        Assert.Equal(0, StreamIds.Compare("10-1", "10-1"));
        Assert.True(StreamIds.Compare("10-2", "10-10") < 0);
    }

    [Fact(DisplayName = "StreamID_非法格式按零处理不抛异常")]
    public void StreamIds_BadInput()
    {
        Assert.Equal(0, StreamIds.Compare(null, "0-0"));
        Assert.Equal(0, StreamIds.Compare("abc", "0-0"));
        Assert.True(StreamIds.Compare("3", "2-0") > 0);
    }

    [Fact(DisplayName = "StreamID_毫秒换算")]
    public void StreamIds_FromMilliseconds()
        => Assert.Equal("1730000000000-0", StreamIds.FromMilliseconds(1730000000000));
}
