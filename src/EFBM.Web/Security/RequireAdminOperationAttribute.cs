using EFBM.Core.Security;
using EFBM.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Web.Security;

/// <summary>声明式后台授权：标注动作需要的最低权限，同时负责「是否登录」（FR-14）。</summary>
/// <remarks>
/// 401 与 403 都在这里返回，不再用 <c>[Authorize]</c>：Cube 的登录用户要到 <c>UseCube</c> 的中间件里
/// 才被还原，而授权中间件必须早于 <c>UseCube</c>（D-30），所以中间件层永远看到「未认证」。
/// 动作过滤器在端点执行前运行，此时 Cube 的中间件已经跑完，能从 <see cref="IAdminIdentityAccessor"/>
/// 拿到真实用户。详见 <see cref="CubeAdminIdentityAccessor"/> 的说明。
///
/// 判定顺序：未登录 → 401（提示去登录）；登录了但没角色 / 角色不够 → 403（说明缺哪项权限）。
/// 两者都返回 <c>{code,message,data}</c> 信封，与上报端点一致。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAdminOperationAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>本动作要求的操作。</summary>
    public AdminOperation Operation { get; }

    /// <summary>指定动作要求的操作。</summary>
    /// <param name="operation">操作（对应权限矩阵里的一行）。</param>
    public RequireAdminOperationAttribute(AdminOperation operation) => Operation = operation;

    /// <inheritdoc />
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var accessor = context.HttpContext.RequestServices?.GetService(typeof(IAdminIdentityAccessor)) as IAdminIdentityAccessor;
        var identity = accessor?.Get() ?? new AdminIdentity();

        if (!identity.IsAuthenticated)
        {
            context.Result = Failure(401, "未登录或登录已过期，请先登录后台账号");
            return Task.CompletedTask;
        }

        // 认不出角色就拒绝：宁可让运维去配别名，也不能「没配好就当管理员放行」
        if (identity.Role == null)
        {
            context.Result = Failure(403, $"账号「{identity.Name}」未授予后台角色，请联系管理员分配「管理员 / 运维 / 只读」之一");
            return Task.CompletedTask;
        }

        if (!AdminPermissionMatrix.Allows(identity.Role.Value, Operation))
        {
            context.Result = Failure(403, $"角色「{AdminRoleInfo.DisplayName(identity.Role.Value)}」无权{AdminOperationInfo.DisplayName(Operation)}");
        }

        return Task.CompletedTask;
    }

    /// <summary>失败 + 统一信封（<c>code</c> 等于 HTTP 状态码、<c>data=null</c>）。</summary>
    /// <param name="statusCode">HTTP 状态码（401 / 403）。</param>
    /// <param name="message">给运维看的原因。</param>
    /// <returns>结果对象。</returns>
    private static ObjectResult Failure(Int32 statusCode, String message) => new(new ApiResponse
    {
        Code = statusCode,
        Message = message,
        Data = null,
    })
    { StatusCode = statusCode };
}
