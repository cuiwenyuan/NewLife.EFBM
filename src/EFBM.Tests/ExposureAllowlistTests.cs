using EFBM.Web.Security;

namespace EFBM.Tests;

/// <summary>外网暴露面白名单（AUTH-4b）：只放行上报、查询、健康检查、后台与静态资源。</summary>
/// <remarks>
/// 白名单写错有两种后果：漏掉后台静态资源导致页面样式全丢，或放得过宽等于没做收敛。
/// 这里把「必须放行」和「必须拦下」的典型路径各钉一遍。
/// </remarks>
public sealed class ExposureAllowlistTests
{
    [Theory(DisplayName = "暴露面_放行必要路径")]
    [InlineData("/api/events")]
    [InlineData("/api/query/devices")]
    [InlineData("/api/query/stream")]
    [InlineData("/health")]
    [InlineData("/Admin")]
    [InlineData("/Admin/DeviceEventRaw")]
    [InlineData("/CubeHome/Index")]
    [InlineData("/Content/css/site.css")]
    [InlineData("/Scripts/jquery.js")]
    [InlineData("/Images/logo.png")]
    [InlineData("/favicon.ico")]
    [InlineData("/")]
    [InlineData("")]
    public void Allowed(String path) => Assert.True(ExposureAllowlistMiddleware.IsAllowed(path), $"{path} 应放行");

    [Theory(DisplayName = "暴露面_拦下非白名单路径")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/api/internal/debug")]
    [InlineData("/Admin/../web.config")]
    [InlineData("/metrics")]
    [InlineData("/elmah.axd")]
    public void Denied(String path) => Assert.False(ExposureAllowlistMiddleware.IsAllowed(path), $"{path} 应被拦下");

    [Fact(DisplayName = "暴露面_清单可读出来便于启动日志核对")]
    public void Describe()
    {
        var lines = ExposureAllowlistMiddleware.Describe();

        Assert.Contains("/api/events", lines);
        Assert.Contains("/Admin", lines);
        Assert.Contains(lines, e => e.Contains(".js"));
    }
}
