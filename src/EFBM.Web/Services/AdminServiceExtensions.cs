namespace EFBM.Web.Services;

/// <summary>P4 后台的 DI 注册：只读检索与重放服务。</summary>
public static class AdminServiceExtensions
{
    /// <summary>注册后台管理接口依赖的检索 / 重放服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddEFBMAdmin(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        // 检索只走 XCode 实体、无状态；重放持有队列与应用配置；导出持有进程内凭证表，均按单例注册
        services.AddSingleton<EventQueryService>();
        services.AddSingleton<EventReplayService>();
        services.AddSingleton<EventExportService>();

        return services;
    }
}
