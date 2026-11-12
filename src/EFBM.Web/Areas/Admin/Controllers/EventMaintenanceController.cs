using System.ComponentModel;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;
using XCode.Membership;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>数据维护：重放、按范围补数、标记处理状态、凭证式导出与清理。</summary>
/// <remarks>
/// 这是后台唯一手写的页面，权限仍然走魔方的 <see cref="EntityAuthorizeAttribute"/>：
/// 打开页面要 <c>Detail</c>，重放 / 标记要 <c>Update</c>，清理要 <c>Delete</c>。
/// 菜单由魔方启动时按控制器自动生成，角色在「系统管理 → 角色」里勾权限项即可。
///
/// 二次确认在服务端强制：所有写操作都要求请求头 <c>X-Confirm</c> 等于动作名，
/// 页面上的确认弹窗只是配合它，脚本绕过弹窗也绕不过服务端。
/// </remarks>
[AdminArea]
[Menu(100, true, Icon = "fa-tools")]
[DisplayName("数据维护")]
[Description("重放报文 / 按范围补数 / 标记处理状态，以及凭证式导出与清理。")]
public class EventMaintenanceController : ControllerBaseX
{
    private readonly EventReplayService _replay;
    private readonly EventExportService _export;

    /// <summary>构造维护页控制器。</summary>
    /// <param name="replay">重放服务。</param>
    /// <param name="export">导出与清理服务。</param>
    public EventMaintenanceController(EventReplayService replay, EventExportService export)
    {
        _replay = replay;
        _export = export;
    }

    /// <summary>操作人：优先取魔方当前登录用户，取不到时记 unknown。</summary>
    private String OperatorName => ManageProvider.User?.Name is { Length: > 0 } name ? name : "unknown";

    /// <summary>维护页。</summary>
    /// <returns>页面视图。</returns>
    [EntityAuthorize(PermissionFlags.Detail)]
    public ActionResult Index() => View();

    /// <summary>重放单条报文。</summary>
    /// <param name="id">报文主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重放结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> ReplayEvent(Int64 id, CancellationToken cancellationToken)
    {
        if (id <= 0) return Json(400, "报文 ID 必须大于 0");
        if (!Confirmed("ReplayEvent")) return Json(400, ConfirmHint("重放报文"));

        var result = await _replay.ReplayEventAsync(id, OperatorName, cancellationToken);
        AdminAudit.Write(this, "重放原始报文", result.Success, result.Success
            ? $"新消息 {result.MessageId}，原消息 {result.ReplayOf}"
            : result.Reason, id);

        return result.Success ? Json(0, $"已重放，新消息 {result.MessageId}", result) : Json(409, result.Reason);
    }

    /// <summary>按时间范围批量重放。</summary>
    /// <param name="from">起始接收时间。</param>
    /// <param name="to">结束接收时间。</param>
    /// <param name="limit">本次上限，0 表示默认 500。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>批量重放结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> ReplayRange(DateTime from, DateTime to, Int32 limit, CancellationToken cancellationToken)
    {
        if (from <= DateTime.MinValue || to <= DateTime.MinValue) return Json(400, "起止时间必填");
        if (from > to) return Json(400, "起始时间不能晚于结束时间");
        if (!Confirmed("ReplayRange")) return Json(400, ConfirmHint("按范围批量重放"));

        var result = await _replay.ReplayRangeAsync(from, to, limit, OperatorName, cancellationToken);
        AdminAudit.Write(this, "按范围批量重放", result.Success,
            result.Success ? $"命中 {result.Matched}，成功 {result.Succeeded}，失败 {result.Failed}" : result.Reason);

        return result.Success ? Json(0, $"成功 {result.Succeeded} 条，失败 {result.Failed} 条", result) : Json(409, result.Reason);
    }

