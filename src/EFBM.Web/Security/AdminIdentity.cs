using EFBM.Core.Security;
using Microsoft.AspNetCore.Http;
using NewLife.Cube;

namespace EFBM.Web.Security;

/// <summary>一次请求内的后台身份：登录名 + 已解析出的角色。</summary>
public sealed class AdminIdentity
{
    /// <summary>登录名；未登录为 <c>null</c>。</summary>
    public String Name { get; init; }

    /// <summary>三角色之一；已登录但没匹配到角色为 <c>null</c>。</summary>
    public AdminRole? Role { get; init; }

    /// <summary>是否已登录（只代表身份已确认，不代表有后台权限）。</summary>
    public Boolean IsAuthenticated => !String.IsNullOrWhiteSpace(Name);

    /// <summary>是否允许执行某操作（未登录或无角色一律不允许）。</summary>
    /// <param name="operation">操作。</param>
    /// <returns>允许返回 <c>true</c>。</returns>
    public Boolean Allow(AdminOperation operation) => Role != null && AdminPermissionMatrix.Allows(Role.Value, operation);
}

/// <summary>取当前请求的后台身份。</summary>
public interface IAdminIdentityAccessor
{
    /// <summary>解析当前请求的身份，未登录返回 <see cref="AdminIdentity.IsAuthenticated"/> 为 <c>false</c> 的实例。</summary>
    /// <returns>身份。</returns>
    AdminIdentity Get();
}

/// <summary>从 Cube 的登录态解析后台身份（FR-14）。</summary>
/// <remarks>
/// <b>为什么不用 <c>[Authorize]</c></b>：Cube 在 <c>UseCube</c> 内部才把 Cookie 里的登录用户还原到当前上下文，
/// 而 <c>UseAuthorization</c> 必须早于 <c>UseCube</c>（否则 Cube 的 <c>UseEndpoints</c> 会把端点映射抢走，
/// 见决策 D-30）。两者叠加的结果是——授权中间件看到的永远是未认证请求，登录了也 401。
/// 因此后台接口的登录判定挪到「动作过滤器」阶段（此时 Cube 的中间件已经跑完），
/// 与权限判定放在同一处，401 / 403 一起返回统一信封。
///
/// 用户名优先取 Cube 当前用户，其次取 ASP.NET 主体（若宿主换了认证方式仍然可用）。
/// </remarks>
public sealed class CubeAdminIdentityAccessor : IAdminIdentityAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly IAdminRoleResolver _roles;

    /// <summary>构造访问器。</summary>
    /// <param name="http">HTTP 上下文访问器。</param>
    /// <param name="roles">角色解析器。</param>
    public CubeAdminIdentityAccessor(IHttpContextAccessor http, IAdminRoleResolver roles)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    /// <inheritdoc />
    public AdminIdentity Get()
    {
        var context = _http.HttpContext;
        if (context == null) return new AdminIdentity();

        var name = ResolveName(context);
        if (String.IsNullOrWhiteSpace(name)) return new AdminIdentity();

        // 角色：先按用户名映射（Cube 初始超管没有角色），再按票据里的角色声明
        var role = _roles.ResolveByName(name) ?? _roles.Resolve(context.User);

        return new AdminIdentity { Name = name, Role = role };
    }

    /// <summary>取当前登录用户名：Cube 当前用户 → ASP.NET 主体 → 空。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>登录名或 <c>null</c>。</returns>
    private static String ResolveName(HttpContext context)
    {
        // Cube 的用户体系按请求保存「当前用户」，这里取它而不是看 HttpContext.User——
        // 后者在 Cube 宿主里由 Cube 自己的中间件设置，与 ASP.NET 的授权中间件并不同步
        try
        {
            var provider = context.RequestServices?.GetService(typeof(XCode.Membership.IManageProvider)) as XCode.Membership.IManageProvider;
            if (!String.IsNullOrWhiteSpace(provider?.Current?.Name)) return provider.Current.Name;
        }
        catch (InvalidOperationException)
        {
            // Cube 未注册用户体系（例如测试宿主）时忽略，退回 ASP.NET 主体
        }

        return context.User?.Identity?.Name;
    }
}
