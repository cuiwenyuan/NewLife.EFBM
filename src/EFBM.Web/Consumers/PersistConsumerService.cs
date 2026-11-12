using System.Collections.Concurrent;
using EFBM.Core;
using NewLife.Log;

namespace EFBM.Web.Consumers;

/// <summary>落库组消费者：取消息 → 逐条编排落库 → 显式 <c>XACK</c>（FR-5、FR-12、FR-22）。</summary>
/// <remarks>
/// <b>不使用框架自动 Ack 的消费循环</b>（<c>TakeMessagesAsync</c> 的 <c>RetryAck()</c> 会把
/// 超过 MaxRetry 的消息静默丢弃）：成功路径在编排层全部置位后才 Ack；
/// 失败不 Ack，消息留在 PEL，下一轮 <c>TakeAsync</c> 先读 PEL 让其重投；
/// 进程内重试计数达到 <c>ConsumerMaxRetry</c> 后<b>先写死信表再 Ack</b>——绝不静默丢弃。
///
/// 循环异常（Redis 断连等）退避 5 秒后整轮重来，消费者进程常驻（IHostedService 由宿主守护）。
/// 纯日志模式（<c>RedisEnabled=false</c>）不启动本服务，改用 <c>--replay</c> 工具补录。
/// </remarks>
public sealed class PersistConsumerService : BackgroundService
{
    private readonly EfbmOptions _options;
    private readonly IEventQueue _queue;
    private readonly EventPersistService _persist;
    private readonly PersistConsumerState _state;
    private readonly String _consumerName;
    private readonly ConcurrentDictionary<String, Int32> _retries = new();

    /// <summary>构造落库消费者。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="queue">共享上报流。</param>
    /// <param name="persist">消费落库编排。</param>
    /// <param name="state">运行指标探针。</param>
    public PersistConsumerService(EfbmOptions options, IEventQueue queue, EventPersistService persist, PersistConsumerState state)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _consumerName = $"{Environment.MachineName}@{Environment.ProcessId}";
    }

    /// <summary>消费主循环。</summary>
    /// <param name="stoppingToken">停机令牌。</param>
    /// <returns>异步任务。</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RedisEnabled)
        {
            XTrace.WriteLine("落库消费者未启动：Redis 未启用（纯日志模式），数据在文本日志，可用 --replay 工具补录");
            return;
        }

        _state.Start();
        XTrace.WriteLine("落库消费者启动：group={0} consumer={1} batchSize={2} maxRetry={3}",
            _options.PersistGroup, _consumerName, _options.BatchSize, _options.ConsumerMaxRetry);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<QueueMessage> messages;
                try
                {
                    // 先读 PEL 再读新消息：未确认消息优先重投（TakeAsync 内部实现）
                    messages = await _queue.TakeAsync(_options.PersistGroup, _consumerName, _options.BatchSize, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Redis 断连等整轮失败：退避后重来，期间上报仍 200（走本地兜底）
                    _state.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
                    XTrace.WriteLine("消费者取消息失败，5 秒后重试: {0}", _state.LastError);
                    await DelayAsync(5000, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (messages.Count == 0)
                {
                    await DelayAsync(_options.BatchIntervalMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                _state.MarkTake(messages.Count);

                foreach (var message in messages)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await HandleOneAsync(message, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _state.Stop();
            XTrace.WriteLine("落库消费者停止");
        }
    }

    /// <summary>处理单条消息：成功 Ack；失败计重试；超限先入死信再 Ack。</summary>
    private async Task HandleOneAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _persist.PersistAsync(message.Message, message.StreamId, cancellationToken).ConfigureAwait(false);
            if (outcome != PersistOutcome.Failed)
            {
                // 处理完成（含重复 / 死信）才确认；Failed 永远走不到这里（异常上抛）
                await _queue.AckAsync(_options.PersistGroup, message.StreamId, cancellationToken).ConfigureAwait(false);
                // 仅当该消息此前进过重试（_retries 里有记录）才抵消重试计数，避免 retrying 被错误递减为负
                if (_retries.TryRemove(message.StreamId, out _)) _state.MarkRetryResolved();
                _state.MarkAck();
                _state.MarkPersisted(outcome);
            }
        }
        catch (Exception ex)
        {
            var count = _retries.AddOrUpdate(message.StreamId, 1, (_, old) => old + 1);
            var error = $"{ex.GetType().Name}: {ex.Message}";
            _state.MarkFailed($"streamId={message.StreamId} retry={count} {error}");

            if (count < _options.ConsumerMaxRetry)
            {
                // 不 Ack：留在 PEL，下一轮 Take 先读 PEL 重投
                XTrace.WriteLine("消费失败待重投 streamId={0} retry={1}/{2}: {3}",
                    message.StreamId, count, _options.ConsumerMaxRetry, error);
                return;
            }

            try
            {
                // 超限：先写死信表（含完整消息体，可人工重放），成功后才确认；绝不静默丢弃
                await _persist.DeadLetterAsync(message.Message, message.StreamId, count, error, cancellationToken).ConfigureAwait(false);
                await _queue.AckAsync(_options.PersistGroup, message.StreamId, cancellationToken).ConfigureAwait(false);
                if (_retries.TryRemove(message.StreamId, out _)) _state.MarkRetryResolved();
                _state.MarkAck();
                _state.MarkPersisted(PersistOutcome.DeadLettered);
                XTrace.WriteLine("高优告警：消息重试 {0} 次失败，已转入死信表 streamId={1} eventKey={2}",
                    count, message.StreamId, message.Message?.EventKey);
            }
            catch (Exception deadLetterEx)
            {
                // 死信写不进去：不确认，下一轮继续尝试（重试计数继续上涨但不再 Ack，直到死信成功）
                XTrace.WriteLine("死信写入失败，消息保持未确认 streamId={0}: {1}: {2}",
                    message.StreamId, deadLetterEx.GetType().Name, deadLetterEx.Message);
            }
        }
    }

    /// <summary>可取消的延迟；停机时立刻醒来。</summary>
    private static Task DelayAsync(Int32 milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(Math.Max(1, milliseconds), cancellationToken);
}
