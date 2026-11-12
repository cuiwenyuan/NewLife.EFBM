namespace EFBM.Core;

/// <summary>队列运行时状态快照，供 <c>/health</c> 与运维排查使用。</summary>
/// <remarks>
/// 只读、可失败：Redis 不可用时返回 <see cref="Connected"/>=<c>false</c> 与错误摘要，
/// 不抛异常——健康检查本身要能在依赖故障时回答「哪里坏了」。
/// </remarks>
public sealed class QueueStatus
{
    /// <summary>是否启用共享流；Beta 只写文本日志时为 <c>false</c>，不算故障。</summary>
    public Boolean Enabled { get; set; } = true;

    /// <summary>是否连接成功。</summary>
    public Boolean Connected { get; set; }

    /// <summary>Stream 当前长度（<c>XLEN</c>），即已写入的消息条数。</summary>
    public Int64 Length { get; set; }

    /// <summary>失败原因摘要；成功时为 <c>null</c>。</summary>
    public String Error { get; set; }
}

/// <summary>可探测运行时状态的队列实现。</summary>
/// <remarks>
/// 与 <see cref="IEventQueue"/> 分开定义：接收链路只依赖写入 / 读取 / 确认三个动作，
/// 探测属于运维能力，不应强迫测试替身实现。
/// </remarks>
public interface IEventQueueProbe
{
    /// <summary>读取队列状态快照；实现方不得抛异常。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态快照。</returns>
    Task<QueueStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
