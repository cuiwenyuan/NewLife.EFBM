using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>死信：超过重试上限后被移入死信队列的报文，只读。</summary>
/// <remarks>死信重放在「数据维护」页统一处理（需要 <c>Update</c> 权限）。</remarks>
[AdminArea]
[Menu(20, true, Icon = "fa-exclamation-triangle")]
[DisplayName("死信队列")]
[Description("超过重试上限的报文。原文保留在此，排查后可重放回队列。")]
public class DeadLetterController : ReadOnlyEntityController<DeadLetter>
{
    static DeadLetterController()
    {
        ListFields.RemoveField("RawJson");
    }
}
