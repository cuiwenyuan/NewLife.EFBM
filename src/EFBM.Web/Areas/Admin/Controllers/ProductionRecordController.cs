using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>生产记录：由消费链路落库的产量数据，只读。</summary>
[AdminArea]
[Menu(60, true, Icon = "fa-cubes")]
[DisplayName("生产记录")]
[Description("消费链路落库的产量数据，按事件时间排序；数据来自设备上报，页面只读。")]
public class ProductionRecordController : ReadOnlyEntityController<ProductionRecord>
{
    static ProductionRecordController() => ListFields.RemoveField("ExtJson");
}
