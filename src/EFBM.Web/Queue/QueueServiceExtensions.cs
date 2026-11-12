using EFBM.Core;

namespace EFBM.Web.Queue;

/// <summary>上报流相关服务的依赖注入注册。</summary>
public static class QueueServiceExtensions
{
    /// <summary>注册共享上报流。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合，便于链式调用。</returns>
    /// <remarks>
    /// 使用<b>惰性构造</b>：注册时不发起网络 IO，首次 <c>XADD</c> / <c>XREADGROUP</c> 才连接 Redis
    /// 并创建落库消费组，因此没有 Redis 的开发环境也能启动；Redis 故障才会由接收层降级到本地兜底。
    /// P2 已接入上报端点；落库消费者（P3）与安全裁剪任务（P4）按阶段注册，此处不预注册空壳服务。
    /// </remarks>
    public static IServiceCollection AddEFBMQueue(this IServiceCollection services)
    {
        services.AddSingleton<IEventQueue>(sp => new RedisEventQueue(sp.GetRequiredService<EfbmOptions>()));
        return services;
    }
}
