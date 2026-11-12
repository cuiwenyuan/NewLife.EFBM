namespace EFBM.Core.Security;

/// <summary>三角色 × 全部操作 的权限矩阵（FR-14），是后台鉴权的<b>唯一事实来源</b>。</summary>
/// <remarks>
/// 为什么不用 Cube 的 <c>[EntityAuthorize]</c>：那套特性绑定菜单与实体控制器，判的是
/// 「菜单权限位」（Detail / Insert / Update / Delete），表达不了「导出可以、清理不行」这类
/// 同一实体上的动作差异。后台接口是手写 REST，动作粒度自己说了算，因此矩阵单独定义、
/// 纯函数判定（<see cref="Allows"/>），既能在特性里拦截，也能被页面直接读出来做菜单收敛。
///
/// 角色是<b>包含关系</b>：<see cref="AdminRole.Admin"/> ⊃ <see cref="AdminRole.Operator"/> ⊃
/// <see cref="AdminRole.Viewer"/>，矩阵按「该角色可做的全部动作」平铺，不做运行时求和——
/// 平铺才能在页面上一次读全，也便于审计「谁多了哪项」。
/// </remarks>
public static class AdminPermissionMatrix
{
    /// <summary>只读：查询与查看，不含任何写操作。</summary>
    private static readonly AdminOperation[] _viewer =
    [
        AdminOperation.QueryEvents,
        AdminOperation.ViewEventDetail,
        AdminOperation.QueryDeadLetters,
        AdminOperation.ViewDeadLetterDetail,
    ];

    /// <summary>运维：只读 + 标记 / 重放 / 导出（排障必需，均不改原始报文内容）。</summary>
    private static readonly AdminOperation[] _operator =
    [
        AdminOperation.QueryEvents,
        AdminOperation.ViewEventDetail,
        AdminOperation.MarkEvent,
        AdminOperation.ReplayEvent,
        AdminOperation.ExportEvents,
        AdminOperation.DownloadExport,
        AdminOperation.QueryDeadLetters,
        AdminOperation.ViewDeadLetterDetail,
        AdminOperation.ReplayDeadLetter,
    ];

    /// <summary>管理员：运维 + 清理（唯一会删数据的操作）。</summary>
    private static readonly AdminOperation[] _admin =
    [
        AdminOperation.QueryEvents,
        AdminOperation.ViewEventDetail,
        AdminOperation.MarkEvent,
        AdminOperation.ReplayEvent,
        AdminOperation.ExportEvents,
        AdminOperation.DownloadExport,
        AdminOperation.CleanupEvents,
        AdminOperation.QueryDeadLetters,
        AdminOperation.ViewDeadLetterDetail,
        AdminOperation.ReplayDeadLetter,
    ];

    private static readonly Dictionary<AdminRole, AdminOperation[]> _matrix = new()
    {
        [AdminRole.Viewer] = _viewer,
        [AdminRole.Operator] = _operator,
        [AdminRole.Admin] = _admin,
    };

    /// <summary>判断某角色能否执行某操作。</summary>
    /// <param name="role">当前角色。</param>
    /// <param name="operation">待判定的操作。</param>
    /// <returns>允许返回 <c>true</c>。</returns>
    public static Boolean Allows(AdminRole role, AdminOperation operation)
        => _matrix.TryGetValue(role, out var ops) && Array.IndexOf(ops, operation) >= 0;

    /// <summary>取某角色可执行的全部操作（顺序固定，便于前端与文档一致）。</summary>
    /// <param name="role">角色。</param>
    /// <returns>操作数组；未知角色返回空数组。</returns>
    public static AdminOperation[] AllowedFor(AdminRole role)
        => _matrix.TryGetValue(role, out var ops) ? [.. ops] : [];

    /// <summary>取某操作的最低角色要求（权限最小的那个可执行角色）。</summary>
    /// <param name="operation">操作。</param>
    /// <returns>最低角色；任何角色都不能执行时返回 <c>null</c>。</returns>
    public static AdminRole? MinimumRole(AdminOperation operation)
    {
        foreach (var role in AdminRoleInfo.All)
        {
            if (Allows(role, operation)) return role;
        }

        return null;
    }

    /// <summary>导出矩阵快照：角色标识 → 操作标识数组，供页面渲染菜单与权限说明。</summary>
    /// <returns>三角色的完整矩阵。</returns>
    public static Dictionary<String, String[]> Describe()
    {
        var map = new Dictionary<String, String[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in AdminRoleInfo.All)
        {
            map[AdminRoleInfo.Name(role)] = [.. Array.ConvertAll(_matrix[role], op => op.ToString())];
        }

        return map;
    }
}
