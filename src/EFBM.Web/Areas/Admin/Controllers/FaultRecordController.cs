using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>故障记录：设备上报的故障与恢复。</summary>
[AdminArea]
[Menu(80, true, Icon = "fa-wrench")]
[DisplayName("故障记录")]
[Description("设备上报的故障与恢复记录，未恢复的故障 <c>ClearedAt</c> 为空。")]
public class FaultRecordController : ReadOnlyEntityController<FaultRecord>
{
    static FaultRecordController() => ListFields.RemoveField("ExtJson");
}
