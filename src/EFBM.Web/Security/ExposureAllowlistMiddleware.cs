using EFBM.Core;
using NewLife.Log;

namespace EFBM.Web.Security;

/// <summary>外网暴露面收敛：只放行上报端点、查询接口、健康检查与后台入口（AUTH-4b）。</summary>
/// <remarks>
/// 为什么需要它：Cube 宿主除了本系统的端点，还带了一整套后台与框架页面（Swagger、
/// 各类管理页、诊断端点）。内网无所谓，外网每多一个入口就多一份被扫的风险。
/// 这里用<b>白名单</b>而不是黑名单——黑名单永远会漏掉下一个框架版本新增的路径。
///
/// 命中白名单之外一律返回 <c>404</c> 而不是 403：403 等于告诉扫描器「这里确实有东西，只是不让你看」。
/// 请求根本不进入后续管道，也就不消耗 MVC 的解析成本。
///
/// 只在 <c>Deployment=Internet</c> 且 <c>ExposureAllowlistEnabled=true</c> 时启用；
/// 内网部署保持原样，避免联调时被自己的白名单挡在门外。
/// </remarks>
public sealed class ExposureAllowlistMiddleware
{
    /// <summary>固定放行的路径前缀。</summary>
    private static readonly String[] AllowedPrefixes =
    [
        "/api/events",
        "/api/query",
        "/health",
        "/Admin",
        "/CubeHome",
    ];

    /// <summary>静态资源前缀：后台页面要靠它们才能正常显示。</summary>
    private static readonly String[] StaticPrefixes =
    [
        "/Content",
        "/Scripts",
        "/Script",
        "/Images",
        "/fonts",
        "/assets",
    ];

    /// <summary>静态资源扩展名，命中即放行（兼容主题自带的零散资源路径）。</summary>
    private static readonly String[] StaticExtensions =
    [
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
    ];

    private readonly RequestDelegate _next;

    /// <summary>构造中间件。</summary>
    /// <param name="next">后续管道。</param>
    public ExposureAllowlistMiddleware(RequestDelegate next) => _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>执行拦截。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>异步任务。</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (IsAllowed(path)) return _next(context);

        XTrace.WriteLine("外网暴露面拦截：{0} {1} 来自 {2}", context.Request.Method, path,
            context.Connection.RemoteIpAddress);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    /// <summary>判断路径是否放行。</summary>
    /// <param name="path">请求路径。</param>
    /// <returns>放行返回 <c>true</c>。</returns>
    public static Boolean IsAllowed(String path)
    {
        if (String.IsNullOrEmpty(path) || path == "/") return true;

        // 带 .. 的路径一律拒绝：前缀匹配会被 /Admin/../xxx 绕过
        if (path.Contains("..", StringComparison.Ordinal)) return false;

        foreach (var prefix in AllowedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        foreach (var prefix in StaticPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        var extension = Path.GetExtension(path);
        return extension.Length > 0 && Array.IndexOf(StaticExtensions, extension.ToLowerInvariant()) >= 0;
    }

    /// <summary>把白名单清单暴露给启动日志，便于现场核对「到底放行了什么」。</summary>
    /// <returns>放行前缀清单。</returns>
    public static String[] Describe() => [.. AllowedPrefixes, .. StaticPrefixes, "静态资源扩展名 " + String.Join(",", StaticExtensions)];
}
