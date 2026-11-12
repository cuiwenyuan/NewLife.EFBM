namespace EFBM.Core;

/// <summary>本地兜底队列中的一条待回灌消息。</summary>
/// <remarks><see cref="RawJson"/> 存的是 <b>Stream 消息体 JSON</b>（不是设备原始报文），
/// 回灌时反序列化后原样重新 <c>XADD</c>，保留原 <c>messageId</c> / <c>eventKey</c> / <c>replayOf</c>。</remarks>
public sealed class FallbackEntry
{
    /// <summary>兜底记录主键。</summary>
    public Int64 Id { get; set; }

    /// <summary>消息追踪 ID。</summary>
    public String MessageId { get; set; }

    /// <summary>全链路幂等键。</summary>
    public String EventKey { get; set; }

    /// <summary>Stream 消息体 JSON 全文。</summary>
    public String RawJson { get; set; }

    /// <summary>已尝试回灌次数。</summary>
    public Int32 RetryCount { get; set; }
}

/// <summary>本地兜底暂存抽象：Redis 不可用时保住数据，恢复后回灌（FR-11、ING-4b）。</summary>
/// <remarks>
/// 正常路径<b>完全不会调用</b>本接口，因此同步链路零 DB 写；
/// 只有 Redis 故障路径才产生一次本地写入，二者同时失败才返回 503。
/// </remarks>
public interface ILocalFallbackStore
{
    /// <summary>暂存一条未能入流的消息。</summary>
    /// <param name="message">已组装的 Stream 消息体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；失败应抛出异常，由接收服务判 503。</returns>
    Task SaveAsync(StreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>取出一批尚未回灌的消息，按主键升序。</summary>
    /// <param name="count">最多取出的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待回灌消息列表；无记录返回空集合。</returns>
    Task<IReadOnlyList<FallbackEntry>> TakePendingAsync(Int32 count, CancellationToken cancellationToken = default);

    /// <summary>标记一条消息已成功重新入流。</summary>
    /// <param name="id">兜底记录主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task MarkRequeuedAsync(Int64 id, CancellationToken cancellationToken = default);

    /// <summary>累加回灌失败次数，供后台展示与告警。</summary>
    /// <param name="id">兜底记录主键。</param>
    /// <param name="error">最近一次错误摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task MarkRetryAsync(Int64 id, String error, CancellationToken cancellationToken = default);
}
