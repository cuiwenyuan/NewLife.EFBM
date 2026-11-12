namespace EFBM.Core;

/// <summary><see cref="IEventHandler"/> 注册表：按事件名分发，未知事件走兜底（FR-26）。</summary>
/// <remarks>
/// 构造期校验重复注册与缺失兜底，错误配置启动即失败（与启动自检同一原则）。
/// 新增事件只需实现 <see cref="IEventHandler"/> 并注册到 DI，接入层零改动。
/// </remarks>
public sealed class EventHandlerRegistry
{
    private readonly Dictionary<String, IEventHandler> _handlers = new(StringComparer.Ordinal);
    private readonly IEventHandler _fallback;

    /// <summary>用全部已注册 handler 构建注册表。</summary>
    /// <param name="handlers">DI 中注册的全部事件处理器。</param>
    /// <exception cref="InvalidOperationException">事件名重复注册或缺少兜底 handler。</exception>
    public EventHandlerRegistry(IEnumerable<IEventHandler> handlers)
    {
        if (handlers == null) throw new ArgumentNullException(nameof(handlers));

        foreach (var handler in handlers)
        {
            if (handler == null) continue;

            var eventName = handler.Event;
            if (String.IsNullOrEmpty(eventName))
                throw new InvalidOperationException("事件处理器的 Event 不能为空");

            if (_handlers.TryGetValue(eventName, out var existing))
                throw new InvalidOperationException($"事件 {eventName} 被重复注册: {existing.GetType().Name} 与 {handler.GetType().Name}");

            _handlers[eventName] = handler;
            if (String.Equals(eventName, IEventHandler.UnknownEventName, StringComparison.Ordinal))
                _fallback = handler;
        }

        if (_fallback == null)
            throw new InvalidOperationException($"缺少未知事件兜底 handler（Event = \"{IEventHandler.UnknownEventName}\"）");
    }

    /// <summary>已注册事件数（不含兜底键）。</summary>
    public Int32 Count => _handlers.Count;

    /// <summary>按事件名取处理器；未知事件返回兜底 handler。</summary>
    /// <param name="eventName">报文中的事件名。</param>
    /// <returns>事件处理器，永不为 <c>null</c>。</returns>
    public IEventHandler Resolve(String eventName)
    {
        if (!String.IsNullOrEmpty(eventName) && _handlers.TryGetValue(eventName, out var handler))
            return handler;

        return _fallback;
    }

    /// <summary>分发一条已解析的上报；handler 抛异常即整体失败，消息不确认、留待重投。</summary>
    /// <param name="envelope">重解析后的信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task DispatchAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));

        return Resolve(envelope.Event).HandleAsync(envelope, cancellationToken);
    }
}
