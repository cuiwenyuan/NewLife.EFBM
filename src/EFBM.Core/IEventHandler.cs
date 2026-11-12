namespace EFBM.Core;

/// <summary>事件处理器抽象：按 <c>event</c> 字符串注册表分发，新增事件只加一个实现。</summary>
/// <remarks>
/// 注册表位于 <c>EFBM.Web/Consumers/Handlers</c>（P3 落地）；接入层不写 <c>switch</c> 硬编码。
/// 未知事件由 <see cref="UnknownEventName"/> 对应的兜底 handler 只存原始报文并打标，
/// 补上 handler 后可从历史 <c>RawJson</c> 重放解析（FR-26）。
/// </remarks>
public interface IEventHandler
{
    /// <summary>兜底 handler 注册键；命中所有未登记事件。</summary>
    public const String UnknownEventName = "*";

    /// <summary>本处理器负责的事件名。</summary>
    String Event { get; }

    /// <summary>处理一条已解析的上报。</summary>
    /// <param name="envelope">报文解析结果，含原文与两个键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；抛出异常即不确认消息，由 PEL 重投。</returns>
    Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}
