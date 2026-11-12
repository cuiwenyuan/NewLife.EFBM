namespace EFBM.Core.Security;

/// <summary>后台三角色鉴权的配置（<c>EFBM:Admin</c>）。</summary>
/// <remarks>
/// Cube 的角色是运维在后台自己建的，名字各现场不同（「设备管理员」「运维一组」…）。
/// 这里用别名表把现场角色名映射到本系统的三个固定角色，代码里不出现现场业务名。
/// 内置别名（见 <see cref="AdminRoleResolver"/>）覆盖常见写法，配置只写差异即可。
///
/// <see cref="Users"/> 是兜底：Cube 用户的 <c>RoleIDs</c> 为空（例如刚装好、还没分配角色的超管）
/// 时，登录票据里不会有角色声明，此时按用户名直接定角色，保证「装完就能用」。
/// </remarks>
public class AdminOptions
{
    /// <summary>角色别名：键为 Cube 里的角色名（或角色标识），值为本系统角色（<c>admin</c> / <c>operator</c> / <c>viewer</c>，也可写中文）。</summary>
    /// <remarks>
    /// 例：<c>"设备管理员": "admin"</c>、<c>"车间主任": "viewer"</c>。配置项覆盖同名内置别名。
    /// </remarks>
    public Dictionary<String, String> RoleAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按用户名直接定角色，优先级高于角色声明（用于没有分配角色的账号，如初始超管）。</summary>
    /// <remarks>
    /// 例：<c>"admin": "admin"</c>。这里写的是<b>例外</b>而不是常态：常规账号请用 Cube 角色 + <see cref="RoleAliases"/>，
    /// 用户名写死太多会让「谁有什么权限」散落在配置文件里。
    /// </remarks>
    public Dictionary<String, String> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>校验两张映射表：键非空、值必须能解析成三个角色之一。</summary>
    /// <exception cref="InvalidOperationException">映射值非法时抛出。</exception>
    public void ValidateOrThrow()
    {
        RoleAliases ??= new(StringComparer.OrdinalIgnoreCase);
        Users ??= new(StringComparer.OrdinalIgnoreCase);

        Validate(RoleAliases, "EFBM:Admin:RoleAliases", "角色别名");
        Validate(Users, "EFBM:Admin:Users", "用户名");
    }

    /// <summary>逐项校验映射表。</summary>
    /// <param name="map">映射表。</param>
    /// <param name="section">配置节名，写进异常信息便于定位。</param>
    /// <param name="label">中文名称，写进异常信息。</param>
    private static void Validate(Dictionary<String, String> map, String section, String label)
    {
        foreach (var (key, value) in map)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"{label}不能为空（{section}）");
            if (AdminRoleInfo.Parse(value) == null)
                throw new InvalidOperationException($"{label}「{key}」的值非法（{value}），仅支持 admin / operator / viewer 或 管理员 / 运维 / 只读（{section}）");
        }
    }
}
