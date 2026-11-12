using EFBM.Core.Security;
using EFBM.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EFBM.Web.Security;

/// <summary>敏感操作的二次确认（FR-15）：请求头必须带 <c>X-Confirm: &lt;动作名&gt;</c>。</summary>
/// <remarks>
/// 光靠页面弹窗不算「强制」——脚本、旧版本页面、误触的刷新都能绕过前端直接打接口。
/// 这里在服务端要求一个<b>表达意图</b>的头部：值必须等于该操作的枚举名（如 <c>CleanupEvents</c>），
/// 而不是随便一个 <c>true</c>。这样日志里能看出「这次调用是有意确认过的」，
/// 也挡住「按钮没做确认、直接 POST」的实现偷懒。
///
/// 缺头部返回 400 而不是 403：它不是权限问题，是请求没带确认。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireConfirmAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>确认头的名称。</summary>
    public const String HeaderName = "X-Confirm";

    /// <summary>本动作要求的操作（同时是确认头应有的值）。</summary>
    public AdminOperation Operation { get; }

    /// <summary>指定动作要求的操作。</summary>
    /// <param name="operation">操作；确认头的值必须是它的枚举名。</param>
    public RequireConfirmAttribute(AdminOperation operation) => Operation = operation;

    /// <summary>排在角色判定之后执行（角色不够时不泄露「要确认什么」）。</summary>
    public Int32 Order => 100;

    /// <inheritdoc />
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var value = context.HttpContext.Request.Headers[HeaderName].ToString().Trim();
        if (String.Equals(value, Operation.ToString(), StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;

        context.Result = new ObjectResult(new ApiResponse
        {
            Code = 400,
            Message = $"敏感操作需要二次确认：请求头 {HeaderName} 必须为 {Operation}（{AdminOperationInfo.DisplayName(Operation)}）",
            Data = null,
        })
        { StatusCode = 400 };

        return Task.CompletedTask;
    }
}
