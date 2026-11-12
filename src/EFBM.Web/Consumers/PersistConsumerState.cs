using EFBM.Core;

namespace EFBM.Web.Consumers;

/// <summary>落库消费者的运行指标，供 <c>/health</c> 与运维判读（FR-17）。</summary>
/// <remarks>
/// 纯内存计数器，重启归零；持久化的真相在 <c>DeviceEventRaw</c> 与 <c>DeadLetter</c> 表。
/// 「消费停了没」的判读：<c>running=false</c> 且 <c>RedisEnabled=true</c> 时数据只会堆在 Stream，
/// 上报仍 200——这正是 P3 上线后必须盯的指标（需求说明 §3.6 确认语义）。
/// </remarks>
public sealed class PersistConsumerState
{
    private Int64 _taken;
    private Int64 _persisted;
    private Int64 _duplicates;
    private Int64 _acked;
    private Int64 _failed;
    private Int64 _deadLettered;
    private Int64 _retrying;

    /// <summary>消费者是否处于运行循环（纯日志模式下为 <c>false</c>，属正常）。</summary>
    public Boolean Running { get; private set; }

    /// <summary>进入运行循环的时间 UTC；未启动为 <c>null</c>。</summary>
    public DateTime? RunningSince { get; private set; }

    /// <summary>最近一次成功取到消息的时间 UTC；从未取到为 <c>null</c>。</summary>
    public DateTime? LastTakeAt { get; private set; }

    /// <summary>最近一次 Ack 的时间 UTC。</summary>
    public DateTime? LastAckAt { get; private set; }

    /// <summary>最近一次错误摘要；连续失败时这里是判读起点。</summary>
    public String LastError { get; private set; }

    /// <summary>标记进入运行循环。</summary>
    public void Start()
    {
        Running = true;
        RunningSince = DateTime.UtcNow;
    }

    /// <summary>标记离开运行循环（停机）。</summary>
    public void Stop() => Running = false;

    /// <summary>记录一批消息取出。</summary>
    /// <param name="count">本批条数。</param>
    public void MarkTake(Int32 count)
    {
        Interlocked.Add(ref _taken, count);
        LastTakeAt = DateTime.UtcNow;
    }

    /// <summary>记录一条消息完成处理。</summary>
    /// <param name="outcome">处理结果。</param>
    public void MarkPersisted(PersistOutcome outcome)
    {
        switch (outcome)
        {
            case PersistOutcome.Persisted:
                Interlocked.Increment(ref _persisted);
                break;
            case PersistOutcome.Duplicate:
                Interlocked.Increment(ref _duplicates);
                break;
            case PersistOutcome.DeadLettered:
                Interlocked.Increment(ref _deadLettered);
                break;
        }
    }

    /// <summary>记录一条消息确认。</summary>
    public void MarkAck()
    {
        Interlocked.Increment(ref _acked);
        LastAckAt = DateTime.UtcNow;
    }

    /// <summary>记录一次处理失败（未 Ack，等待重投）。</summary>
    /// <param name="error">错误摘要。</param>
    public void MarkFailed(String error)
    {
        Interlocked.Increment(ref _failed);
        Interlocked.Increment(ref _retrying);
        LastError = error;
    }

    /// <summary>一条消息结束重试（死信或成功）。</summary>
    public void MarkRetryResolved() => Interlocked.Decrement(ref _retrying);

    /// <summary>当前快照。</summary>
    /// <returns>匿名对象，序列化进 <c>/health</c>。</returns>
    public Object Snapshot() => new
    {
        running = Running,
        runningSince = RunningSince,
        lastTakeAt = LastTakeAt,
        lastAckAt = LastAckAt,
        lastError = LastError,
        taken = Interlocked.Read(ref _taken),
        persisted = Interlocked.Read(ref _persisted),
        duplicates = Interlocked.Read(ref _duplicates),
        acked = Interlocked.Read(ref _acked),
        failed = Interlocked.Read(ref _failed),
        deadLettered = Interlocked.Read(ref _deadLettered),
        retrying = Interlocked.Read(ref _retrying),
    };
}
