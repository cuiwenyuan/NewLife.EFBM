using EFBM.Core;

namespace EFBM.Tests;

/// <summary>PEL 接管规则测试：只抢「别人的、且空闲超阈值」的消息。</summary>
/// <remarks>
/// 消费者名含进程号，重启即换名；旧消费者留在 PEL 里的未确认消息不会再被任何人读到。
/// 压测实测三轮重启后消费组挂着 3 个僵尸消费者、264 条永不处理的积压——这些消息既没落库、
/// 也没进死信，只在 XPENDING 里永久挂着。接管是唯一的出路，但抢错了会重复处理，所以判据要逐条卡住。
/// </remarks>
public class PelClaimRulesTests
{
    [Theory(DisplayName = "PEL接管_别人的消息超时空闲_接管")]
    [InlineData("HOST@100", "HOST@200", 300_000, 300_000)]
    [InlineData("HOST@100", "HOST@200", 900_000, 300_000)]
    [InlineData(null, "HOST@200", 300_000, 300_000)]
    [InlineData("", "HOST@200", 300_000, 300_000)]
    public void OtherStale_Claimed(String owner, String consumer, Int32 idleMs, Int32 minIdleMs)
    {
        Assert.True(PelClaimRules.ShouldClaim(owner, consumer, idleMs, minIdleMs));
    }

    [Fact(DisplayName = "PEL接管_自己的消息不抢")]
    public void OwnMessage_NotClaimed()
    {
        // 自己的 PEL 由正常重投路径处理，抢自己没有意义
        Assert.False(PelClaimRules.ShouldClaim("HOST@200", "HOST@200", 900_000, 300_000));
    }

    [Fact(DisplayName = "PEL接管_别人可能还在处理_不抢")]
    public void OtherFresh_NotClaimed()
    {
        Assert.False(PelClaimRules.ShouldClaim("HOST@100", "HOST@200", 299_999, 300_000));
        Assert.False(PelClaimRules.ShouldClaim("HOST@100", "HOST@200", 0, 300_000));
    }

    [Theory(DisplayName = "PEL接管_阈值关闭时不接管")]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClaimDisabled(Int32 minIdleMs)
    {
        Assert.False(PelClaimRules.ShouldClaim("HOST@100", "HOST@200", 900_000, minIdleMs));
    }

    [Fact(DisplayName = "PEL接管_默认值与契约一致")]
    public void Defaults_MatchContract()
    {
        var options = new EfbmOptions();

        Assert.Equal(PelClaimRules.DefaultIdleSeconds, options.PersistClaimIdleSeconds);
        Assert.Equal(300, options.PersistClaimIdleSeconds);
        Assert.Equal(PelClaimRules.DefaultBatchSize, options.PersistClaimBatchSize);
        Assert.Equal(300, options.PersistClaimIntervalSeconds);
    }

    [Fact(DisplayName = "PEL接管_配置非法_启动自检失败")]
    public void InvalidOptions_FailStartup()
    {
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { PersistClaimIdleSeconds = -1 }.ValidateOrThrow());
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { PersistClaimIntervalSeconds = 0 }.ValidateOrThrow());
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { PersistClaimBatchSize = 0 }.ValidateOrThrow());
    }
}
