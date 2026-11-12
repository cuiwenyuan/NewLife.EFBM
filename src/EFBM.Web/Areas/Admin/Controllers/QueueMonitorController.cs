using System.ComponentModel;
using EFBM.Core;
using EFBM.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.Cube.Areas.Admin;
using XCode.Membership;

namespace EFBM.Web.Areas.Admin.Controllers;

/// <summary>消费组监控：Stream 长度、各组 lag / PEL / 消费者、最慢组与僵尸组、裁剪水位。</summary>
/// <remarks>
/// 只读页面（<c>Detail</c> 权限），所有数据来自 <c>XINFO</c> / <c>XPENDING</c>，不建组、不确认、不裁剪。
/// Redis 不可用时页面照常打开并显示错误摘要——运维最先要知道的就是「连不上」这件事。
/// </remarks>
[AdminArea]
[Menu(101, true, Icon = "fa-tachometer")]
[DisplayName("消费组监控")]
[Description("共享上报流的长度、各组积压与未确认消息、消费者空闲情况、最慢组与僵尸组、安全裁剪水位。")]
public class QueueMonitorController : EfbmAdminController
{
    private readonly IQueueMetricsProbe _probe;
    private readonly EfbmOptions _options;

    /// <summary>构造监控页控制器。</summary>
    /// <param name="probe">队列指标探测。</param>
    /// <param name="options">EFBM 配置节。</param>
    public QueueMonitorController(IQueueMetricsProbe probe, EfbmOptions options)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>监控页。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>页面视图。</returns>
    [EntityAuthorize(PermissionFlags.Detail)]
    public async Task<ActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.SafeTrimEnabled = _options.SafeTrimEnabled;
        ViewBag.SafeTrimIntervalSeconds = _options.SafeTrimIntervalSeconds;
        ViewBag.SafeTrimMinRetentionDays = _options.SafeTrimMinRetentionDays;

        var metrics = await LoadAsync(cancellationToken);

        // 登记信息按组名索引，视图里逐行显示「这是谁家的组、有没有登记」
        var groups = ConsumerGroup.FindAll().ToDictionary(e => e.Name, StringComparer.Ordinal);
        ViewBag.Registry = groups;

        return View(metrics);
    }

    /// <summary>指标快照（页面自动刷新用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><c>{code,message,data}</c> 结构的 JSON。</returns>
    [EntityAuthorize(PermissionFlags.Detail)]
    public async Task<ActionResult> Snapshot(CancellationToken cancellationToken)
        => Json(0, "ok", await LoadAsync(cancellationToken));

    /// <summary>取指标并回写已登记组的「最后出现时间」。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指标快照。</returns>
    /// <remarks>
    /// 回写只在页面被打开时发生（10 秒一次），且实体内部限制每分钟最多写一次，
    /// 不会把「看一眼监控」变成持续写库。未登记的组不自动建记录，只在页面上提示。
    /// </remarks>
    private async Task<QueueMetrics> LoadAsync(CancellationToken cancellationToken)
    {
        var metrics = await _probe.GetMetricsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ViewBag.UnregisteredGroups = ConsumerGroup.TouchSeen(metrics.Groups.Select(e => e.Name));
        }
        catch (Exception ex)
        {
            // 登记表读不出来不能影响监控本身
            NewLife.Log.XTrace.WriteLine("回写消费组最后出现时间失败：{0}: {1}", ex.GetType().Name, ex.Message);
            ViewBag.UnregisteredGroups = Array.Empty<String>();
        }

        return metrics;
    }
}
