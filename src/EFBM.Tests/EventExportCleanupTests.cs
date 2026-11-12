using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,xcode,testing-strategy] | skills=[xcode-data-modeling,testing-strategy] | agent=default

/// <summary>导出与手动清理（WEB-6d / WEB-6e）及按范围批量重放（STR-4b）的集成测试。</summary>
/// <remarks>
/// 真实 SQLite + 真实存储实现 + 假队列。核心不变量：
/// ① 清理<b>必须凭导出凭证</b>，无凭证 / 凭证过期 / 文件被改动一律拒绝（FR-15「先导出再删」）；
/// ② 清理只删导出过的那部分——导出后新到的数据不能被顺手删掉；
/// ③ 批量重放每条都带 <c>replayOf</c> 且 <c>eventKey</c> 不变，Redis 关闭时整体拒绝。
///
/// 与 sqlite collection 串行；类内共享一张表，<b>每个用例用互不重叠的时间窗（不同年份）与唯一 deviceId</b>
/// 隔离，避免清理用例误删其他用例的数据。
/// </remarks>
[Collection("sqlite")]
public sealed class EventExportCleanupTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-export");
    private static readonly String _exportDir = TempSqlite.CreateDirectory("efbm-export-out");

    private readonly EfbmOptions _options = new()
    {
        RedisEnabled = true,
        ExportDirectory = _exportDir,
        ExportTicketMinutes = 30,
    };

    private DateTime _now = new(2026, 11, 12, 18, 0, 0);

    /// <summary>独立 SQLite 文件 + 导出目录。</summary>
    public EventExportCleanupTests()
    {
        DAL.AddConnStr("EFBM", $"Data Source={_dbFile};Provider=SQLite", null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        // XCode 对「已检查过表结构」有进程级缓存，同 collection 里别的类删库后必须显式重建
        DAL.Create("EFBM").CheckTables();
    }

    private EventExportService NewExport(EfbmOptions options = null) => new(options ?? _options, () => _now);

    // ───────────────────────── 导出 ─────────────────────────

    [Fact(DisplayName = "导出_生成CSV_表头与数据行齐全")]
    public async Task Export_WritesHeaderAndRows()
    {
        var day = new DateTime(2020, 1, 1, 10, 0, 0);
        await SeedAsync("ex-h1", day, """{"event":"ORDER_COMPLETED","data":{"completedQty":3}}""");
        await SeedAsync("ex-h2", day.AddMinutes(1), """{"event":"ORDER_PROGRESS"}""");

        var result = await NewExport().ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        Assert.True(result.Success);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(64, result.Sha256.Length);
        Assert.True(File.Exists(result.FilePath));

        var lines = await File.ReadAllLinesAsync(result.FilePath);
        Assert.Equal(3, lines.Length);                       // 表头 + 2 行
        Assert.StartsWith("Id,", lines[0]);
        Assert.Contains("EventKey", lines[0]);
        Assert.Contains("RawJson", lines[0]);
        Assert.Contains("ex-h1", lines[1] + lines[2]);
    }

    [Fact(DisplayName = "导出_文件带UTF8BOM_Excel直接打开不乱码")]
    public async Task Export_WritesUtf8Bom()
    {
        var day = new DateTime(2020, 2, 1, 10, 0, 0);
        await SeedAsync("ex-bom", day, """{"desc":"中文测试"}""");

        var result = await NewExport().ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        var bytes = await File.ReadAllBytesAsync(result.FilePath);
        Assert.True(bytes.Length > 3);
        Assert.Equal(new Byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact(DisplayName = "导出_CSV转义_引号逗号换行都可还原")]
    public async Task Export_EscapesSpecialCharacters()
    {
        var day = new DateTime(2020, 3, 1, 10, 0, 0);
        var raw = "{\"note\":\"a,b\",\"quote\":\"he said \\\"hi\\\"\",\"multi\":\"l1\nl2\"}";
        await SeedAsync("ex-esc", day, raw);

        var result = await NewExport().ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        var text = await File.ReadAllTextAsync(result.FilePath);
        var rows = ParseCsv(text);

        Assert.Equal(2, rows.Count);                          // 表头 + 1 行（换行被引号包住，不拆行）
        Assert.Equal(22, rows[0].Count);
        Assert.Equal(22, rows[1].Count);

        // 最强校验：CSV 转义必须能逐字还原，逗号 / 引号 / 换行都不丢不改
        Assert.Equal(raw, rows[1][18]);                       // RawJson 列
        Assert.Contains("a,b", rows[1][18]);
        Assert.Contains("l1\nl2", rows[1][18]);
    }

    [Fact(DisplayName = "导出_范围内无数据_返回失败")]
    public async Task Export_NoData_Fails()
    {
        var result = await NewExport().ExportAsync(new DateTime(2019, 1, 1), new DateTime(2019, 1, 2), "tester");

        Assert.False(result.Success);
        Assert.Contains("没有数据", result.Reason);
    }

    [Fact(DisplayName = "导出_范围倒挂_返回失败")]
    public async Task Export_ReversedRange_Fails()
    {
        var result = await NewExport().ExportAsync(new DateTime(2020, 5, 2), new DateTime(2020, 5, 1), "tester");

        Assert.False(result.Success);
        Assert.Contains("不能晚于", result.Reason);
    }

    // ───────────────────────── 清理 ─────────────────────────

    [Fact(DisplayName = "清理_无凭证_拒绝删除")]
    public async Task Cleanup_NoTicket_Rejected()
    {
        var result = await NewExport().CleanupAsync(null, "tester");

        Assert.False(result.Success);
        Assert.Contains("导出凭证", result.Reason);
    }

    [Fact(DisplayName = "清理_凭证不存在_拒绝删除")]
    public async Task Cleanup_UnknownTicket_Rejected()
    {
        var result = await NewExport().CleanupAsync("not-a-ticket", "tester");

        Assert.False(result.Success);
        Assert.Contains("导出凭证", result.Reason);
    }

    [Fact(DisplayName = "清理_成功删除范围内已导出数据")]
    public async Task Cleanup_DeletesExportedRange()
    {
        var day = new DateTime(2020, 6, 1, 10, 0, 0);
        await SeedAsync("cl-1", day, "{}");
        await SeedAsync("cl-2", day.AddMinutes(1), "{}");

        var service = NewExport();
        var export = await service.ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");
        Assert.True(export.Success);

        var cleanup = await service.CleanupAsync(export.TicketId, "tester");

        Assert.True(cleanup.Success);
        Assert.Equal(2, cleanup.DeletedCount);
        Assert.Equal(2, cleanup.ExpectedCount);
        Assert.Empty(DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == "cl-1" | DeviceEventRaw._.EventKey == "cl-2"));
    }

    [Fact(DisplayName = "清理_只删导出过的部分_导出后新数据保留")]
    public async Task Cleanup_KeepsRowsArrivedAfterExport()
    {
        var day = new DateTime(2020, 7, 1, 10, 0, 0);
        await SeedAsync("cl-before", day, "{}");

        var service = NewExport();
        var export = await service.ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        // 导出之后新到一条、且落在同一时间窗内：清理不能把它顺手删掉
        await SeedAsync("cl-after", day.AddMinutes(5), "{}");

        var cleanup = await service.CleanupAsync(export.TicketId, "tester");

        Assert.True(cleanup.Success);
        Assert.Equal(1, cleanup.DeletedCount);
        Assert.Empty(DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == "cl-before"));
        Assert.Single(DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == "cl-after"));
    }

    [Fact(DisplayName = "清理_导出文件被改动_拒绝删除")]
    public async Task Cleanup_TamperedFile_Rejected()
    {
        var day = new DateTime(2020, 8, 1, 10, 0, 0);
        await SeedAsync("cl-tamper", day, "{}");

        var service = NewExport();
        var export = await service.ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        await File.AppendAllTextAsync(export.FilePath, "\"999\",\"injected\"\n");

        var cleanup = await service.CleanupAsync(export.TicketId, "tester");

        Assert.False(cleanup.Success);
        Assert.Contains("已被修改", cleanup.Reason);
        Assert.Single(DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == "cl-tamper"));
    }

    [Fact(DisplayName = "清理_凭证过期_拒绝删除")]
    public async Task Cleanup_ExpiredTicket_Rejected()
    {
        var day = new DateTime(2020, 9, 1, 10, 0, 0);
        await SeedAsync("cl-expire", day, "{}");

        var service = NewExport();
        var export = await service.ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        _now = _now.AddMinutes(31);   // 超过 30 分钟有效期

        var cleanup = await service.CleanupAsync(export.TicketId, "tester");

        Assert.False(cleanup.Success);
        Assert.Contains("已过期", cleanup.Reason);
        Assert.Single(DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == "cl-expire"));
    }

    [Fact(DisplayName = "清理_凭证一次性_重复清理被拒")]
    public async Task Cleanup_TicketIsSingleUse()
    {
        var day = new DateTime(2020, 10, 1, 10, 0, 0);
        await SeedAsync("cl-once", day, "{}");

        var service = NewExport();
        var export = await service.ExportAsync(day.AddHours(-1), day.AddHours(1), "tester");

        Assert.True((await service.CleanupAsync(export.TicketId, "tester")).Success);

        var again = await service.CleanupAsync(export.TicketId, "tester");
        Assert.False(again.Success);
        Assert.Contains("导出凭证", again.Reason);
    }

    // ───────────────────────── 批量重放 ─────────────────────────

    [Fact(DisplayName = "批量重放_Redis关闭_整体拒绝")]
    public async Task ReplayRange_RedisDisabled_Rejected()
    {
        var options = new EfbmOptions { RedisEnabled = false };
        var replay = new EventReplayService(options, new FakeEventQueue());

        var result = await replay.ReplayRangeAsync(new DateTime(2020, 11, 1), new DateTime(2020, 11, 2), 10, "tester");

        Assert.False(result.Success);
        Assert.Contains("Redis 未启用", result.Reason);
    }

    [Fact(DisplayName = "批量重放_按范围与上限_带replayOf且幂等键不变")]
    public async Task ReplayRange_ReplaysWithReplayOf()
    {
        var day = new DateTime(2020, 12, 1, 10, 0, 0);
        await SeedAsync("rp-a", day, """{"event":"ORDER_COMPLETED"}""");
        await SeedAsync("rp-b", day.AddMinutes(1), """{"event":"ORDER_COMPLETED"}""");
        await SeedAsync("rp-c", day.AddMinutes(2), """{"event":"ORDER_COMPLETED"}""");

        var queue = new FakeEventQueue();
        var replay = new EventReplayService(new EfbmOptions { RedisEnabled = true }, queue);

        var result = await replay.ReplayRangeAsync(day.AddHours(-1), day.AddHours(1), 2, "tester");

        Assert.True(result.Success);
        Assert.Equal(3, result.Matched);
        Assert.Equal(2, result.Attempted);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(result.Truncated);
        Assert.Equal(2, queue.Added.Count);

        // 原 eventKey 保留（消费端据此幂等），新 messageId 与原记录不同，replayOf 指向原消息
        var originals = DeviceEventRaw.FindAll(
            DeviceEventRaw._.EventKey == "rp-a" | DeviceEventRaw._.EventKey == "rp-b");
        foreach (var message in queue.Added)
        {
            var origin = originals.First(x => x.EventKey == message.EventKey);
            Assert.Equal(origin.MessageId, message.ReplayOf);
            Assert.NotEqual(origin.MessageId, message.MessageId);
            Assert.Equal(origin.RawJson, message.RawJson);
        }
    }

    [Fact(DisplayName = "批量重放_范围内无数据_成功且计数为0")]
    public async Task ReplayRange_EmptyRange_ReturnsZero()
    {
        var replay = new EventReplayService(new EfbmOptions { RedisEnabled = true }, new FakeEventQueue());

        var result = await replay.ReplayRangeAsync(new DateTime(2018, 1, 1), new DateTime(2018, 1, 2), 10, "tester");

        Assert.True(result.Success);
        Assert.Equal(0, result.Matched);
        Assert.Equal(0, result.Attempted);
    }

    [Fact(DisplayName = "批量重放_范围倒挂_拒绝")]
    public async Task ReplayRange_Reversed_Fails()
    {
        var replay = new EventReplayService(new EfbmOptions { RedisEnabled = true }, new FakeEventQueue());

        var result = await replay.ReplayRangeAsync(new DateTime(2021, 1, 2), new DateTime(2021, 1, 1), 10, "tester");

        Assert.False(result.Success);
        Assert.Contains("不能晚于", result.Reason);
    }

    // ───────────────────────── DI ─────────────────────────

    [Fact(DisplayName = "后台DI_导出服务可解析")]
    public void DependencyInjection_ResolvesExportService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(_options);
        services.AddSingleton<IEventQueue>(new FakeEventQueue());
        services.AddEFBMAdmin();

        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);

        Assert.NotNull(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<EventExportService>(provider));
    }

    // ───────────────────────── 辅助 ─────────────────────────

    /// <summary>插入一条已落库的原始报文。</summary>
    private static async Task SeedAsync(String eventKey, DateTime receivedAt, String rawJson)
    {
        var store = new EventRawStore();
        await store.InsertAsync(new RawEventRecord
        {
            EventKey = eventKey,
            MessageId = Guid.NewGuid().ToString("D"),
            Event = "ORDER_COMPLETED",
            DeviceId = "FBOX-EX",
            EventTimeRaw = receivedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            EventTime = receivedAt,
            ReceivedAt = receivedAt,
            RawJson = rawJson,
            DataJson = "{}",
            ParseVersion = EfbmDefaults.ParseVersion,
            ProcessStatus = RawProcessStatus.Persisted,
        });
    }

    /// <summary>最小 RFC 4180 解析：支持引号包裹、双写转义与字段内换行。</summary>
    private static List<List<String>> ParseCsv(String text)
    {
        var rows = new List<List<String>>();
        var row = new List<String>();
        var field = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);

                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<String>();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
