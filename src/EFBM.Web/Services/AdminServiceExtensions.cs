namespace EFBM.Web.Services;

/// <summary>后台服务的 DI 注册：检索、重放与导出 / 清理。</summary>
public static class AdminServiceExtensions
{
    /// <summary>注册后台页面依赖的检索 / 重放 / 导出服务。</summary>
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
