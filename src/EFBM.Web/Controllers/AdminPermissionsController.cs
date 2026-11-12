using EFBM.Core.Security;
using EFBM.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace EFBM.Web.Controllers;

/// <summary>当前登录账号的后台权限（FR-14）：页面的菜单与按钮据此收敛。</summary>
/// <remarks>
/// 只要求登录、不要求具体操作权限——认不出角色的账号也要能拿到 <c>role=null</c> 的结果，
/// 页面才能显示「未授权」而不是把 403 当成登录过期。
/// 返回 <c>matrix</c> 全量矩阵，是为了让页面与《Beta 运行手册》的权限表永远同源。
/// </remarks>
[Route("api/admin/permissions")]
public sealed class AdminPermissionsController : AdminControllerBase
{
    private readonly IAdminRoleResolver _roles;

    /// <summary>构造权限查询接口。</summary>
    /// <param name="roles">角色解析器。</param>
    public AdminPermissionsController(IAdminRoleResolver roles)
        => _roles = roles ?? throw new ArgumentNullException(nameof(roles));

    /// <summary>读取当前账号的角色、可执行操作与完整权限矩阵。</summary>
    /// <returns>角色、允许的操作、全量矩阵与每个操作的最低角色。</returns>
    [HttpGet]
    public ObjectResult Get()
    {
        var role = _roles.Resolve(User);

        return Success(new
        {
            user = OperatorName,
            role = role == null ? null : AdminRoleInfo.Name(role.Value),
            roleName = role == null ? "未授权" : AdminRoleInfo.DisplayName(role.Value),
            allowed = role == null ? Array.Empty<String>() : Array.ConvertAll(AdminPermissionMatrix.AllowedFor(role.Value), op => op.ToString()),
            matrix = AdminPermissionMatrix.Describe(),
            operations = Array.ConvertAll(Enum.GetValues<AdminOperation>(), op =>
            {
                var min = AdminPermissionMatrix.MinimumRole(op);
                return new
                {
                    name = op.ToString(),
                    displayName = AdminOperationInfo.DisplayName(op),
                    minRole = min == null ? null : AdminRoleInfo.Name(min.Value),
                    minRoleName = min == null ? "未授权" : AdminRoleInfo.DisplayName(min.Value),
                };
            }),
        });
    }
}
