using EFBM.Web.Queue;
using Xunit;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,testing-strategy] | skills=[testing-strategy] | agent=default

/// <summary>安全裁剪纯算法单元测试：不依赖 Redis，覆盖 ID 解析、各组取小、保留下限与不可解析边界的保守跳过。</summary>
public sealed class StreamTrimCalculatorTests
{
    [Theory(DisplayName = "ParseMs：从 Stream ID 解析毫秒前缀，通配符返回 -1")]
    [InlineData("1731400000000-0", 1731400000000L)]
    [InlineData("1731400000000-5", 1731400000000L)]
    [InlineData("1731400000000", 1731400000000L)]
    [InlineData("-", -1L)]
    [InlineData("+", -1L)]
    [InlineData("$", -1L)]
    [InlineData("0", -1L)]
    [InlineData("", -1L)]
    [InlineData(null, -1L)]
    [InlineData("not-a-id", -1L)]
    public void ParseMs_各种输入_返回预期毫秒或负一(String id, Int64 expected)
        => Assert.Equal(expected, StreamTrimCalculator.ParseMs(id));

    [Fact(DisplayName = "ComputeSafeMs：无消费组时返回 -1 并给出跳过原因")]
    public void ComputeSafeMs_无组_返回负一并跳过()
    {
        var ms = StreamTrimCalculator.ComputeSafeMs(Array.Empty<GroupView>(), out var reason);
        Assert.Equal(-1, ms);
        Assert.Contains("无消费组", reason);
    }

    [Fact(DisplayName = "ComputeSafeMs：单组无未确认时以 last-delivered-id 为边界")]
    public void ComputeSafeMs_单组已全确认_取LastDelivered()
    {
        var groups = new[] { new GroupView { Name = "efbm-persist", LastDeliveredId = "200-0", PendingMinId = null, PendingCount = 0 } };
        var ms = StreamTrimCalculator.ComputeSafeMs(groups, out _);
        Assert.Equal(200L, ms);
    }

    [Fact(DisplayName = "ComputeSafeMs：单组有未确认时以最旧 pending（StartId）为边界")]
    public void ComputeSafeMs_单组有未确认_取PendingStartId()
    {
        var groups = new[]
        {
            new GroupView { Name = "efbm-persist", LastDeliveredId = "900-0", PendingMinId = "50-0", PendingCount = 3 },
        };
        var ms = StreamTrimCalculator.ComputeSafeMs(groups, out _);
        Assert.Equal(50L, ms);
    }

    [Fact(DisplayName = "ComputeSafeMs：多组取各组边界的最小值（含跨组最小）")]
    public void ComputeSafeMs_多组_取最小边界()
    {
        var groups = new[]
        {
            new GroupView { Name = "efbm-persist", LastDeliveredId = "200-0", PendingMinId = "80-0", PendingCount = 2 },
            new GroupView { Name = "efbm-report", LastDeliveredId = "150-0", PendingMinId = null, PendingCount = 0 },
            new GroupView { Name = "efbm-warehouse", LastDeliveredId = "300-0", PendingMinId = "30-0", PendingCount = 1 },
        };
        var ms = StreamTrimCalculator.ComputeSafeMs(groups, out _);
        // 最小边界来自 efbm-warehouse 的 pending StartId=30
        Assert.Equal(30L, ms);
    }

    [Fact(DisplayName = "ComputeSafeMs：任一消费组边界不可解析时整体保守跳过，避免误删未确认消息")]
    public void ComputeSafeMs_边界不可解析_整体跳过()
    {
        var groups = new[]
        {
            new GroupView { Name = "efbm-persist", LastDeliveredId = "200-0", PendingMinId = null, PendingCount = 0 },
            new GroupView { Name = "bad-group", LastDeliveredId = "not-a-id", PendingMinId = null, PendingCount = 0 },
        };
        var ms = StreamTrimCalculator.ComputeSafeMs(groups, out var reason);
        Assert.Equal(-1, ms);
        Assert.Contains("bad-group", reason);
    }

    [Theory(DisplayName = "ComputeEffectiveMs：保留下限只让裁剪点更靠前，绝不越过未确认边界")]
    [InlineData(100L, 50L, 50L)]   // 下限更靠前 → 删更多已确认旧数据
    [InlineData(100L, 200L, 100L)]  // 下限靠后 → 仍受未确认边界约束
    [InlineData(100L, 100L, 100L)]
    [InlineData(-1L, 100L, -1L)]    // 安全边界非法 → 不裁剪
    public void ComputeEffectiveMs_合并安全边界与保留下限(Int64 safe, Int64 floor, Int64 expected)
        => Assert.Equal(expected, StreamTrimCalculator.ComputeEffectiveMs(safe, floor));
}
