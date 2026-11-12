namespace EFBM.Core.Security;

/// <summary>后台三角色（FR-14）：只读 / 运维 / 管理员，按权限从小到大排列。</summary>
/// <remarks>
/// 数值大小即权限高低：<see cref="AdminRole.Admin"/> 拥有 <see cref="AdminRole.Operator"/> 的全部权限，
/// <see cref="AdminRole.Operator"/> 拥有 <see cref="AdminRole.Viewer"/> 的全部权限。判定一律走
/// <see cref="AdminPermissionMatrix"/>，不要在业务代码里直接比较数值。
/// </remarks>
public enum AdminRole
{
    /// <summary>只读：看报文、看死信，不改任何数据。</summary>
    Viewer = 1,

    /// <summary>运维：在只读之上增加标记、重放、导出（排障必需的写操作）。</summary>
    Operator = 2,

    /// <summary>管理员：在运维之上增加清理（唯一会删数据的操作）。</summary>
    Admin = 3,
}

/// <summary>后台可授权的操作，一个枚举值对应一组接口动作。</summary>
/// <remarks>
/// 新增操作时必须在 <see cref="AdminPermissionMatrix"/> 里指明最低角色，并有单测兜底
/// （枚举未入矩阵会直接失败，避免「加了接口却没人管权限」）。
/// </remarks>
public enum AdminOperation
{
    /// <summary>查询原始报文列表。</summary>
    QueryEvents,

    /// <summary>查看单条原始报文原文与扩展字段。</summary>
    ViewEventDetail,

    /// <summary>手动标记处理状态与备注。</summary>
    MarkEvent,

    /// <summary>单条 / 按范围重新入队重放。</summary>
    ReplayEvent,

    /// <summary>按时间范围导出 CSV。</summary>
    ExportEvents,

    /// <summary>下载已导出的 CSV 文件。</summary>
    DownloadExport,

    /// <summary>凭导出凭证清理原始报文（唯一会删数据的操作）。</summary>
    CleanupEvents,

    /// <summary>查询死信列表。</summary>
    QueryDeadLetters,

    /// <summary>查看死信详情（含消息体）。</summary>
    ViewDeadLetterDetail,

    /// <summary>死信手动重放。</summary>
    ReplayDeadLetter,

    /// <summary>查询后台操作审计日志。</summary>
    QueryAudits,
}

/// <summary>角色与操作的展示名、解析工具。</summary>
public static class AdminRoleInfo
{
    /// <summary>全部角色，按权限升序。</summary>
    public static readonly AdminRole[] All = [AdminRole.Viewer, AdminRole.Operator, AdminRole.Admin];

    /// <summary>取角色的线上标识（小写英文，用于接口与配置）。</summary>
    /// <param name="role">角色。</param>
    /// <returns><c>viewer</c> / <c>operator</c> / <c>admin</c>。</returns>
    public static String Name(AdminRole role) => role switch
    {
        AdminRole.Admin => "admin",
        AdminRole.Operator => "operator",
        AdminRole.Viewer => "viewer",
        _ => "unknown",
    };

    /// <summary>取角色的中文名（用于提示与页面）。</summary>
    /// <param name="role">角色。</param>
    /// <returns>管理员 / 运维 / 只读。</returns>
    public static String DisplayName(AdminRole role) => role switch
    {
        AdminRole.Admin => "管理员",
        AdminRole.Operator => "运维",
        AdminRole.Viewer => "只读",
        _ => "未知角色",
    };

    /// <summary>把线上标识或中文名解析成角色，忽略大小写与首尾空白。</summary>
    /// <param name="value">角色标识，如 <c>admin</c> / <c>管理员</c>。</param>
    /// <returns>解析成功返回角色，否则返回 <c>null</c>。</returns>
    public static AdminRole? Parse(String value)
    {
        if (String.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        foreach (var role in All)
        {
            if (String.Equals(Name(role), text, StringComparison.OrdinalIgnoreCase)) return role;
            if (String.Equals(DisplayName(role), text, StringComparison.Ordinal)) return role;
        }

        return null;
    }
}

/// <summary>操作的展示名。</summary>
public static class AdminOperationInfo
{
    /// <summary>取操作的中文名（写进 403 提示，让运维知道被拦的是哪件事）。</summary>
    /// <param name="operation">操作。</param>
    /// <returns>中文名。</returns>
    public static String DisplayName(AdminOperation operation) => operation switch
    {
        AdminOperation.QueryEvents => "查询原始报文",
        AdminOperation.ViewEventDetail => "查看报文原文",
        AdminOperation.MarkEvent => "标记处理状态",
        AdminOperation.ReplayEvent => "重放原始报文",
        AdminOperation.ExportEvents => "导出原始报文",
        AdminOperation.DownloadExport => "下载导出文件",
        AdminOperation.CleanupEvents => "清理原始报文",
        AdminOperation.QueryDeadLetters => "查询死信",
        AdminOperation.ViewDeadLetterDetail => "查看死信详情",
        AdminOperation.ReplayDeadLetter => "重放死信",
        AdminOperation.QueryAudits => "查询审计日志",
        _ => operation.ToString(),
    };
}
