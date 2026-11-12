namespace EFBM.Core;

/// <summary>从消费组取出的一条消息，携带 Stream 消息 ID 供显式 <c>XACK</c> 与安全裁剪定位。</summary>
public sealed class QueueMessage
{
    /// <summary>Redis Stream 消息 ID（<c>XADD</c> 返回值）。</summary>
    public String StreamId { get; set; }

    /// <summary>消息体。</summary>
    public StreamMessage Message { get; set; }
}

/// <summary>上报流抽象，隔离 <c>NewLife.Redis.RedisStream&lt;T&gt;</c> 的具体实现。</summary>
/// <remarks>
/// 本期实现基于 Redis Stream 多消费组；接口只暴露「写入 / 取出 / 确认」三个动作，
/// 使接收层与消费者不依赖具体队列组件。实现必须遵守：业务成功后才确认、失败留在 PEL、
/// 禁止框架按 <c>MaxRetry</c> 自动丢弃、禁止按长度 / 时间裁剪未确认消息（需求说明 §3.6、FR-22）。
/// </remarks>
public interface IEventQueue
{
    /// <summary>一次写入共享流，返回 Stream 消息 ID。</summary>
    /// <param name="message">消息体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><c>XADD</c> 返回的消息 ID。</returns>
    Task<String> AddAsync(StreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>以指定消费组 / 消费者批量取出消息，不自动确认。</summary>
    /// <param name="group">消费组名，如 <see cref="EfbmDefaults.PersistGroup"/>。</param>
    /// <param name="consumer">组内唯一消费者名（主机名 + 进程 ID）。</param>
    /// <param name="count">单次最多取出的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取到的消息列表；无消息返回空集合。</returns>
    Task<IReadOnlyList<QueueMessage>> TakeAsync(String group, String consumer, Int32 count, CancellationToken cancellationToken = default);

    /// <summary>业务处理成功后显式确认消息。</summary>
    /// <param name="group">消费组名。</param>
    /// <param name="streamId">Stream 消息 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task AckAsync(String group, String streamId, CancellationToken cancellationToken = default);
}
