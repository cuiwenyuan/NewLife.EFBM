using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;

namespace EFBM.Web.Services;

// 📋 生效: instructions=[newlife-global,xcode,development] | skills=[xcode-data-modeling] | agent=default

/// <summary><c>LocalQueueFallback</c> 实体实现的本地兜底暂存（FR-11、ING-4b）。</summary>
/// <remarks>
/// 只有 <c>XADD</c> 失败时才会写入，正常接收链路零 DB 写。
/// <c>RawJson</c> 存的是<b>完整 Stream 消息体 JSON</b>而非设备原始报文，回灌后能保留
/// 原 <c>messageId</c> / <c>eventKey</c> / <c>receivedAt</c> / <c>replayOf</c>，外部消费组可正常幂等。
/// 设备原文另在消息体的 <c>rawJson</c> 字段内全量保留，不会丢失。
/// </remarks>
public sealed class LocalFallbackStore : ILocalFallbackStore
{
    /// <summary>暂存一条未能入流的消息。</summary>
    /// <param name="message">已组装的 Stream 消息体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务；写入失败抛异常，由接收服务判 503。</returns>
    public Task SaveAsync(StreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        cancellationToken.ThrowIfCancellationRequested();

        var entity = new LocalQueueFallback
        {
            MessageId = message.MessageId,
            EventKey = message.EventKey,
            RawJson = EventIngestService.Serialize(message),
            RequeuedAt = EfbmDefaults.UnrequeuedAt,
            RetryCount = 0,
            CreateTime = DateTime.UtcNow,
        };
        entity.Insert();

        return Task.CompletedTask;
    }

    /// <summary>取出一批尚未回灌的消息，按主键升序。</summary>
    /// <param name="count">最多取出的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待回灌消息列表；无记录返回空集合。</returns>
    public Task<IReadOnlyList<FallbackEntry>> TakePendingAsync(Int32 count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        // 用 1970 哨兵值而非 DateTime.MinValue：MySQL DATETIME 无法存零值，NULL 又不能用普通比较查询
        var entities = LocalQueueFallback.FindAll(LocalQueueFallback._.RequeuedAt == EfbmDefaults.UnrequeuedAt);
        var list = entities
            .OrderBy(e => e.Id)
            .Take(count)
            .Select(e => new FallbackEntry
            {
                Id = e.Id,
                MessageId = e.MessageId,
                EventKey = e.EventKey,
                RawJson = e.RawJson,
                RetryCount = e.RetryCount,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<FallbackEntry>>(list);
    }

    /// <summary>标记一条消息已成功重新入流。</summary>
    /// <param name="id">兜底记录主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task MarkRequeuedAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = LocalQueueFallback.FindById(id);
        if (entity != null)
        {
            entity.RequeuedAt = DateTime.UtcNow;
            entity.Update();
        }

        return Task.CompletedTask;
    }

    /// <summary>累加回灌失败次数并记日志，等待下一轮重试，绝不删除记录。</summary>
    /// <param name="id">兜底记录主键。</param>
    /// <param name="error">最近一次错误摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task MarkRetryAsync(Int64 id, String error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = LocalQueueFallback.FindById(id);
        if (entity != null)
        {
            entity.RetryCount += 1;
            entity.Update();
        }

        XTrace.WriteLine("本地兜底回灌失败 id={0} 次数={1} 原因={2}", id, entity?.RetryCount, error);
        return Task.CompletedTask;
    }
}
