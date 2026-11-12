using System.ComponentModel;
using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>消费组登记：外部系统接入共享流时的组名、责任人与最后出现时间（WEB-9b、STR-2a）。</summary>
/// <remarks>
/// 组名是跨系统长期标识，规则由 <see cref="ConsumerGroupRules"/> 在实体层强制：
/// 必须是 <c>efbm-&lt;系统名&gt;</c>、小写、全局唯一，且<b>不允许删除</b>（只能停用）。
/// 页面上不提供「自动登记」：看到未登记的组时，由运维确认归属后手工登记。
/// </remarks>
[AdminArea]
[Menu(103, true, Icon = "fa-users")]
[DisplayName("消费组登记")]
[Description("外部系统接入共享流时的消费组名与责任人。组名格式 efbm-系统名，登记后不得删除或复用。")]
public class ConsumerGroupController : EntityController<ConsumerGroup, ConsumerGroup>
{
    static ConsumerGroupController() => ListFields.RemoveField("Remark");
}
