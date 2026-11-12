using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>工单：生产工单的计划数与完成数。</summary>
[AdminArea]
[Menu(70, true, Icon = "fa-tasks")]
[DisplayName("生产工单")]
[Description("生产工单的计划数、完成数与状态，数据来自设备上报。")]
public class WorkOrderController : ReadOnlyEntityController<WorkOrder>
{
    static WorkOrderController() => ListFields.RemoveField("ExtJson");
}
