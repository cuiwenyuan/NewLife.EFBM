using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using EFBM.Web.Areas.Efbm;

namespace EFBM.Web.Areas.Efbm.Controllers;

/// <summary>状态字典：设备状态码与中文名对照，后台可直接维护。</summary>
/// <remarks>新增 / 修改 / 删除分别要求 <c>Insert</c> / <c>Update</c> / <c>Delete</c> 权限位。</remarks>
[EfbmArea]
[Menu(30, true, Icon = "fa-book")]
[DisplayName("状态字典")]
[Description("设备状态码与显示名对照。设备上报的状态码在此维护，页面与接口按此翻译。")]
public class StatusDictController : EntityController<StatusDict, StatusDict>
{
}
