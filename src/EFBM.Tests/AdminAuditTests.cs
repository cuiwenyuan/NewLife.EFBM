using System.Reflection;
using EFBM.Core.Security;
using EFBM.Web.Controllers;
using EFBM.Web.Models;
using EFBM.Web.Security;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Tests;

/// <summary>敏感操作二次确认与审计留痕的测试（FR-15、AUTH-3b、验收用例 C13）。</summary>
public class AdminAuditTests
{
    // ── 一、二次确认 ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "二次确认_缺确认头_返回400并说明该带什么")]
    public void Confirm_MissingHeader_Returns400()
    {
        var result = Assert.IsType<ObjectResult>(RunConfirm(AdminOperation.CleanupEvents, null));
        var body = Assert.IsType<ApiResponse>(result.Value);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("二次确认", body.Message);
        Assert.Contains(nameof(AdminOperation.CleanupEvents), body.Message);
        Assert.Null(body.Data);
    }

    [Theory(DisplayName = "二次确认_头部值不对_仍然拒绝")]
    [InlineData("true")]
    [InlineData("ReplayEvent")]
    [InlineData("")]
    public void Confirm_WrongValue_Returns400(String value)
    {
        var result = Assert.IsType<ObjectResult>(RunConfirm(AdminOperation.CleanupEvents, value));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact(DisplayName = "二次确认_头部值与动作一致_放行（忽略大小写与空白）")]
    public void Confirm_MatchingValue_Passes()
    {
        Assert.Null(RunConfirm(AdminOperation.CleanupEvents, " CleanupEvents "));
        Assert.Null(RunConfirm(AdminOperation.CleanupEvents, "cleanupevents"));
    }

    [Fact(DisplayName = "二次确认_排在角色判定之后，避免越权者探到要确认什么")]
    public void Confirm_RunsAfterPermission()
    {
        Assert.Equal(100, new RequireConfirmAttribute(AdminOperation.CleanupEvents).Order);
    }

    [Fact(DisplayName = "接口标注_危险动作都要求二次确认_且与权限动作一致")]
    public void Controllers_DangerousActionsRequireConfirm()
    {
        var expected = new Dictionary<String, AdminOperation>
        {
            [nameof(AdminEventsController.Replay)] = AdminOperation.ReplayEvent,
            [nameof(AdminEventsController.ReplayRange)] = AdminOperation.ReplayEvent,
            [nameof(AdminEventsController.Cleanup)] = AdminOperation.CleanupEvents,
        };

        foreach (var (method, operation) in expected)
        {
            var attr = typeof(AdminEventsController).GetMethod(method)?.GetCustomAttribute<RequireConfirmAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(operation, attr.Operation);
        }

        var dead = typeof(AdminDeadLettersController).GetMethod(nameof(AdminDeadLettersController.Replay))
            ?.GetCustomAttribute<RequireConfirmAttribute>();
        Assert.NotNull(dead);
        Assert.Equal(AdminOperation.ReplayDeadLetter, dead.Operation);
    }

    [Fact(DisplayName = "接口标注_只读查询类动作不要求二次确认")]
    public void Controllers_ReadOnlyActionsNeedNoConfirm()
    {
        foreach (var method in new[] { nameof(AdminEventsController.List), nameof(AdminEventsController.Get), nameof(AdminEventsController.Download) })
        {
            var attr = typeof(AdminEventsController).GetMethod(method)?.GetCustomAttribute<RequireConfirmAttribute>();
            Assert.Null(attr);
        }
    }

    // ── 二、审计留痕 ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "审计_成功操作_记下操作人目标与结果摘要")]
    public void Audit_Success_RecordsOperatorTargetAndSummary()
    {
        var store = new FakeAuditStore();
        RunAudit(store, "清理原始报文", new ApiResponse { Code = 0, Message = "ok", Data = new { deleted = 5 } }, routeId: "42");

        var record = Assert.Single(store.Written);
        Assert.Equal("清理原始报文", record.Action);
        Assert.True(record.Success);
        Assert.Equal(42, record.TargetId);
        Assert.Equal("tester", record.Operator);
        Assert.Contains("deleted", record.Summary);
        Assert.Equal("10.0.0.9", record.Ip);
        Assert.Equal("trace-1", record.TraceId);
    }

    [Fact(DisplayName = "审计_业务失败_记下原因而不是结果")]
    public void Audit_Failure_RecordsReason()
    {
        var store = new FakeAuditStore();
        RunAudit(store, "清理原始报文", new ApiResponse { Code = 409, Message = "凭证无效" }, statusCode: 409);

        var record = Assert.Single(store.Written);
        Assert.False(record.Success);
        Assert.Equal("凭证无效", record.Summary);
    }

    [Fact(DisplayName = "审计_动作抛异常_仍记一条失败")]
    public void Audit_Exception_RecordsFailure()
    {
        var store = new FakeAuditStore();
        RunAudit(store, "重放原始报文", null, routeId: "7", exception: new InvalidOperationException("boom"));

        var record = Assert.Single(store.Written);
        Assert.False(record.Success);
        Assert.Contains("InvalidOperationException", record.Summary);
        Assert.Equal(7, record.TargetId);
    }

