using EFBM.Web.Models;
using EFBM.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>后台管理接口的公共基类：统一响应体、统一取操作人。</summary>
/// <remarks>
/// 后台接口沿用上报端点冻结的 <c>{code, message, data}</c> 结构（<see cref="ApiResponse"/>），
/// 前端与运维脚本只需记一套约定。
///
/// <b>登录与权限都在动作过滤器里判</b>（<see cref="RequireAdminOperationAttribute"/>），不用 <c>[Authorize]</c>：
/// Cube 的登录用户要到 <c>UseCube</c> 内部才还原，而授权中间件必须早于 <c>UseCube</c>（D-30），
/// 中间件层永远看到未认证请求。过滤器阶段已经能拿到真实用户，401 / 403 也都在那里按统一信封返回。
/// </remarks>
[ApiController]
public abstract class AdminControllerBase : ControllerBase
{
    private readonly IAdminIdentityAccessor _identity;

    /// <summary>构造后台接口基类。</summary>
    /// <param name="identity">当前请求的身份访问器。</param>
    protected AdminControllerBase(IAdminIdentityAccessor identity)
        => _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    /// <summary>当前登录身份（登录名 + 角色）；未登录时 <see cref="AdminIdentity.IsAuthenticated"/> 为 <c>false</c>。</summary>
    protected AdminIdentity Identity => _identity.Get();

    /// <summary>当前登录用户，未知时返回 <c>unknown</c>（用于审计留痕）。</summary>
    protected String OperatorName => Identity.Name is { Length: > 0 } name ? name : "unknown";

    /// <summary>成功响应（HTTP 200，<c>code=0</c>）。</summary>
    /// <param name="data">返回数据。</param>
    /// <returns>统一结构响应。</returns>
    protected static ObjectResult Success(Object data) => new(new ApiResponse
    {
        Code = 0,
        Message = "ok",
        Data = data,
    })
    { StatusCode = 200 };

    /// <summary>失败响应：<c>code</c> 与 HTTP 状态码一致、<c>data=null</c>。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="message">失败原因（面向运维，不含密钥）。</param>
    /// <returns>统一结构响应。</returns>
    protected static ObjectResult Failure(Int32 statusCode, String message) => new(new ApiResponse
    {
        Code = statusCode,
        Message = message,
        Data = null,
    })
    { StatusCode = statusCode };
}
