using NewLife.Log;

namespace EFBM.Web.Services;

/// <summary>字段回溯回填的命令行入口：<c>dotnet EFBM.Web.dll --backfill-fields [天数]</c>（FLD-2c）。</summary>
/// <remarks>
/// 为什么需要命令行：回填是全量扫描，跑在 Web 进程里会占住线程与连接，
/// 而它又必须能重跑（中途失败、分批推进）。命令行跑完即退出，不影响正在接收的上报。
///
/// 不传天数表示不限时间范围；传入天数则只扫最近 N 天的报文。
/// 单次处理条数受 <see cref="FieldBackfillService.MaxBatchSize"/> 限制，未处理完可重复执行。
/// </remarks>
public static class FieldBackfillRunner
{
    /// <summary>执行回填并输出统计。</summary>
    /// <param name="serviceProvider">宿主服务提供者。</param>
    /// <param name="days">只回填最近多少天；<c>≤0</c> 表示不限。</param>
    /// <returns>异步任务。</returns>
    public static async Task RunAsync(IServiceProvider serviceProvider, Int32 days)
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

        var service = serviceProvider.GetRequiredService<FieldBackfillService>();
        var request = new BackfillRequest
        {
            From = days > 0 ? DateTime.UtcNow.AddDays(-days) : null,
            Limit = FieldBackfillService.MaxBatchSize,
        };

        XTrace.WriteLine("字段回溯回填开始：范围={0}", request.From.HasValue ? $"最近 {days} 天" : "全部历史");

        var result = await service.BackfillAsync(request, "cli", CancellationToken.None).ConfigureAwait(false);

        if (!result.Success)
        {
            Console.WriteLine($"回填未执行：{result.Reason}");
            return;
        }

        Console.WriteLine($"回填完成：字段={String.Join(",", result.Fields)} 扫描={result.Scanned} 回填={result.Updated} 失败={result.Failed}");
        if (result.Truncated)
            Console.WriteLine("本次已达单次上限，可再次执行以继续推进。");
    }
}
