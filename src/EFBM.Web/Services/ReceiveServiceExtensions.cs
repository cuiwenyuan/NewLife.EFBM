using EFBM.Core;
using EFBM.Web.Logging;
using EFBM.Web.Services;

namespace EFBM.Web.Services;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>P2 接收链路的依赖注入注册。</summary>
/// <remarks>
/// <see cref="EventIngestService"/> 注册为惰性 Singleton：只有首个 <c>POST /api/events</c> 到达时才解析，
/// 启动阶段不会连接 Redis 或业务库。<see cref="FallbackRequeueService"/> 只在有待回灌记录时才解析
/// <see cref="IEventQueue"/>，因此无 Redis / MySQL 的开发机同样可以启动宿主进行调试。
///
/// <see cref="FileRequestLog"/> 同时以 <see cref="IRequestLog"/> 与 <see cref="IRequestLogProbe"/> 暴露，
/// 保证「写日志」和「/health 看日志状态」用的是同一个实例与同一把锁。
/// </remarks>
public static class ReceiveServiceExtensions
{
    /// <summary>注册解析、鉴权、入流编排、请求日志与兜底回灌服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合，便于链式调用。</returns>
    public static IServiceCollection AddEFBMReceive(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<FileRequestLog>();
        services.AddSingleton<IRequestLog>(sp => sp.GetRequiredService<FileRequestLog>());
        services.AddSingleton<IRequestLogProbe>(sp => sp.GetRequiredService<FileRequestLog>());

        services.AddSingleton<IDeviceCredentialStore, DeviceCredentialStore>();
        services.AddSingleton<ILocalFallbackStore, LocalFallbackStore>();
        services.AddSingleton<EventIngestService>();
        services.AddHostedService<FallbackRequeueService>();

        return services;
    }
}
