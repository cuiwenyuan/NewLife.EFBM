using EFBM.Web.Models;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Web.Security;

/// <summary>把一次后台写操作记进审计日志（FR-15 / AUTH-3b）。</summary>
/// <remarks>
/// 挂在动作上而不是写进 Service：审计关心的是「谁、从哪、通过哪个接口、对哪条数据、结果如何」，
/// 这些信息只有 HTTP 层有（登录名、来源 IP、追踪 ID、路由主键）。业务层拿不到，也不该为审计改签名。
///
/// 无论成功失败都记一条：成功记结果摘要（删除条数 / 新消息 ID），失败记原因——
/// 「操作失败」本身也是运维要看的证据。审计写失败不会影响业务（<see cref="IAuditStore.Write"/> 内部吞异常）。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>动作中文名，写进审计的 <c>Action</c>。</summary>
    public String Action { get; }

    /// <summary>指定动作名。</summary>
    /// <param name="action">中文动作名，如「清理原始报文」。</param>
    public AuditAttribute(String action) => Action = action;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (next == null) throw new ArgumentNullException(nameof(next));

        var executed = await next().ConfigureAwait(false);

        var services = context.HttpContext.RequestServices;
        var store = services?.GetService(typeof(IAuditStore)) as IAuditStore;
        if (store == null) return;

        var identity = (services?.GetService(typeof(IAdminIdentityAccessor)) as IAdminIdentityAccessor)?.Get();
        var (success, summary) = Describe(executed);

        store.Write(new AuditRecord
        {
            Action = Action,
            TargetId = TargetId(context),
            Success = success,
            Operator = identity?.Name ?? "unknown",
            Ip = RemoteIp(context.HttpContext),
            TraceId = context.HttpContext.TraceIdentifier,
            Summary = summary,
            Remark = Parameters(context),
            Time = DateTime.Now,
        });
    }

    /// <summary>从执行结果判断成败并取结果摘要。</summary>
    /// <param name="executed">动作执行结果。</param>
    /// <returns>是否成功 + 摘要。</returns>
    private static (Boolean Success, String Summary) Describe(ActionExecutedContext executed)
    {
        if (executed.Exception != null && !executed.ExceptionHandled)
            return (false, $"异常：{executed.Exception.GetType().Name}");

        if (executed.Result is ObjectResult { Value: ApiResponse response })
        {
            var ok = response.Code == 0;
            return (ok, ok ? CubeAuditStore.Summarize(response.Data) : response.Message);
        }

        if (executed.Result is ObjectResult obj)
            return (obj.StatusCode is null or < 400, CubeAuditStore.Summarize(obj.Value));

        return (true, null);
    }

    /// <summary>取目标主键：优先路由 <c>id</c>，其次请求体里的 <c>id</c>。</summary>
    /// <param name="context">动作上下文。</param>
    /// <returns>主键或 <c>null</c>。</returns>
    private static Int64? TargetId(ActionExecutingContext context)
    {
        if (context.RouteData.Values.TryGetValue("id", out var raw)
            && Int64.TryParse(raw?.ToString(), out var id) && id > 0)
            return id;

        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg == null) continue;
            var prop = arg.GetType().GetProperty("Id");
            if (prop?.GetValue(arg) is Int64 value && value > 0) return value;
        }

        return null;
    }

    /// <summary>把动作参数压成一行摘要（时间范围、条数上限、凭证号等），绝不写密钥。</summary>
    /// <param name="context">动作上下文。</param>
    /// <returns>参数摘要。</returns>
    private static String Parameters(ActionExecutingContext context)
    {
        if (context.ActionArguments.Count == 0) return null;

        // 逐个参数序列化：直接把整个字典丢给序列化器只会得到 "Dictionary`2"（object 值无法内联）
        var parts = context.ActionArguments
            .Where(kv => kv.Value != null && kv.Value is not System.Threading.CancellationToken)
            .Select(kv => $"{kv.Key}={CubeAuditStore.Summarize(kv.Value)}");

        return String.Join("; ", parts);
    }

    /// <summary>取来源 IP（反向代理场景已由框架的 ForwardedHeaders 还原）。</summary>
    /// <param name="http">HTTP 上下文。</param>
    /// <returns>IP 文本或 <c>null</c>。</returns>
    private static String RemoteIp(Microsoft.AspNetCore.Http.HttpContext http)
        => http?.Connection.RemoteIpAddress?.ToString();
}