    [Fact(DisplayName = "接口标注_写操作都记审计_读操作不记")]
    public void Controllers_WriteActionsAreAudited()
    {
        var audited = new Dictionary<String, String>
        {
            [nameof(AdminEventsController.Mark)] = "标记处理状态",
            [nameof(AdminEventsController.Replay)] = "重放原始报文",
            [nameof(AdminEventsController.ReplayRange)] = "按范围批量重放",
            [nameof(AdminEventsController.Export)] = "导出原始报文",
            [nameof(AdminEventsController.Cleanup)] = "清理原始报文",
        };

        foreach (var (method, action) in audited)
        {
            var attr = typeof(AdminEventsController).GetMethod(method)?.GetCustomAttribute<AuditAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(action, attr.Action);
        }

        Assert.NotNull(typeof(AdminDeadLettersController).GetMethod(nameof(AdminDeadLettersController.Replay))
            ?.GetCustomAttribute<AuditAttribute>());

        // 只读接口不写审计，避免把日志表刷满
        Assert.Null(typeof(AdminEventsController).GetMethod(nameof(AdminEventsController.List))?.GetCustomAttribute<AuditAttribute>());
        Assert.Null(typeof(AdminEventsController).GetMethod(nameof(AdminEventsController.Get))?.GetCustomAttribute<AuditAttribute>());
    }

    [Fact(DisplayName = "接口标注_审计查询需要运维及以上权限")]
    public void Controllers_AuditQueryNeedsOperator()
    {
        var method = typeof(AdminAuditsController).GetMethod(nameof(AdminAuditsController.List));

        Assert.NotNull(method);
        var attr = method.GetCustomAttribute<RequireAdminOperationAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(AdminOperation.QueryAudits, attr.Operation);
        Assert.Equal(AdminRole.Operator, AdminPermissionMatrix.MinimumRole(AdminOperation.QueryAudits));
        Assert.False(AdminPermissionMatrix.Allows(AdminRole.Viewer, AdminOperation.QueryAudits));
    }

    // ── 三、过滤器与上下文的构造 ───────────────────────────────────────────────

    /// <summary>跑一次确认过滤器，返回拦截结果（放行时为 <c>null</c>）。</summary>
    /// <param name="operation">被保护的操作。</param>
    /// <param name="headerValue">确认头的值；<c>null</c> 表示不带。</param>
    /// <returns>拦截结果或 <c>null</c>。</returns>
    private static IActionResult RunConfirm(AdminOperation operation, String headerValue)
    {
        var http = new DefaultHttpContext();
        if (headerValue != null) http.Request.Headers[RequireConfirmAttribute.HeaderName] = headerValue;

        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            []);

        new RequireConfirmAttribute(operation).OnAuthorizationAsync(context).GetAwaiter().GetResult();

        return context.Result;
    }

    /// <summary>跑一次审计过滤器（成功 / 失败 / 抛异常都走同一条路径）。</summary>
    /// <param name="store">审计存储替身。</param>
    /// <param name="action">审计动作名。</param>
    /// <param name="body">响应体；<c>null</c> 表示没有结果对象。</param>
    /// <param name="routeId">路由里的 <c>id</c>；<c>null</c> 表示无目标主键。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="exception">动作抛出的异常。</param>
    private static void RunAudit(FakeAuditStore store, String action, ApiResponse body, String routeId = null, Int32 statusCode = 200, Exception exception = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuditStore>(store)
            .AddSingleton<IAdminIdentityAccessor>(new FixedIdentity(new AdminIdentity { Name = "tester", Role = AdminRole.Admin }))
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.9");
        http.TraceIdentifier = "trace-1";

        var route = new RouteData();
        if (routeId != null) route.Values["id"] = routeId;

        var actionContext = new ActionContext(http, route, new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<String, Object>(), controller: null);

        ActionExecutionDelegate next = () => Task.FromResult(new ActionExecutedContext(actionContext, [], controller: null)
        {
            Result = body == null ? null : new ObjectResult(body) { StatusCode = statusCode },
            Exception = exception,
        });

        new AuditAttribute(action).OnActionExecutionAsync(context, next).GetAwaiter().GetResult();
    }

    /// <summary>记录写入内容的审计存储替身。</summary>
    private sealed class FakeAuditStore : IAuditStore
    {
        public List<AuditRecord> Written { get; } = [];

        public void Write(AuditRecord record) => Written.Add(record);

        public AuditPage Query(AuditQuery query) => new() { Items = Written, Total = Written.Count, Page = 1, PageSize = 20 };
    }

    /// <summary>固定身份的访问器替身。</summary>
    private sealed class FixedIdentity(AdminIdentity identity) : IAdminIdentityAccessor
    {
        public AdminIdentity Get() => identity;
    }
}
