namespace EFBM.Web.Queue;

/// <summary>单次安全裁剪所需的消费组视图（已与 NewLife.Redis 类型解耦，便于测试）。</summary>
public sealed record GroupView
{
    /// <summary>消费组名。</summary>
    public String Name { get; init; }

    /// <summary>该组最后投递 ID（<c>last-delivered-id</c>）。</summary>
    public String LastDeliveredId { get; init; }

    /// <summary>该组未确认消息的最小 ID（<c>XPENDING</c> 摘要的 <c>startId</c>）；无未确认时为 <c>null</c>。</summary>
    public String PendingMinId { get; init; }

    /// <summary>该组未确认消息数。</summary>
    public Int32 PendingCount { get; init; }
}

/// <summary>一次安全裁剪的执行结果，供后台任务记录日志与决定后续行为。</summary>
public sealed class StreamTrimReport
{
    /// <summary>是否实际发起了 <c>XTRIM</c>（区别于跳过）。</summary>
    public Boolean Ran { get; private set; }

    /// <summary>是否成功裁剪（<see cref="Ran"/> 且删除条数 ≥ 0）。</summary>
    public Boolean Applied { get; private set; }

    /// <summary>跳过原因（未启用 / 无消费组 / 边界不可解析等）。</summary>
    public String SkippedReason { get; private set; }

    /// <summary>Redis 版本过低（< 6.2，不支持 <c>XTRIM MINID</c>）导致不可用。</summary>
    public Boolean Unsupported { get; private set; }

    /// <summary>异常信息（若有）。</summary>
    public String ErrorMessage { get; private set; }

    /// <summary>参与计算的消费组数量。</summary>
    public Int32 GroupCount { get; private set; }

    /// <summary>安全边界毫秒（各组最小已确认 ID）。</summary>
    public Int64 SafeMs { get; private set; }

    /// <summary>保留下限毫秒（最近 N 天）。</summary>
    public Int64 FloorMs { get; private set; }

    /// <summary>最终裁剪点毫秒（<c>Min(Safe, Floor)</c>）。</summary>
    public Int64 EffectiveMs { get; private set; }

    /// <summary>本次删除的消息条数。</summary>
    public Int32 TrimmedCount { get; private set; }

    /// <summary>标记为本轮跳过。</summary>
    public StreamTrimReport Skipped(String reason)
    {
        SkippedReason = reason;
        return this;
    }

    /// <summary>标记为已裁剪。</summary>
    public StreamTrimReport MarkApplied(Int32 groupCount, Int64 safeMs, Int64 floorMs, Int64 effectiveMs, Int32 trimmed)
    {
        Ran = true;
        Applied = true;
        GroupCount = groupCount;
        SafeMs = safeMs;
        FloorMs = floorMs;
        EffectiveMs = effectiveMs;
        TrimmedCount = trimmed;
        return this;
    }

    /// <summary>标记为 Redis 不可用（版本过低）。</summary>
    public StreamTrimReport MarkUnsupported(String message)
    {
        Unsupported = true;
        ErrorMessage = message;
        return this;
    }

    /// <summary>标记为发生异常。</summary>
    public StreamTrimReport MarkError(String message)
    {
        ErrorMessage = message;
        return this;
    }
}

/// <summary>Stream 安全裁剪抽象；由 <see cref="RedisEventQueue"/> 实现（依赖 NewLife.Redis）。</summary>
public interface IStreamTrimmer
{
    /// <summary>按各组最小已确认 ID 安全裁剪共享上报流；不删除任何未确认消息。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行报告。</returns>
    Task<StreamTrimReport> TrimSafeAsync(CancellationToken cancellationToken = default);
}
