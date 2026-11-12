using System.ComponentModel;
using EFBM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;
using XCode.Membership;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>字段演进：字段字典、新增字段监测与现场补登（FR-27、FR-28 入口）。</summary>
/// <remarks>
/// 只做三件事：看代码字典里有什么、看报文里冒出哪些没登记的字段、把没登记的补上中文名与类型。
/// 补登<b>不会</b>自动建列，固化仍然要走「改 Model.xml → 重新生成 → 回溯回填」的正式流程（需求说明 §3.7），
/// 页面上的「是否已固化」只是给人看的标记，不影响解析。
/// </remarks>
[AdminArea]
[Menu(102, true, Icon = "fa-sitemap")]
[DisplayName("字段演进")]
[Description("已登记字段、报文中出现但未登记的新字段（按事件分组、首次出现、样例值），以及现场补登。")]
public class FieldEvolutionController : EfbmAdminController
{
    private readonly FieldEvolutionService _service;
    private readonly FieldBackfillService _backfill;

    /// <summary>构造字段演进页控制器。</summary>
    /// <param name="service">字段演进服务。</param>
    /// <param name="backfill">字段回溯回填服务。</param>
    public FieldEvolutionController(FieldEvolutionService service, FieldBackfillService backfill)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _backfill = backfill ?? throw new ArgumentNullException(nameof(backfill));
    }

    /// <summary>字段字典与新增字段监测页。</summary>
    /// <param name="days">统计窗口天数，默认 7 天。</param>
    /// <param name="maxRows">扫描条数上限，默认 2000。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>页面视图。</returns>
    [EntityAuthorize(PermissionFlags.Detail)]
    public async Task<ActionResult> Index(Int32 days, Int32 maxRows, CancellationToken cancellationToken)
    {
        ViewBag.JsonTypes = FieldEvolutionService.JsonTypes;

        return View(await _service.ScanAsync(days, maxRows, cancellationToken));
    }

    /// <summary>登记或修改一个字段的中文名 / 类型 / 所属事件 / 是否已固化。</summary>
    /// <param name="id">登记表主键；0 表示新增。</param>
    /// <param name="eventName">所属事件；空表示信封字段。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="displayName">中文名。</param>
    /// <param name="jsonType">JSON 类型。</param>
    /// <param name="solidified">是否已固化为正式列。</param>
    /// <param name="remark">备注。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> Register(Int32 id, String eventName, String fieldName, String displayName,
        String jsonType, Boolean solidified, String remark, CancellationToken cancellationToken)
    {
        if (!Confirmed("Register")) return Json(400, ConfirmHint("登记字段"));

        var result = await _service.RegisterAsync(id, eventName, fieldName, displayName, jsonType, solidified, remark, OperatorName, cancellationToken);

        AdminAudit.Write(this, "登记字段字典", result.Success,
            result.Success
                ? $"{FieldEvolutionService.DescribeEvent(eventName)}.{fieldName} = {displayName}（{jsonType}，固化={solidified}）"
                : result.Reason,
            result.Dict?.Id);

        return result.Success
            ? Json(0, "已登记", new { id = result.Dict?.Id })
            : Json(409, result.Reason);
    }

    /// <summary>对已固化字段执行历史回溯回填（FLD-2c）。</summary>
    /// <param name="eventName">只回填某个事件；为空表示全部事件。</param>
    /// <param name="fieldName">只回填某个字段；为空表示登记表里所有已固化字段。</param>
    /// <param name="days">只回填最近多少天；<c>≤0</c> 表示不限。</param>
    /// <param name="dryRun">是否试运行（只统计不写库）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回填结果。</returns>
    /// <remarks>数据量大时单次会受上限截断，返回里 <c>truncated=true</c> 表示可再点一次继续推进。</remarks>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Update)]
    public async Task<ActionResult> Backfill(String eventName, String fieldName, Int32 days, Boolean dryRun, CancellationToken cancellationToken)
    {
        if (!Confirmed("Backfill")) return Json(400, ConfirmHint("回溯回填"));

        var result = await _backfill.BackfillAsync(new BackfillRequest
        {
            Event = eventName,
            FieldName = fieldName,
            From = days > 0 ? DateTime.UtcNow.AddDays(-days) : null,
            DryRun = dryRun,
        }, OperatorName, cancellationToken).ConfigureAwait(false);

        if (!result.Success) return Json(409, result.Reason);

        var message = $"扫描 {result.Scanned} 条，回填 {result.Updated} 条" + (result.Truncated ? "（已达单次上限，可再次执行）" : String.Empty);
        return Json(0, dryRun ? $"试运行：{message}" : message, result);
    }

    /// <summary>删除一条字段登记。</summary>
    /// <param name="id">登记表主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    [EntityAuthorize(PermissionFlags.Delete)]
    public async Task<ActionResult> Delete(Int32 id, CancellationToken cancellationToken)
    {
        if (id <= 0) return Json(400, "登记 ID 必须大于 0");
        if (!Confirmed("Delete")) return Json(400, ConfirmHint("删除字段登记"));

        var ok = await _service.DeleteAsync(id, OperatorName, cancellationToken);
        AdminAudit.Write(this, "删除字段登记", ok, ok ? $"登记 ID {id}" : "记录不存在", id);

        return ok ? Json(0, "已删除") : Json(404, "登记记录不存在");
    }
}
