using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>设备台账：现场设备的基础信息与启用状态。</summary>
[AdminArea]
[Menu(40, true, Icon = "fa-microchip")]
[DisplayName("设备台账")]
[Description("现场设备的基础信息、最后上报时间与启用状态。")]
public class DeviceController : EntityController<Device, Device>
{
    static DeviceController() => ListFields.RemoveField("ExtJson");
}