    /// <summary>重放死信。</summary>
    /// <param name="id">死信主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重放结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> ReplayDeadLetter(Int64 id, CancellationToken cancellationToken)
    {
        if (id <= 0) return Json(400, "死信 ID 必须大于 0");
        if (!Confirmed("ReplayDeadLetter")) return Json(400, ConfirmHint("重放死信"));

        var result = await _replay.ReplayDeadLetterAsync(id, OperatorName, cancellationToken);
        AdminAudit.Write(this, "重放死信", result.Success,
            result.Success ? $"新消息 {result.MessageId}" : result.Reason, id);

        return result.Success ? Json(0, $"已重放，新消息 {result.MessageId}", result) : Json(409, result.Reason);
    }

    /// <summary>标记报文的处理状态。</summary>
    /// <param name="id">报文主键。</param>
    /// <param name="status">处理状态值。</param>
    /// <param name="remark">处理备注。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标记结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> Mark(Int64 id, Int32 status, String remark, CancellationToken cancellationToken)
    {
        if (id <= 0) return Json(400, "报文 ID 必须大于 0");

        var ok = await _replay.MarkAsync(id, status, remark, OperatorName, cancellationToken);
        AdminAudit.Write(this, "标记处理状态", ok, ok ? $"状态 {status}，备注 {remark}" : "报文不存在", id);

        return ok ? Json(0, "已标记", new { id, status }) : Json(404, "报文不存在");
    }

    /// <summary>导出指定范围的报文，返回清理凭证。</summary>
    /// <param name="from">起始接收时间。</param>
    /// <param name="to">结束接收时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出结果（含凭证与文件路径）。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Detail)]
    public async Task<ActionResult> Export(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (from <= DateTime.MinValue || to <= DateTime.MinValue) return Json(400, "起止时间必填");
        if (from > to) return Json(400, "起始时间不能晚于结束时间");

        var result = await _export.ExportAsync(from, to, OperatorName, cancellationToken);
        AdminAudit.Write(this, "导出原始报文", result.Success,
            result.Success ? $"导出 {result.RowCount} 条，凭证 {result.TicketId}" : result.Reason);

        return result.Success ? Json(0, $"已导出 {result.RowCount} 条", result) : Json(409, result.Reason);
    }

    /// <summary>下载导出文件。</summary>
    /// <param name="ticketId">导出凭证。</param>
    /// <returns>CSV 文件；凭证无效时 404。</returns>
    [HttpGet]
    [EntityAuthorize(PermissionFlags.Detail)]
    public ActionResult Download(String ticketId)
    {
        var ticket = _export.FindTicket(ticketId);
        if (ticket == null || !System.IO.File.Exists(ticket.FilePath)) return NotFound("导出凭证无效或文件已被移走");

        return PhysicalFile(ticket.FilePath, "text/csv", Path.GetFileName(ticket.FilePath));
    }

    /// <summary>凭导出凭证清理报文。</summary>
    /// <param name="ticketId">导出凭证。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清理结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Delete)]
    public async Task<ActionResult> Cleanup(String ticketId, CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(ticketId)) return Json(400, "必须提供导出凭证");
        if (!Confirmed("Cleanup")) return Json(400, ConfirmHint("清理原始报文"));

        var result = await _export.CleanupAsync(ticketId, OperatorName, cancellationToken);
        AdminAudit.Write(this, "清理原始报文", result.Success,
            result.Success ? $"删除 {result.DeletedCount} 条（凭证记录 {result.ExpectedCount} 条）" : result.Reason);

        return result.Success
            ? Json(0, $"已删除 {result.DeletedCount} 条", result)
            : Json(409, result.Reason);
    }

    /// <summary>检查二次确认头。</summary>
    /// <param name="action">动作名。</param>
    /// <returns>请求头 <c>X-Confirm</c> 与动作名一致时返回 <c>true</c>。</returns>
    private Boolean Confirmed(String action)
        => String.Equals(Request.Headers["X-Confirm"].ToString(), action, StringComparison.Ordinal);

    /// <summary>确认头缺失时的提示。</summary>
    /// <param name="actionName">动作的中文名。</param>
    /// <returns>面向运维的提示。</returns>
    private static String ConfirmHint(String actionName)
        => $"缺少二次确认：请在请求头带上 X-Confirm: <动作名> 再执行{actionName}";
}
