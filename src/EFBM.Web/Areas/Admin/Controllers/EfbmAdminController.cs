using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using XCode.Membership;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>EFBM 后台自定义页面的公共基类：操作人、二次确认、结果提示。</summary>
/// <remarks>
/// 只收敛「每个写操作都要重复一遍」的三件小事，不接管权限：
/// 权限依旧由各动作上的 <see cref="EntityAuthorizeAttribute"/> 声明，菜单由魔方按控制器生成。
/// 二次确认也不是权限——它防的是「手滑点错」，与「有没有权限」正交，所以放在这里而不放进特性。
/// </remarks>
public abstract class EfbmAdminController : ControllerBaseX
{
    /// <summary>二次确认请求头名。</summary>
    public const String ConfirmHeader = "X-Confirm";

    /// <summary>操作人：优先取魔方当前登录用户，取不到时记 unknown。</summary>
    /// <remarks>审计日志里写 <c>unknown</c> 比写空白强：至少能看出「这条没取到登录信息」。</remarks>
    protected String OperatorName => ManageProvider.User?.Name is { Length: > 0 } name ? name : "unknown";

    /// <summary>检查二次确认头。</summary>
    /// <param name="action">动作名，需与请求头值完全一致。</param>
    /// <returns>一致时返回 <c>true</c>。</returns>
    protected Boolean Confirmed(String action)
        => String.Equals(Request.Headers[ConfirmHeader].ToString(), action, StringComparison.Ordinal);

    /// <summary>确认头缺失时的提示。</summary>
    /// <param name="actionName">动作的中文名。</param>
    /// <returns>面向运维的提示，含正确写法。</returns>
    protected static String ConfirmHint(String actionName)
        => $"缺少二次确认：请在请求头带上 {ConfirmHeader}: <动作名> 再执行{actionName}";
}
