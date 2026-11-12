using System.ComponentModel;
using System.Reflection;
using EFBM.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;
using XCode.Membership;

namespace EFBM.Tests;

/// <summary>后台控制器的结构与权限标注：所有页面都必须按魔方标准挂 Area / 菜单 / 权限位。</summary>
/// <remarks>
/// 这组测试守的是「结构性约定」，不是某个页面的行为：
/// ① 控制器必须带 <see cref="AdminArea"/>、<see cref="MenuAttribute"/>、<see cref="DisplayNameAttribute"/>，
///    否则魔方不会生成菜单，<c>EntityAuthorize</c> 找不到菜单会直接 403；
/// ② 每个公开动作都必须有 <see cref="EntityAuthorizeAttribute"/>，权限位与约定一致
///    （查询 = Detail、写 = Update、删 = Delete）。漏标会被这里拦住，而不是等上线后被发现「谁都能删」。
/// </remarks>
public class AdminAreaTests
{
    /// <summary>后台全部控制器。</summary>
    public static TheoryData<Type> Controllers => new()
    {
        typeof(DeviceEventRawController),
        typeof(DeadLetterController),
        typeof(StatusDictController),
        typeof(DeviceController),
        typeof(AppCredentialController),
        typeof(ProductionRecordController),
        typeof(WorkOrderController),
        typeof(FaultRecordController),
        typeof(DeviceStatusLogController),
        typeof(EventMaintenanceController),
        typeof(FaultDisposalController),
        typeof(QueueMonitorController),
        typeof(FieldEvolutionController),
        typeof(ConsumerGroupController),
    };

    [Theory(DisplayName = "后台控制器_都挂魔方区域与菜单特性")]
    [MemberData(nameof(Controllers))]
    public void Controller_HasAreaAndMenu(Type type)
    {
        Assert.NotNull(type.GetCustomAttribute<AdminArea>());
        Assert.NotNull(type.GetCustomAttribute<MenuAttribute>());
        Assert.NotNull(type.GetCustomAttribute<DisplayNameAttribute>());
        Assert.False(String.IsNullOrWhiteSpace(type.GetCustomAttribute<DisplayNameAttribute>()!.DisplayName));
    }

    [Theory(DisplayName = "后台控制器_命名空间都在 Areas.Admin 下")]
    [MemberData(nameof(Controllers))]
    public void Controller_LivesInAdminArea(Type type)
        => Assert.EndsWith(".Areas.Admin.Controllers", type.Namespace);

