namespace EFBM.Core;

/// <summary>认证模式常量，对应 <c>EFBM:AuthMode</c> 配置项（需求说明 §2、FR-2）。</summary>
/// <remarks>
/// 三种模式全部实现，按部署环境配置启用：内网可用 <see cref="Anonymous"/>，外网禁用匿名。
/// 凭据一律按设备发放，与 <c>EFBM.Data.Entities.AppCredential</c> 一对一。
/// </remarks>
public static class AuthModes
{
    /// <summary>匿名上报，不校验凭据；仅允许内网使用。</summary>
    public const String Anonymous = "Anonymous";

    /// <summary>HTTP Basic 认证；用户名取 AppKey、密码取 AppSecret。</summary>
    public const String Basic = "Basic";

    /// <summary>默认模式：<c>X-App-Key</c> / <c>X-App-Secret</c> 两个请求头。</summary>
    public const String AppKeyAppSecret = "AppKeyAppSecret";

    /// <summary>全部受支持的认证模式。</summary>
    public static readonly String[] All = [Anonymous, Basic, AppKeyAppSecret];

    /// <summary>判断配置值是否为受支持的认证模式。</summary>
    /// <param name="mode">配置值。</param>
    /// <returns>受支持返回 <c>true</c>。</returns>
    public static Boolean IsKnown(String mode) =>
        !String.IsNullOrEmpty(mode) && All.Any(e => String.Equals(e, mode, StringComparison.OrdinalIgnoreCase));

    /// <summary>把配置值归一化为标准模式名。</summary>
    /// <param name="mode">配置值；为空时回落到 <see cref="AppKeyAppSecret"/>。</param>
    /// <returns>标准模式名；未识别的值原样返回，由启动自检拦截。</returns>
    public static String Normalize(String mode)
    {
        if (String.IsNullOrWhiteSpace(mode)) return AppKeyAppSecret;
        return All.FirstOrDefault(e => String.Equals(e, mode, StringComparison.OrdinalIgnoreCase)) ?? mode;
    }
}
