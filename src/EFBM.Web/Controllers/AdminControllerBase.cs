using EFBM.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>后台管理接口的公共基类：统一响应体、统一取操作人。</summary>
/// <remarks>
/// 后台接口沿用上报端点冻结的 <c>{code, message, data}</c> 结构（<see cref="ApiResponse"/>），
/// 前端与运维脚本只需记一套约定。<b>与上报端点的区别是必须登录</b>：由 <c>[Authorize]</c> 走
/// Cube 的后台用户体系，设备凭据在这里不起作用。
/// </remarks>
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public abstract class AdminControllerBase : ControllerBase
{
    /// <summary>当前登录用户，未知时返回 <c>unknown</c>（用于审计留痕）。</summary>
    protected String OperatorName => User?.Identity?.Name is { Length: > 0 } name ? name : "unknown";

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