    [Theory(DisplayName = "实体控制器_列表与详情要求查看权限")]
    [InlineData(typeof(DeviceEventRawController))]
    [InlineData(typeof(DeadLetterController))]
    [InlineData(typeof(StatusDictController))]
    [InlineData(typeof(DeviceController))]
    [InlineData(typeof(AppCredentialController))]
    [InlineData(typeof(ProductionRecordController))]
    [InlineData(typeof(WorkOrderController))]
    [InlineData(typeof(FaultRecordController))]
    [InlineData(typeof(DeviceStatusLogController))]
    [InlineData(typeof(ConsumerGroupController))]
    public void EntityController_ReadActionsRequireDetail(Type type)
    {
        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, "Index"));
        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, "Detail"));
    }

    [Theory(DisplayName = "可维护实体_增改删分别要求新增修改删除权限")]
    [InlineData(typeof(StatusDictController))]
    [InlineData(typeof(DeviceController))]
    [InlineData(typeof(AppCredentialController))]
    [InlineData(typeof(ConsumerGroupController))]
    public void EntityController_WriteActionsRequireFlags(Type type)
    {
        Assert.Equal(PermissionFlags.Insert, PermissionOf(type, "Add"));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, "Edit"));
        Assert.Equal(PermissionFlags.Delete, PermissionOf(type, "Delete"));
    }

    [Fact(DisplayName = "维护页_页面看查看_重放改标记要修改_清理要删除")]
    public void Maintenance_Permissions()
    {
        var type = typeof(EventMaintenanceController);

        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(EventMaintenanceController.Index)));
        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(EventMaintenanceController.Export)));
        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(EventMaintenanceController.Download)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(EventMaintenanceController.ReplayEvent)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(EventMaintenanceController.ReplayRange)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(EventMaintenanceController.ReplayDeadLetter)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(EventMaintenanceController.Mark)));
        Assert.Equal(PermissionFlags.Delete, PermissionOf(type, nameof(EventMaintenanceController.Cleanup)));
    }

    [Fact(DisplayName = "故障处置页_看要查看_关要修改")]
    public void FaultDisposal_Permissions()
    {
        var type = typeof(FaultDisposalController);

        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(FaultDisposalController.Index)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(FaultDisposalController.Close)));
    }

    [Fact(DisplayName = "消费组监控页_页面与快照都只要查看")]
    public void QueueMonitor_Permissions()
    {
        var type = typeof(QueueMonitorController);

        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(QueueMonitorController.Index)));
        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(QueueMonitorController.Snapshot)));
    }

    [Fact(DisplayName = "字段演进页_看要查看_补登要修改_删除要删除")]
    public void FieldEvolution_Permissions()
    {
        var type = typeof(FieldEvolutionController);

        Assert.Equal(PermissionFlags.Detail, PermissionOf(type, nameof(FieldEvolutionController.Index)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(FieldEvolutionController.Register)));
        Assert.Equal(PermissionFlags.Update, PermissionOf(type, nameof(FieldEvolutionController.Backfill)));
        Assert.Equal(PermissionFlags.Delete, PermissionOf(type, nameof(FieldEvolutionController.Delete)));
    }

    [Fact(DisplayName = "自定义页面_都继承EFBM后台基类以复用确认与操作人")]
    public void CustomPages_UseEfbmBase()
    {
        foreach (var type in new[] { typeof(EventMaintenanceController), typeof(FaultDisposalController), typeof(QueueMonitorController), typeof(FieldEvolutionController) })
        {
            Assert.True(typeof(EfbmAdminController).IsAssignableFrom(type), $"{type.Name} 应继承 EfbmAdminController");
        }
    }

    [Theory(DisplayName = "后台动作_没有漏标权限的公开方法")]
    [MemberData(nameof(Controllers))]
    public void Controller_NoUnannotatedPublicAction(Type type)
    {
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(e => typeof(IActionResult).IsAssignableFrom(e.ReturnType) || typeof(Task<IActionResult>).IsAssignableFrom(e.ReturnType))
            .Where(e => !e.IsSpecialName);

        foreach (var action in actions)
        {
            var attr = action.GetCustomAttribute<EntityAuthorizeAttribute>();
            Assert.True(attr != null, $"{type.Name}.{action.Name} 缺少 [EntityAuthorize]，等于把接口裸奔");
        }
    }

    [Fact(DisplayName = "维护页_写操作全部要求二次确认头")]
    public void Maintenance_WriteActionsCheckConfirm()
    {
        var source = File.ReadAllText(RepoPaths.Find("src/EFBM.Web/Areas/Admin/Controllers/EventMaintenanceController.cs"));

        // 只有真正会改数据的动作才需要确认；导出与页面本身不需要
        foreach (var action in new[] { "ReplayEvent", "ReplayRange", "ReplayDeadLetter", "Cleanup" })
        {
            var block = Slice(source, $"public async Task<ActionResult> {action}(");
            Assert.Contains("Confirmed(", block);
        }

        Assert.DoesNotContain("Confirmed(", Slice(source, "public async Task<ActionResult> Mark("));
    }

    [Fact(DisplayName = "故障处置与字段演进_写操作也强制二次确认")]
    public void DisposalAndEvolution_WriteActionsCheckConfirm()
    {
        var fault = File.ReadAllText(RepoPaths.Find("src/EFBM.Web/Areas/Admin/Controllers/FaultDisposalController.cs"));
        Assert.Contains("Confirmed(", Slice(fault, "public async Task<ActionResult> Close("));

        var field = File.ReadAllText(RepoPaths.Find("src/EFBM.Web/Areas/Admin/Controllers/FieldEvolutionController.cs"));
        foreach (var action in new[] { "Register", "Delete", "Backfill" })
        {
            Assert.Contains("Confirmed(", Slice(field, $"public async Task<ActionResult> {action}("));
        }
    }

    [Theory(DisplayName = "内置角色_权限串是魔方格式")]
    [InlineData(1, "990002#1,990003#1")]
    [InlineData(5, "990002#5,990003#5")]
    public void RoleSeed_PermissionFormat(Int32 flags, String expected)
    {
        var menus = new[] { new Menu { ID = 990002 }, new Menu { ID = 990003 } };

        Assert.Equal(expected, EFBM.Web.Services.AdminRoleSeed.BuildPermission(menus, flags));
    }

    [Fact(DisplayName = "内置角色_没有菜单时权限串为空")]
    public void RoleSeed_EmptyMenus()
        => Assert.Equal(String.Empty, EFBM.Web.Services.AdminRoleSeed.BuildPermission([], 1));

    /// <summary>取动作上的权限位。</summary>
    /// <param name="type">控制器类型。</param>
    /// <param name="methodName">动作名。</param>
    /// <returns>权限位。</returns>
    /// <remarks>
    /// 魔方的 <c>Add</c> / <c>Edit</c> 都有两个重载（打开表单 / 提交表单），
    /// <c>GetMethod</c> 会抛 <see cref="AmbiguousMatchException"/>，这里取参数最少的那个——
    /// 打开表单的动作才是暴露给路由 GET 的入口，也是权限位所在。
    /// </remarks>
    private static PermissionFlags PermissionOf(Type type, String methodName)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(e => e.Name == methodName)
            .OrderBy(e => e.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{type.Name} 没有动作 {methodName}");

        var attr = method.GetCustomAttribute<EntityAuthorizeAttribute>(inherit: true)
            ?? throw new InvalidOperationException($"{type.Name}.{methodName} 缺少 [EntityAuthorize]");

        return attr.Permission;
    }

    /// <summary>截取从指定锚点到下一个空行之间的代码块，用于检查动作内部逻辑。</summary>
    /// <param name="source">源文件全文。</param>
    /// <param name="anchor">起始锚点。</param>
    /// <returns>代码块。</returns>
    private static String Slice(String source, String anchor)
    {
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"源码里找不到 {anchor}");

        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }
}
