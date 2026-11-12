using System.ComponentModel;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;
using XCode.Membership;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>故障处置：未关闭故障列表与「手动关闭」。</summary>
/// <remarks>
/// 与只读的「故障记录」页分工：那边查历史与导出，这边只处理<b>还没闭环</b>的故障。
/// 设备恢复事件 <c>FAULT_CLEARED</c> 可能永远不来（断电、掉网、设备报废），
/// 没有手动关闭这些故障会一直挂在「未清除」里，把现场告警淹掉。
///
/// 权限仍然完全走魔方：打开页面要 <c>Detail</c>，关闭要 <c>Update</c>，删除按钮不提供。
/// 关闭前要求请求头 <c>X-Confirm: Close</c>，并由服务端强制校验关闭原因必填。
/// </remarks>
[AdminArea]
[Menu(81, true, Icon = "fa-medkit")]
[DisplayName("故障处置")]
[Description("未关闭故障列表与手动关闭；关闭只改状态与清除时间，不改设备上报的事实字段。")]
public class FaultDisposalController : EfbmAdminController
{
    private readonly FaultDisposalService _service;

    /// <summary>构造故障处置页控制器。</summary>
    /// <param name="service">故障处置服务。</param>
    public FaultDisposalController(FaultDisposalService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>未关闭故障列表。</summary>
    /// <param name="deviceId">设备编号过滤，可空。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>页面视图。</returns>
    [EntityAuthorize(PermissionFlags.Detail)]
    public async Task<ActionResult> Index(String deviceId, Int32 page, CancellationToken cancellationToken)
    {
        var result = await _service.QueryOpenAsync(deviceId, page, 20, cancellationToken);

        ViewBag.DeviceId = deviceId;

        return View(result);
    }

    /// <summary>手动关闭一条故障。</summary>
    /// <param name="id">故障主键。</param>
    /// <param name="remark">关闭原因，必填并写进审计。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关闭结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> Close(Int64 id, String remark, CancellationToken cancellationToken)
    {
        if (id <= 0) return Json(400, "故障 ID 必须大于 0");
        if (!Confirmed("Close")) return Json(400, ConfirmHint("手动关闭故障"));

        var result = await _service.CloseAsync(id, remark, OperatorName, cancellationToken);

        AdminAudit.Write(this, "手动关闭故障", result.Success,
            result.Success ? $"设备 {result.Fault?.DeviceId}；原因：{remark}" : result.Reason, id);

        return result.Success
            ? Json(0, $"已关闭（设备 {result.Fault?.DeviceId}）", new { id, result.Fault?.DeviceId })
            : Json(409, result.Reason);
    }
}
