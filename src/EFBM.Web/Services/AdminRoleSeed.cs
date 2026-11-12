using NewLife.Log;
using XCode;
using XCode.Membership;

namespace EFBM.Web.Services;

/// <summary>按魔方的角色模型初始化「运维」「只读」两个内置角色（FR-14）。</summary>
/// <remarks>
/// 魔方的权限模型是「菜单 × 权限位」：菜单在 <c>Menu</c> 表、每个菜单声明自己有哪些权限项
/// （查看 / 新增 / 修改 / 删除），角色在 <c>Role.Permission</c> 里按 <c>菜单ID#权限位</c> 授权。
/// 这里只做一件事——把两个内置角色的授权补齐，权限判定仍由魔方的 <c>EntityAuthorize</c> 完成：
/// <list type="bullet">
/// <item>运维：查看 + 修改（<c>1|4</c>）—— 可以标记、重放、导出，不能清理</item>
/// <item>只读：仅查看（<c>1</c>）</item>
/// <item>管理员：系统角色，魔方启动时已自动授予全部菜单（含后续新增），无需在此处理</item>
/// </list>
/// 每次启动重算，新增的菜单会自动纳入两个角色的授权范围；现场在「角色」页手工调整后，
/// 下次启动会被覆盖，若需要保留现场配置请把 <c>EFBM:SeedAdminRoles</c> 设为 <c>false</c>。
/// </remarks>
public static class AdminRoleSeed
{
    /// <summary>运维角色名。</summary>
    public const String OperatorRole = "运维";

    /// <summary>只读角色名。</summary>
    public const String ViewerRole = "只读";

    /// <summary>本项目在后台注册的控制器名（与菜单的 <c>Name</c> 一致）。</summary>
    private static readonly String[] ManagedControllers =
    [
        "DeviceEventRaw",
        "DeadLetter",
        "StatusDict",
        "Device",
        "AppCredential",
        "ProductionRecord",
        "WorkOrder",
        "FaultRecord",
        "DeviceStatusLog",
        "EventMaintenance",
        "FaultDisposal",
        "QueueMonitor",
        "FieldEvolution",
        "ConsumerGroup",
    ];

    /// <summary>补齐两个内置角色的授权；已存在则更新权限串。</summary>
    /// <returns>写入的角色数（新增 + 更新）。</returns>
    public static Int32 Seed()
    {
        var menus = Menu.FindAll()
            .Where(e => ManagedControllers.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e.ID)
            .ToList();

        if (menus.Count == 0)
        {
            XTrace.WriteLine("后台菜单尚未生成，跳过内置角色初始化");
            return 0;
        }

        var count = 0;
        count += Upsert(OperatorRole, BuildPermission(menus, 1 | 4));
        count += Upsert(ViewerRole, BuildPermission(menus, 1));

        XTrace.WriteLine($"后台内置角色已就绪：{OperatorRole}（查看+修改）、{ViewerRole}（仅查看），覆盖 {menus.Count} 个菜单");
        return count;
    }

    /// <summary>按菜单列表拼权限串：<c>菜单ID#权限位</c>，用逗号分隔。</summary>
    /// <param name="menus">菜单列表。</param>
    /// <param name="flags">权限位（1 查看 / 2 新增 / 4 修改 / 8 删除，可组合）。</param>
    /// <returns>魔方格式的权限串。</returns>
    public static String BuildPermission(IEnumerable<Menu> menus, Int32 flags)
        => String.Join(",", menus.Select(e => $"{e.ID}#{flags}"));

    /// <summary>创建或更新角色。</summary>
    /// <param name="name">角色名。</param>
    /// <param name="permission">权限串。</param>
    /// <returns>1 表示有变更。</returns>
    private static Int32 Upsert(String name, String permission)
    {
        var role = Role.Find(Role._.Name == name);
        if (role == null)
        {
            role = new Role
            {
                Name = name,
                // 2 = 普通角色；系统角色只留给魔方内置的管理员
                Type = (RoleTypes)2,
                Enable = true,
                IsSystem = false,
                Permission = permission,
                Sort = 0,
                Remark = "EFBM 内置角色，由启动初始化维护",
            };
            return role.Insert();
        }

        // 只改本系统关心的字段，避免把现场改过的排序 / 备注冲掉
        if (role.Permission == permission && role.Enable) return 0;

        role.Permission = permission;
        role.Enable = true;
        return role.Update();
    }
}
