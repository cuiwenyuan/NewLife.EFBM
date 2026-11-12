using EFBM.Core;
using EFBM.Web.Consumers;
using EFBM.Web.Consumers.Handlers;
using EFBM.Web.Services;

namespace EFBM.Web.Consumers;

/// <summary>P3 消费链路的 DI 注册：存储实现、事件处理器注册表、编排服务与常驻消费者。</summary>
public static class ConsumerServiceExtensions
{
    /// <summary>注册消费落库全部组件。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddEFBMConsumers(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        // 存储：XCode 实体实现，单例（无状态）
        services.AddSingleton<IRawEventStore, EventRawStore>();
        services.AddSingleton<IDeadLetterStore, DeadLetterStore>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

        // 事件处理器：新增事件只需在此追加一行（FR-26）
        services.AddSingleton<IEventHandler, OrderStartedHandler>();
        services.AddSingleton<IEventHandler, OrderProgressHandler>();
        services.AddSingleton<IEventHandler, OrderCompletedHandler>();
        services.AddSingleton<IEventHandler, DeviceStatusChangedHandler>();
        services.AddSingleton<IEventHandler, FaultOccurredHandler>();
        services.AddSingleton<IEventHandler, FaultClearedHandler>();
        services.AddSingleton<IEventHandler, UnknownEventHandler>();   // 必须有：兜底

        // 注册表在构造期校验重复 / 缺兜底，配置错误启动即失败
        services.AddSingleton<EventHandlerRegistry>();
        services.AddSingleton<EventPersistService>();

        // 消费者与指标
        services.AddSingleton<PersistConsumerState>();
        services.AddHostedService<PersistConsumerService>();

        return services;
    }
}
