namespace EFBM.Core;

/// <summary>PEL 接管规则：判断一条「别人手上」的未确认消息是否该由当前消费者接手。</summary>
/// <remarks>
/// 为什么要接管：消费者名是「主机名@进程号」，进程重启就换名字。旧消费者留在 PEL 里的
/// 未确认消息<b>再也不会被任何人读到</b>——新消费者只读自己的 PEL（<c>XREADGROUP ... 0</c>），
/// 于是这些消息既没落库、也不进死信，只会在 <c>XPENDING</c> 里永久挂着。
/// 压测时实测：三轮重启后消费组挂着 3 个僵尸消费者、264 条永远不处理的积压。
///
/// 判据只有两条，且必须同时满足：
/// ① 消息不归我 —— 自己的 PEL 由正常重投路径处理，抢自己没有意义；
/// ② 空闲时长超过阈值 —— 别人可能正处理到一半，抢走会导致重复处理（幂等能兜住，但没必要）。
/// 阈值默认 5 分钟，远大于单条处理耗时（毫秒级），只会命中真正卡死的消息。
/// </remarks>
public static class PelClaimRules
{
    /// <summary>默认空闲阈值（秒）：超过该时长仍未被确认的消息视为遗留。</summary>
    public const Int32 DefaultIdleSeconds = 300;

    /// <summary>默认单轮扫描条数：一次最多看多少条 pending。</summary>
    public const Int32 DefaultBatchSize = 100;

    /// <summary>单次接管最多翻多少页：防止一次强杀留下海量遗留时把启动过程拖住。</summary>
    /// <remarks>100 条/页 × 20 页 = 单次最多接管 2000 条，剩下的留给下一个扫描周期。</remarks>
    public const Int32 MaxClaimRounds = 20;

    /// <summary>判断是否应由当前消费者接管。</summary>
    /// <param name="owner">消息当前归属的消费者名；空表示无主（可接管）。</param>
    /// <param name="consumer">当前消费者名。</param>
    /// <param name="idleMs">消息已空闲的毫秒数。</param>
    /// <param name="minIdleMs">接管所需的空闲阈值（毫秒）；<c>&lt;=0</c> 视为不启用接管。</param>
    /// <returns>应当接管返回 <c>true</c>。</returns>
    public static Boolean ShouldClaim(String owner, String consumer, Int32 idleMs, Int32 minIdleMs)
    {
        if (minIdleMs <= 0) return false;

        // 自己的消息由 PEL 重投路径处理，不需要抢
        if (!String.IsNullOrEmpty(owner) && String.Equals(owner, consumer, StringComparison.Ordinal)) return false;

        return idleMs >= minIdleMs;
    }
}

/// <summary>可接管他人遗留未确认消息的队列实现。</summary>
/// <remarks>
/// 与 <see cref="IEventQueue"/> 分开定义的理由同 <see cref="IEventQueueProbe"/>：
/// 接管是 Redis Stream 特有的运维能力（<c>XPENDING</c> + <c>XCLAIM</c>），
/// 不应强迫测试替身实现；消费者按能力探测后启用。
/// </remarks>
public interface IPelClaimer
{
    /// <summary>把其他消费者空闲超时的未确认消息改判给当前消费者。</summary>
    /// <param name="group">消费组名。</param>
    /// <param name="consumer">当前消费者名（接管后的归属）。</param>
    /// <param name="minIdleMs">空闲阈值（毫秒）；<c>&lt;=0</c> 时不做任何事。</param>
    /// <param name="count">单轮最多扫描的 pending 条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际接管成功的条数。</returns>
    Task<Int32> ClaimStaleAsync(String group, String consumer, Int32 minIdleMs, Int32 count, CancellationToken cancellationToken = default);
}
