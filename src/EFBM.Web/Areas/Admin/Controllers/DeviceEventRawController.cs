using System.ComponentModel;
using EFBM.Data.Entities;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>原始报文：设备上报的原文，只读。</summary>
/// <remarks>
/// 页面完全由魔方内置的列表 / 详情模板渲染，权限走 <see cref="EntityAuthorizeAttribute"/>：
/// 列表与详情要求 <c>Detail</c>，导出沿用同一权限位。报文由设备产生，页面不提供新增与编辑；
/// 标记处理状态、重放与清理统一放在「数据维护」页（需要 <c>Update</c> / <c>Delete</c>）。
/// </remarks>
[AdminArea]
[Menu(10, true, Icon = "fa-file-text-o")]
[DisplayName("原始报文")]
[Description("设备上报的原始报文，只读；支持按设备、事件类型、处理状态与接收时间检索。")]
public class DeviceEventRawController : ReadOnlyEntityController<DeviceEventRaw>
{
    static DeviceEventRawController()
    {
        // 列表只放定位用的列，原文 / 解析结果这类大字段进详情页再看，避免一页几 MB
        ListFields.RemoveField("RawJson", "DataJson", "ExtJson", "UnknownFields", "ParseWarning", "ReplayOf");
        ListFields.RemoveField("SourceIp", "PersistedAt", "HandleRemark", "HandleBy", "HandleAt");
    }
}
