using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>接入凭据：Beta 阶段设备上报用的 AppKey / AppSecret。</summary>
/// <remarks>
/// 列表不显示 <c>AppSecret</c>，只在编辑表单里可见——后台列表可能被投屏或截图，
/// 密钥不该出现在上面。权限位与其它实体一致：查看 / 新增 / 修改 / 删除。
/// </remarks>
[AdminArea]
[Menu(50, true, Icon = "fa-key")]
[DisplayName("接入凭据")]
[Description("设备上报使用的 AppKey / AppSecret。新增后请把密钥抄给现场，后台列表不展示密钥。")]
public class AppCredentialController : EntityController<AppCredential, AppCredential>
{
    static AppCredentialController() => ListFields.RemoveField("AppSecret");
}
