using EFBM.Core.Security;
using EFBM.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EFBM.Web.Security;

/// <summary>声明式后台操作授权：标注动作需要的最低权限（FR-14）。</summary>
/// <remarks>
/// 与 <c>[Authorize]</c> 分工明确：<c>[Authorize]</c> 只保证「登录了」，本特性保证「这个角色能干这件事」。
/// 未登录仍是 401（由认证中间件处理并跳登录页），已登录但角色不够返回 403 与统一信封，
/// 页面据此显示「无权限」而不是「登录过期」。
///
/// 判定走 <see cref="AdminPermissionMatrix"/>，不在特性里写 if-else，
/// 这样矩阵改动一处，接口、页面、单测三处自动一致。
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

        var resolver = context.HttpContext.RequestServices?.GetService(typeof(IAdminRoleResolver)) as IAdminRoleResolver;
        var role = resolver?.Resolve(context.HttpContext.User);

        // 认不出角色就拒绝：宁可让运维去配别名，也不能「没配好就当管理员放行」
        if (role == null)
        {
            context.Result = Deny("当前账号未授予后台角色，请联系管理员分配「管理员 / 运维 / 只读」之一");
            return Task.CompletedTask;
        }

        if (!AdminPermissionMatrix.Allows(role.Value, Operation))
        {
            context.Result = Deny($"角色「{AdminRoleInfo.DisplayName(role.Value)}」无权{AdminOperationInfo.DisplayName(Operation)}");
        }

        return Task.CompletedTask;
    }

    /// <summary>403 + 统一信封（<c>code=403</c>、<c>data=null</c>）。</summary>
    /// <param name="message">拒绝原因。</param>
    /// <returns>结果对象。</returns>
    private static ObjectResult Deny(String message) => new(new ApiResponse
    {
        Code = 403,
        Message = message,
        Data = null,
    })
    { StatusCode = 403 };
}
