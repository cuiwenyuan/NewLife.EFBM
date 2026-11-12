using System.Security.Claims;
using EFBM.Core;
using EFBM.Core.Security;

namespace EFBM.Web.Security;

/// <summary>把当前登录用户解析成后台三角色之一（FR-14）。</summary>
public interface IAdminRoleResolver
{
    /// <summary>解析当前请求用户的角色。</summary>
    /// <param name="user">当前用户主体；未登录时为 <c>null</c>。</param>
    /// <returns>角色；用户没有任何可识别角色时返回 <c>null</c>。</returns>
    AdminRole? Resolve(ClaimsPrincipal user);

    /// <summary>按用户名查 <c>EFBM:Admin:Users</c> 里的直接映射。</summary>
    /// <param name="name">登录名。</param>
    /// <returns>配置里点名时返回角色，否则 <c>null</c>。</returns>
    AdminRole? ResolveByName(String name);
}

/// <summary>按「角色名 / 角色标识 → 三角色」的别名表解析用户角色。</summary>
/// <remarks>
/// 取值来源是登录票据里的角色声明：<see cref="ClaimTypes.Role"/> 以及 Cube 常用的
/// <c>role</c> / <c>roles</c> / <c>Role</c> / <c>Roles</c> / <c>RoleIDs</c> 这几种写法。
/// 一个声明里出现多个值（逗号 / 分号分隔）时逐个匹配，命中多个角色取<b>权限最高</b>的一个——
/// 同时属于「运维组」和「管理员组」的人应当是管理员，否则会莫名少权限。
///
/// 匹配不上（例如只配了 Cube 的数字角色 ID）时返回 <c>null</c>，接口按 403 拒绝。
/// 宁可拒绝也不能猜：猜错等于把删数据的权限给了不相干的人。现场用数字 ID 时，
/// 在 <c>EFBM:Admin:RoleAliases</c> 里把 <c>"3": "viewer"</c> 这类映射显式写出来即可。
///
/// 唯一的例外是 <c>EFBM:Admin:Users</c> 里点名的账号：Cube 的初始超管 <c>RoleIDs</c> 为空，
/// 登录票据里没有任何角色声明，只能按用户名兜底，否则「刚装好，超管进不去后台」。
/// </remarks>
public sealed class AdminRoleResolver : IAdminRoleResolver
{
    /// <summary>内置别名：覆盖常见的中英文角色名写法，配置里的别名优先。</summary>
    private static readonly Dictionary<String, AdminRole> _builtin = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = AdminRole.Admin,
        ["administrator"] = AdminRole.Admin,
        ["sysadmin"] = AdminRole.Admin,
        ["root"] = AdminRole.Admin,
        ["管理员"] = AdminRole.Admin,
        ["系统管理员"] = AdminRole.Admin,
        ["超级管理员"] = AdminRole.Admin,

        ["operator"] = AdminRole.Operator,
        ["ops"] = AdminRole.Operator,
        ["maintainer"] = AdminRole.Operator,
        ["运维"] = AdminRole.Operator,
        ["运维人员"] = AdminRole.Operator,
        ["操作员"] = AdminRole.Operator,
        ["工程师"] = AdminRole.Operator,
        // Cube 自带的「高级用户」是业务管理人员，对应到运维（能重放、能导出，但不能删数据）
        ["高级用户"] = AdminRole.Operator,

        ["viewer"] = AdminRole.Viewer,
        ["readonly"] = AdminRole.Viewer,
        ["read-only"] = AdminRole.Viewer,
        ["read only"] = AdminRole.Viewer,
        ["guest"] = AdminRole.Viewer,
        ["只读"] = AdminRole.Viewer,
        ["只读用户"] = AdminRole.Viewer,
        ["访客"] = AdminRole.Viewer,
        ["观察者"] = AdminRole.Viewer,
    };

    /// <summary>需要检查的声明类型（Cube 与不同宿主写法不一，全部认一遍）。</summary>
    private static readonly String[] _claimTypes =
    [
        ClaimTypes.Role,
        "role",
        "roles",
        "Role",
        "Roles",
        "RoleIDs",
        "RoleIds",
        "roleNames",
        "RoleNames",
    ];

    private readonly Dictionary<String, AdminRole> _aliases;
    private readonly Dictionary<String, AdminRole> _users;

    /// <summary>按配置构建解析器。</summary>
    /// <param name="options">应用配置，取 <see cref="AdminOptions.RoleAliases"/> 与 <see cref="AdminOptions.Users"/>；为空时只用内置别名。</param>
    public AdminRoleResolver(EfbmOptions options = null)
    {
        _aliases = new Dictionary<String, AdminRole>(_builtin, StringComparer.OrdinalIgnoreCase);
        _users = new Dictionary<String, AdminRole>(StringComparer.OrdinalIgnoreCase);

        if (options?.Admin == null) return;

        foreach (var (key, value) in options.Admin.RoleAliases)
        {
            var role = AdminRoleInfo.Parse(value);
            if (role != null) _aliases[key.Trim()] = role.Value;
        }

        foreach (var (key, value) in options.Admin.Users)
        {
            var role = AdminRoleInfo.Parse(value);
            if (role != null) _users[key.Trim()] = role.Value;
        }
    }

    /// <inheritdoc />
    public AdminRole? ResolveByName(String name)
        => !String.IsNullOrWhiteSpace(name) && _users.TryGetValue(name.Trim(), out var role) ? role : null;

    /// <inheritdoc />
    public AdminRole? Resolve(ClaimsPrincipal user)
    {
        if (user == null) return null;

        // 用户名映射优先：初始超管的 RoleIDs 是空的，票据里根本没有角色声明
        var name = user.Identity?.Name;
        if (String.IsNullOrWhiteSpace(name)) name = user.FindFirst(ClaimTypes.Name)?.Value;
        if (ResolveByName(name) is { } byName) return byName;

        AdminRole? best = null;
        foreach (var identity in user.Identities)
        {
            foreach (var claim in identity.Claims)
            {
                if (!_claimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase)) continue;

                var role = Match(claim.Value);
                if (role != null && (best == null || role.Value > best.Value)) best = role;
            }
        }

        return best;
    }

    /// <summary>把一段声明值（可能逗号 / 分号分隔多个角色）解析成最高角色。</summary>
    /// <param name="value">声明值。</param>
    /// <returns>最高角色；一个都匹配不上时返回 <c>null</c>。</returns>
    public AdminRole? Match(String value)
    {
        if (String.IsNullOrWhiteSpace(value)) return null;

        AdminRole? best = null;
        foreach (var part in value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!_aliases.TryGetValue(part, out var role)) continue;
            if (best == null || role > best.Value) best = role;
        }

        return best;
    }
}
