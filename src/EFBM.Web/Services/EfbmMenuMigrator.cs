using NewLife.Log;
using XCode.Membership;

namespace EFBM.Web.Services;

/// <summary>把业务菜单从 Cube 自带的「系统管理」树迁到独立的「电动折盒机」树。</summary>
/// <remarks>
/// 为什么要迁移而不是让 Cube 重新扫描生成：<list type="bullet">
/// <item>魔方的角色权限串按<b>菜单 ID</b>授权（<c>菜单ID#权限位</c>），重新生成会让 ID 变化，
/// 现场在「角色」页配过的权限全部失效；迁移保留原 ID，权限无缝延续。</item>
/// <item>Cube 的 <c>CubeArea</c> 有自己的「菜单迁移 Admin→Cube」逻辑，但业务菜单走的
/// <c>AdminArea</c> 不含其中；这里显式把业务菜单归入 <c>EfbmArea</c>，两棵树彻底分开。</item>
/// </list>
/// 执行时机：必须在 <c>app.UseCube()</c> 之前——Cube 在 UseCube 里扫描控制器生成菜单，
/// 迁移先跑才能让它认出已有菜单而不重复新建。方法幂等，重复执行不会造出重复菜单。
/// </remarks>
public static class EfbmMenuMigrator
{
    /// <summary>业务控制器新命名空间（与 EfbmArea 一致）。</summary>
    public const String NewNamespace = "EFBM.Web.Areas.Efbm.Controllers";

    /// <summary>业务控制器旧命名空间（迁移前在 AdminArea 下）。</summary>
    public const String OldNamespace = "EFBM.Web.Areas.Admin.Controllers";

    /// <summary>业务菜单新 URL 前缀。</summary>
    public const String NewUrlPrefix = "/Efbm";

    /// <summary>业务菜单根名称（与 EfbmArea 的区域名一致）。</summary>
    public const String RootName = "Efbm";

    /// <summary>把旧 Admin 树下的业务菜单迁到 Efbm 树。</summary>
    /// <returns>迁移的菜单数（含根菜单）；0 表示已是最新结构。</returns>
    public static Int32 Migrate()
    {
        // 幂等：新版菜单已存在就不再动（Cube 升级后可能自动扫描生成，避免二次覆盖管理员改过的排序）
        var existRoot = Menu.FindByFullName(NewNamespace);
        if (existRoot != null && Menu.FindAll().Any(e => e.FullName?.StartsWith(NewNamespace + ".") == true))
            return 0;

        var oldMenus = Menu.FindAll()
            .Where(e => e.FullName != null && e.FullName.StartsWith(OldNamespace + ".", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (oldMenus.Count == 0) return 0;

        // 建根菜单（Efbm）：Cube 扫描控制器时会按 FullName 找到它并把业务菜单挂在其下。
        // 显示名取 EfbmArea 的 [DisplayName]，与 Cube 自带的「系统管理 / 魔方管理」区分。
        var root = existRoot ?? new Menu
        {
            Name = RootName,
            DisplayName = "电动折盒机",
            FullName = NewNamespace,
            Url = NewUrlPrefix,
            ParentID = 0,
            Sort = 120,
            Visible = true,
            Necessary = true,
        };
        if (root.ID == 0)
        {
            root.Insert();
            XTrace.WriteLine("业务菜单根已创建：{0}（ID={1}）", root.DisplayName, root.ID);
        }

        var count = 0;
        foreach (var menu in oldMenus)
        {
            var suffix = menu.FullName![OldNamespace.Length..];
            menu.FullName = NewNamespace + suffix;
            if (!String.IsNullOrEmpty(menu.Url) && menu.Url.StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase))
                menu.Url = NewUrlPrefix + menu.Url[6..];
            menu.ParentID = root.ID;
            count += menu.Update();
        }

        XTrace.WriteLine("业务菜单已迁移到「{0}」树：{1} 项（菜单 ID 与角色权限保持不变）", root.DisplayName, count);
        return count;
    }

    /// <summary>删除 Cube 误把抽象基类当成控制器生成的菜单。</summary>
    /// <remarks>
    /// Cube 的 <c>ScanController</c> 按「命名空间 + 类型名后缀 Controller」扫描，不排除 abstract 类型。
    /// 新建 Area 首次扫描时会把 <c>EfbmAdminController</c> 也加进菜单——它没有可访问页面，
    /// 出现在侧边栏只会让人困惑。放在 <c>UseCube</c> 之后调用，此时 Cube 已完成扫描。
    /// </remarks>
    /// <returns>删除的菜单数。</returns>
    public static Int32 RemovePhantomMenus()
    {
        var phantoms = Menu.FindAll()
            .Where(e => e.Name == "EfbmAdmin" || e.FullName == NewNamespace + ".EfbmAdminController")
            .ToList();

        var count = 0;
        foreach (var menu in phantoms)
            count += menu.Delete();

        if (count > 0) XTrace.WriteLine("已清理误生成的抽象基类菜单：{0} 项", count);
        return count;
    }
}
