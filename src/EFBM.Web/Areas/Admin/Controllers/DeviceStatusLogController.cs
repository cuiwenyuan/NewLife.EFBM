using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>状态流水：设备状态变化的历史记录，只读。</summary>
[AdminArea]
[Menu(90, true, Icon = "fa-history")]
[DisplayName("状态流水")]
[Description("设备状态变化的历史流水，按事件时间倒序。")]
public class DeviceStatusLogController : ReadOnlyEntityController<DeviceStatusLog>
{
    static DeviceStatusLogController() => ListFields.RemoveField("ExtJson");
}
