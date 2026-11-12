using System.Text.Json;
using EFBM.Core;
using NewLife.Caching;
using NewLife.Caching.Queues;

namespace EFBM.Web.Queue;

/// <summary>基于 <c>NewLife.Redis.RedisStream&lt;String&gt;</c> 的共享上报流实现。</summary>
/// <remarks>
/// 消息体以 JSON 字符串写入 Stream 的 <c>__data</c> 字段（PrimitiveKey），
/// 一次 <c>XADD</c> 后每个消费组各拿一份全量，组间 Ack 独立。
///
/// 三个必须接管的框架默认行为（已核实 NewLife.Redis 6.4.2026.201 源码）：
/// ① <c>MaxLength=100万</c> 会触发 <c>XADD MAXLEN ~</c> 与定时 <c>XTRIM MAXLEN</c>，故设为 0；
/// ② <c>Expire=7天</c> 会触发定时 <c>XTRIM MINID</c>，故设为 <see cref="TimeSpan.Zero"/>；
///   两者都不判断消费组是否已消费，会静默删除未确认消息；
/// ③ <c>MaxRetry=10</c> 会让 <c>TakeMessagesAsync</c> 内部的 <c>RetryAck()</c> 把超限消息自动 Ack 丢弃，
///   故改用 <c>ReadGroupAsync</c> 直接读取 + 显式 <c>Ack</c>，并把 MaxRetry 设为极大值兜底。
///
/// 安全裁剪（按所有消费组最小已确认 ID）由 P4 的后台任务实现；
/// 本地兜底写入后的回灌由 P2 的 <c>FallbackRequeueService</c> 负责。
/// 本类只负责写入、读取与确认，不自动裁剪、不自动丢弃。
/// </remarks>
public sealed class RedisEventQueue : IEventQueue, IEventQueueProbe, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly EfbmOptions _options;
    private readonly FullRedis _redis;
    private readonly RedisStream<String> _stream;
    private readonly Object _sync = new();
    private volatile Boolean _groupReady;

    /// <summary>构造 Stream 接入对象；<b>不发起网络 IO</b>，首次读写时才连接 Redis 并建组。</summary>
    /// <param name="options">EFBM 配置节。</param>
    public RedisEventQueue(EfbmOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _redis = new FullRedis(options.RedisServer, options.RedisPassword, options.RedisDb);
        _stream = _redis.GetStream<String>(options.StreamKey);

        // 关闭一切自动裁剪：设备失败不重发，任何静默删除都是永久缺口
        _stream.MaxLength = 0;
        _stream.Expire = TimeSpan.Zero;
        _stream.MaxRetry = Int32.MaxValue;
        _stream.FromLastOffset = false;         // 组误删重建时从头重放，重复由 EventKey 收敛
        _stream.BlockTime = 1000;
        _stream.Group = options.PersistGroup;
        _stream.Consumer = $"{Environment.MachineName}@{Environment.ProcessId}";

        //!!! 不在构造函数里 SetGroup：XGROUP CREATE 会真实连接 Redis。若此处抛异常，
        //    DI 解析 IEventQueue 失败会让整个请求 500，接收服务根本走不到「本地兜底仍 200」的降级分支。
        //    建组延迟到首次 AddAsync / TakeAsync，并把失败异常交给调用方处理。
    }

    /// <summary>一次写入共享流，返回 Stream 消息 ID。</summary>
    /// <param name="message">消息体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><c>XADD</c> 返回的消息 ID。</returns>
    public Task<String> AddAsync(StreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        cancellationToken.ThrowIfCancellationRequested();

        // Beta 纯日志模式：不连 Redis，直接走本地兜底 / 文本日志，避免每条请求都等连接超时
        if (!_options.RedisEnabled) throw new InvalidOperationException("Redis 未启用（EFBM:RedisEnabled=false）");

        EnsureGroup();
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return Task.FromResult(_stream.Add(json, null));
    }

    /// <summary>按消费组取出消息，不自动确认。</summary>
    /// <param name="group">消费组名。</param>
    /// <param name="consumer">组内唯一消费者名。</param>
    /// <param name="count">单次最多取出的条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取到的消息列表；无消息返回空集合。</returns>
    public async Task<IReadOnlyList<QueueMessage>> TakeAsync(String group, String consumer, Int32 count, CancellationToken cancellationToken = default)
    {
        if (String.IsNullOrEmpty(group)) throw new ArgumentNullException(nameof(group));
        if (String.IsNullOrEmpty(consumer)) throw new ArgumentNullException(nameof(consumer));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        EnsureGroup();

        // 先读当前消费者的 PEL（id=0），让未确认消息优先重投；再读新消息（id=>）。
        // 跨消费者的 XAUTOCLAIM / Claim 归属转移在 P3 消费者里补。
        var pending = await _stream.ReadGroupAsync(group, consumer, count, 0, "0", cancellationToken).ConfigureAwait(false);
        var messages = pending is { Count: > 0 }
            ? pending
            : await _stream.ReadGroupAsync(group, consumer, count, _stream.BlockTime, ">", cancellationToken).ConfigureAwait(false);

        if (messages == null || messages.Count == 0) return Array.Empty<QueueMessage>();

        var result = new List<QueueMessage>(messages.Count);
        foreach (var item in messages)
        {
            var json = ReadPrimitiveBody(item.Body);
            if (String.IsNullOrEmpty(json)) continue;

            var message = JsonSerializer.Deserialize<StreamMessage>(json, JsonOptions);
            if (message == null) continue;

            result.Add(new QueueMessage { StreamId = item.Id, Message = message });
        }

        return result;
    }

    /// <summary>业务处理成功后显式确认；失败不调用本方法，消息留在 PEL 由 AutoClaim 重投。</summary>
    /// <param name="group">消费组名。</param>
    /// <param name="streamId">Stream 消息 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task AckAsync(String group, String streamId, CancellationToken cancellationToken = default)
    {
        if (String.IsNullOrEmpty(group)) throw new ArgumentNullException(nameof(group));
        if (String.IsNullOrEmpty(streamId)) throw new ArgumentNullException(nameof(streamId));
        cancellationToken.ThrowIfCancellationRequested();

        _stream.Ack(group, streamId);
        return Task.CompletedTask;
    }

    /// <summary>当前落库消费组的组名，供监控与日志使用。</summary>
    /// <returns>消费组名。</returns>
    public String PersistGroup => _options.PersistGroup;

    /// <summary>读取队列状态：连接是否可用与 Stream 当前长度，用于 Beta 验收与运维排查。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态快照；Redis 不可用时返回错误摘要而不抛异常。</returns>
    public Task<QueueStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.RedisEnabled)
            return Task.FromResult(new QueueStatus { Enabled = false, Connected = false, Error = "Redis 未启用（EFBM:RedisEnabled=false）" });

        try
        {
            // XLEN 是 O(1)，可以高频调用；不触发建组，也不改变任何状态
            return Task.FromResult(new QueueStatus { Enabled = true, Connected = true, Length = _stream.Count });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new QueueStatus { Enabled = true, Connected = false, Error = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }

    /// <summary>确保落库消费组存在（startId=0 + MKSTREAM），失败时保持未就绪以便下次重试。</summary>
    /// <remarks>落库组必须是所有组里最先创建的；本方法把 Redis 连接异常留给调用方决定降级还是重试。</remarks>
    private void EnsureGroup()
    {
        if (_groupReady) return;

        lock (_sync)
        {
            if (_groupReady) return;
            // 失败时不置位：下一轮重新执行 XGROUP CREATE，异常交由调用方处理
            _stream.SetGroup(_options.PersistGroup);
            _groupReady = true;
        }
    }

    /// <summary>从 Stream entry 的名值交替数组中取出 <c>__data</c> 字段的值。</summary>
    /// <param name="body">字段名 / 值交替数组。</param>
    /// <returns>消息体 JSON；未找到返回 <c>null</c>。</returns>
    private static String ReadPrimitiveBody(String[] body)
    {
        if (body == null || body.Length < 2) return null;

        for (var i = 0; i < body.Length - 1; i += 2)
        {
            if (String.Equals(body[i], "__data", StringComparison.Ordinal)) return body[i + 1];
        }

        return null;
    }

    /// <summary>释放 Stream 定时器与 Redis 连接池。</summary>
    public void Dispose()
    {
        _stream.Dispose();
        _redis.Dispose();
    }
}
