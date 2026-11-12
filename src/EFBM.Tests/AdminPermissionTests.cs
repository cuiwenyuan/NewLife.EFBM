using System.Reflection;
using System.Security.Claims;
using EFBM.Core.Security;
using EFBM.Web.Controllers;
using EFBM.Web.Models;
using EFBM.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Tests;

/// <summary>三角色权限矩阵与后台鉴权链路的测试（FR-14、验收用例 C13）。</summary>
public class AdminPermissionTests
{
    // ── 一、矩阵本身 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "权限矩阵_只读角色_只能看不能改")]
    public void Matrix_Viewer_ReadOnly()
    {
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.QueryEvents));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.ViewEventDetail));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.QueryDeadLetters));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.ViewDeadLetterDetail));

        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.MarkEvent));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.ReplayEvent));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.ExportEvents));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.DownloadExport));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.ReplayDeadLetter));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.CleanupEvents));
    }

    [Fact(DisplayName = "权限矩阵_运维角色_可标记重放导出但不能清理")]
    public void Matrix_Operator_CanRepairButNotDelete()
    {
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.MarkEvent));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.ReplayEvent));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.ExportEvents));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.DownloadExport));
        Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.ReplayDeadLetter));

        // 删除数据只给管理员：运维能导出、能重放，但不能抹掉证据
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Operator, AdminOperation.CleanupEvents));
    }

    [Fact(DisplayName = "权限矩阵_管理员_拥有全部操作")]
    public void Matrix_Admin_HasAll()
    {
        foreach (var op in Enum.GetValues<AdminOperation>())
        {
            Assert.True(AdminPermissionMatrix.Allows(AdminRole.Admin, op), $"管理员应能做 {op}");
        }
    }

    [Fact(DisplayName = "权限矩阵_角色包含关系_高角色覆盖低角色全部操作")]
    public void Matrix_RolesAreSuperset()
    {
        foreach (var op in AdminPermissionMatrix.AllowedFor(AdminRole.Viewer))
        {
            Assert.True(AdminPermissionMatrix.Allows(AdminRole.Operator, op), $"运维应继承只读的 {op}");
            Assert.True(AdminPermissionMatrix.Allows(AdminRole.Admin, op), $"管理员应继承只读的 {op}");
        }

        foreach (var op in AdminPermissionMatrix.AllowedFor(AdminRole.Operator))
        {
            Assert.True(AdminPermissionMatrix.Allows(AdminRole.Admin, op), $"管理员应继承运维的 {op}");
        }
    }

    [Fact(DisplayName = "权限矩阵_每个操作都标了最低角色_新增操作不能漏配")]
    public void Matrix_EveryOperationHasMinimumRole()
    {
        foreach (var op in Enum.GetValues<AdminOperation>())
        {
            var min = AdminPermissionMatrix.MinimumRole(op);
            Assert.NotNull(min);
            Assert.True(AdminPermissionMatrix.Allows(min.Value, op));
        }

        // 关键操作的归属固定：清理只能管理员、查看类最低只读
        Assert.Equal(AdminRole.Admin, AdminPermissionMatrix.MinimumRole(AdminOperation.CleanupEvents));
        Assert.Equal(AdminRole.Operator, AdminPermissionMatrix.MinimumRole(AdminOperation.ReplayEvent));
        Assert.Equal(AdminRole.Viewer, AdminPermissionMatrix.MinimumRole(AdminOperation.QueryEvents));
    }

    [Fact(DisplayName = "权限矩阵_快照_三个角色齐全且顺序稳定")]
    public void Matrix_Describe()
    {
        var map = AdminPermissionMatrix.Describe();

        Assert.Equal(3, map.Count);
        Assert.Contains("viewer", map.Keys);
        Assert.Contains("operator", map.Keys);
        Assert.Contains("admin", map.Keys);
        Assert.Equal(Enum.GetValues<AdminOperation>().Length, map["admin"].Length);
        Assert.DoesNotContain(nameof(AdminOperation.CleanupEvents), map["operator"]);
    }

    [Fact(DisplayName = "权限矩阵_角色标识与中文名可互相解析")]
    public void RoleInfo_Names()
    {
        Assert.Equal("admin", AdminRoleInfo.Name(AdminRole.Admin));
        Assert.Equal("管理员", AdminRoleInfo.DisplayName(AdminRole.Admin));
        Assert.Equal(AdminRole.Operator, AdminRoleInfo.Parse("运维"));
        Assert.Equal(AdminRole.Viewer, AdminRoleInfo.Parse("VIEWER"));
        Assert.Equal(AdminRole.Admin, AdminRoleInfo.Parse(" admin "));
        Assert.Null(AdminRoleInfo.Parse("车间主任"));
        Assert.Null(AdminRoleInfo.Parse(null));
    }

    // ── 二、角色解析：Cube 角色声明 → 三角色 ─────────────────────────────────

    [Theory(DisplayName = "角色解析_Cube常见角色名_映射到对应角色")]
    [InlineData("admin", AdminRole.Admin)]
    [InlineData("管理员", AdminRole.Admin)]
    [InlineData("系统管理员", AdminRole.Admin)]
    [InlineData("Administrator", AdminRole.Admin)]
    [InlineData("运维", AdminRole.Operator)]
    [InlineData("ops", AdminRole.Operator)]
    [InlineData("只读", AdminRole.Viewer)]
    [InlineData("readonly", AdminRole.Viewer)]
    [InlineData("Guest", AdminRole.Viewer)]
    public void Resolver_KnownRoleNames(String name, AdminRole expected)
    {
        var principal = Principal(ClaimTypes.Role, name);

        Assert.Equal(expected, new AdminRoleResolver().Resolve(principal));
    }

    [Fact(DisplayName = "角色解析_同时属于多个组_取权限最高的角色")]
    public void Resolver_MultipleClaims_TakesHighest()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "只读"),
            new Claim("roles", "运维"),
            new Claim("Role", "管理员"),
        ], "Cookies"));

        Assert.Equal(AdminRole.Admin, new AdminRoleResolver().Resolve(principal));
    }

    [Fact(DisplayName = "角色解析_一个声明里多个角色_仍取最高")]
    public void Resolver_CommaSeparatedValue_TakesHighest()
    {
        Assert.Equal(AdminRole.Operator, new AdminRoleResolver().Match("只读,运维"));
        Assert.Equal(AdminRole.Admin, new AdminRoleResolver().Match("viewer;admin"));
        Assert.Equal(AdminRole.Viewer, new AdminRoleResolver().Match("只读,访客,guest"));
    }

    [Fact(DisplayName = "角色解析_数字角色ID无法识别_返回空而不是猜")]
    public void Resolver_NumericRoleIds_ReturnsNull()
    {
        var principal = Principal("RoleIDs", "1,2");

        Assert.Null(new AdminRoleResolver().Resolve(principal));
    }

    [Fact(DisplayName = "角色解析_无角色声明或未登录_返回空")]
    public void Resolver_NoRole_ReturnsNull()
    {
        Assert.Null(new AdminRoleResolver().Resolve(null));
        Assert.Null(new AdminRoleResolver().Resolve(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(new AdminRoleResolver().Resolve(Principal("name", "kaixi")));
    }

    [Fact(DisplayName = "角色解析_配置别名_支持现场自定义角色名并可覆盖内置")]
    public void Resolver_ConfiguredAliases()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.RoleAliases["车间主任"] = "viewer";
        options.Admin.RoleAliases["guest"] = "operator";

        var resolver = new AdminRoleResolver(options);

        Assert.Equal(AdminRole.Viewer, resolver.Resolve(Principal(ClaimTypes.Role, "车间主任")));
        Assert.Equal(AdminRole.Operator, resolver.Resolve(Principal(ClaimTypes.Role, "guest")));
    }

    [Fact(DisplayName = "角色解析_用户名为准_初始超管没有角色声明也能进后台")]
    public void Resolver_UserNameMapping()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.Users["admin"] = "admin";

        var resolver = new AdminRoleResolver(options);

        // 票据里没有任何角色声明，只有用户名 —— Cube 初始超管就是这种情况
        Assert.Equal(AdminRole.Admin, resolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "Cookies"))));
        Assert.Equal(AdminRole.Admin, resolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "Cookies", ClaimTypes.Name, ClaimTypes.Role))));

        // 未在表里的账号仍按角色声明判定，不会因为存在用户名映射就全放行
        Assert.Null(resolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "zhangsan")], "Cookies"))));
    }

    [Fact(DisplayName = "角色解析_用户名映射优先于角色声明")]
    public void Resolver_UserNameWins()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.Users["auditor"] = "viewer";

        var resolver = new AdminRoleResolver(options);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "auditor"),
            new Claim(ClaimTypes.Role, "管理员"),
        ], "Cookies"));

        Assert.Equal(AdminRole.Viewer, resolver.Resolve(principal));
    }

    [Fact(DisplayName = "角色别名配置_值非法_启动自检直接失败")]
    public void AdminOptions_InvalidAlias_Throws()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.RoleAliases["车间主任"] = "老板";

        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
        Assert.Contains("车间主任", ex.Message);
    }

    [Fact(DisplayName = "用户名映射配置_值非法_启动自检直接失败")]
    public void AdminOptions_InvalidUserRole_Throws()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.Users["admin"] = "superuser";

        var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
        Assert.Contains("admin", ex.Message);
    }

    [Fact(DisplayName = "角色解析_内置覆盖Cube高级用户_映射为运维")]
    public void Resolver_BuiltinSeniorUser_IsOperator()
        => Assert.Equal(AdminRole.Operator, new AdminRoleResolver().Resolve(Principal(ClaimTypes.Role, "高级用户")));

    [Fact(DisplayName = "角色别名配置_空键_启动自检直接失败")]
    public void AdminOptions_EmptyKey_Throws()
    {
        var options = new EFBM.Core.EfbmOptions();
        options.Admin.RoleAliases[" "] = "viewer";

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    // ── 三、接口标注：每个动作都声明了要求，且映射正确 ───────────────────────

    [Fact(DisplayName = "接口标注_后台两个控制器_每个动作都有权限声明")]
    public void Controllers_EveryActionHasOperation()
    {
        foreach (var type in new[] { typeof(AdminEventsController), typeof(AdminDeadLettersController) })
        {
            var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .ToArray();

            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var attr = action.GetCustomAttribute<RequireAdminOperationAttribute>();
                Assert.True(attr != null, $"{type.Name}.{action.Name} 缺少 [RequireAdminOperation]");
            }
        }
    }

    [Theory(DisplayName = "接口标注_动作与权限一一对应")]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.List), AdminOperation.QueryEvents)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Get), AdminOperation.ViewEventDetail)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Mark), AdminOperation.MarkEvent)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Replay), AdminOperation.ReplayEvent)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.ReplayRange), AdminOperation.ReplayEvent)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Export), AdminOperation.ExportEvents)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Download), AdminOperation.DownloadExport)]
    [InlineData(typeof(AdminEventsController), nameof(AdminEventsController.Cleanup), AdminOperation.CleanupEvents)]
    [InlineData(typeof(AdminDeadLettersController), nameof(AdminDeadLettersController.List), AdminOperation.QueryDeadLetters)]
    [InlineData(typeof(AdminDeadLettersController), nameof(AdminDeadLettersController.Get), AdminOperation.ViewDeadLetterDetail)]
    [InlineData(typeof(AdminDeadLettersController), nameof(AdminDeadLettersController.Replay), AdminOperation.ReplayDeadLetter)]
    public void Controllers_OperationMapping(Type controller, String methodName, AdminOperation expected)
    {
        var method = controller.GetMethod(methodName);

        Assert.NotNull(method);
        var attr = method.GetCustomAttribute<RequireAdminOperationAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expected, attr.Operation);
    }

    [Fact(DisplayName = "接口标注_权限查询接口只要求登录_未授权账号也能读到自己没有角色")]
    public void Controllers_PermissionsEndpoint_OnlyRequiresLogin()
    {
        var method = typeof(AdminPermissionsController).GetMethod(nameof(AdminPermissionsController.Get));

        Assert.NotNull(method);
        Assert.Null(method.GetCustomAttribute<RequireAdminOperationAttribute>());
    }

    // ── 四、权限查询接口的返回内容 ─────────────────────────────────────────────

    [Fact(DisplayName = "权限接口_管理员_返回角色与全量矩阵")]
    public void Permissions_Admin_ReturnsMatrix()
    {
        using var json = CallPermissions(AdminRole.Admin);
        var root = json.RootElement;

        Assert.Equal(0, root.GetProperty("code").GetInt32());
        var data = root.GetProperty("data");
        Assert.Equal("admin", data.GetProperty("role").GetString());
        Assert.Equal("管理员", data.GetProperty("roleName").GetString());

        var allowed = data.GetProperty("allowed").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(nameof(AdminOperation.CleanupEvents), allowed);

        var matrix = data.GetProperty("matrix");
        Assert.Equal(3, matrix.EnumerateObject().Count());
        Assert.Equal(Enum.GetValues<AdminOperation>().Length, matrix.GetProperty("admin").GetArrayLength());

        // operations 里的 minRole 必须与矩阵判定同源，页面才不会出现「显示能点、点了 403」
        foreach (var op in data.GetProperty("operations").EnumerateArray())
        {
            var name = Enum.Parse<AdminOperation>(op.GetProperty("name").GetString());
            var min = AdminPermissionMatrix.MinimumRole(name);
            Assert.Equal(AdminRoleInfo.Name(min.Value), op.GetProperty("minRole").GetString());
        }
    }

    [Fact(DisplayName = "权限接口_认不出角色_返回空权限而不是报错")]
    public void Permissions_NoRole_ReturnsEmptyAllowed()
    {
        using var json = CallPermissions(null);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("role").ValueKind);
        Assert.Equal("未授权", data.GetProperty("roleName").GetString());
        Assert.Equal(0, data.GetProperty("allowed").GetArrayLength());
    }

    /// <summary>以指定角色调用一次权限接口，返回解析后的 JSON。</summary>
    /// <param name="role">要模拟的角色；<c>null</c> 表示认不出角色。</param>
    /// <returns>可查询的 JSON 文档。</returns>
    private static System.Text.Json.JsonDocument CallPermissions(AdminRole? role)
    {
        var http = new DefaultHttpContext();
        if (role != null) http.User = Principal(ClaimTypes.Role, AdminRoleInfo.Name(role.Value));

        var controller = new AdminPermissionsController(new StubResolver(role))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var result = Assert.IsType<ObjectResult>(controller.Get());
        var body = Assert.IsType<ApiResponse>(result.Value);
        return System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(body));
    }

    // ── 五、过滤器实际行为 ────────────────────────────────────────────────────

    [Fact(DisplayName = "鉴权过滤_未授予角色_返回403统一信封")]
    public void Filter_NoRole_Returns403()
    {
        var result = RunFilter(null, AdminOperation.QueryEvents);

        var body = Assert.IsType<ApiResponse>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(403, body.Code);
        Assert.Contains("未授予后台角色", body.Message);
        Assert.Null(body.Data);
    }

    [Fact(DisplayName = "鉴权过滤_只读角色执行清理_返回403并说明缺哪项权限")]
    public void Filter_ViewerCannotCleanup_Returns403()
    {
        var result = RunFilter(AdminRole.Viewer, AdminOperation.CleanupEvents);

        var obj = Assert.IsType<ObjectResult>(result);
        var body = Assert.IsType<ApiResponse>(obj.Value);
        Assert.Equal(403, obj.StatusCode);
        Assert.Contains("只读", body.Message);
        Assert.Contains("清理原始报文", body.Message);
    }

    [Theory(DisplayName = "鉴权过滤_角色够权限_放行")]
    [InlineData(AdminRole.Viewer, AdminOperation.QueryEvents)]
    [InlineData(AdminRole.Operator, AdminOperation.ReplayEvent)]
    [InlineData(AdminRole.Operator, AdminOperation.ExportEvents)]
    [InlineData(AdminRole.Admin, AdminOperation.CleanupEvents)]
    public void Filter_Allowed_Passes(AdminRole role, AdminOperation operation)
        => Assert.Null(RunFilter(role, operation));

    /// <summary>构造一个带角色的登录主体。</summary>
    /// <param name="type">声明类型。</param>
    /// <param name="value">声明值。</param>
    /// <returns>用户主体。</returns>
    private static ClaimsPrincipal Principal(String type, String value)
        => new(new ClaimsIdentity([new Claim(type, value)], "Cookies"));

    /// <summary>跑一次过滤器，返回它写入的拦截结果（放行时为 <c>null</c>）。</summary>
    /// <param name="role">解析器要返回的角色；<c>null</c> 表示认不出角色。</param>
    /// <param name="operation">被保护的操作。</param>
    /// <returns>拦截结果或 <c>null</c>。</returns>
    private static IActionResult RunFilter(AdminRole? role, AdminOperation operation)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAdminRoleResolver>(new StubResolver(role))
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (role != null) http.User = Principal(ClaimTypes.Role, AdminRoleInfo.Name(role.Value));

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, []);

        new RequireAdminOperationAttribute(operation).OnAuthorizationAsync(context).GetAwaiter().GetResult();

        return context.Result;
    }

    /// <summary>固定返回指定角色的解析器替身（真实解析逻辑已在上面的用例单独覆盖）。</summary>
    private sealed class StubResolver(AdminRole? role) : IAdminRoleResolver
    {
        public AdminRole? Resolve(ClaimsPrincipal user) => role;
    }
}
