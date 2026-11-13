using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>字段固化回溯回填（FLD-2c，真实 SQLite）。</summary>
/// <remarks>
/// 回填是唯一会批量改写历史 <c>ExtJson</c> 的操作，必须钉住三条不变量：
/// 只补空缺、不改原始报文、每次执行留审计。否则「补历史」很容易变成「改历史」。
/// </remarks>
[Collection("sqlite")]
public sealed class FieldBackfillServiceTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-backfill");

    private readonly FieldBackfillService _service = new();

    /// <summary>独立 SQLite 文件；显式建表绕开 XCode 的进程级表结构缓存。</summary>
    public FieldBackfillServiceTests()
    {
        DAL.AddConnStr("EFBM", ConnectionStringTuning.Normalize($"Data Source={_dbFile};Provider=SQLite"), null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("EFBM").CheckTables();
    }

    [Fact(DisplayName = "回填_把原文里的字段补进 ExtJson")]
    public async Task Backfill_MergesFromRaw()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, "{}", "{\"boxType\":\"A7\",\"operator\":\"张三\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName }, "tester");

        Assert.True(result.Success);
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.Contains("boxType", saved.ExtJson);
        Assert.Contains("A7", saved.ExtJson);

        // 只补登记过的字段，未登记的 operator 不动：回填不是「把所有字段都搬一遍」
        Assert.DoesNotContain("operator", saved.ExtJson);

        // 原始报文一个字都不能变
        Assert.Equal(raw.RawJson, saved.RawJson);
    }

    [Fact(DisplayName = "回填_不覆盖 ExtJson 里已有的值")]
    public async Task Backfill_DoesNotOverwrite()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, "{\"boxType\":\"KEEP\"}", "{\"boxType\":\"NEW\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName }, "tester");

        Assert.True(result.Success);
        // 行计数为 1：ExtJson 里的 boxType 已存在不重写，但空的固化列 BoxType 会从 DataJson 补值，
        // 固化列回填命中也算一次更新（2026-11-13 字段固化演练引入固化列赋值后口径扩展）
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.Contains("KEEP", saved.ExtJson);
        Assert.DoesNotContain("NEW", saved.ExtJson);
    }

    [Fact(DisplayName = "回填_试运行只统计不写库")]
    public async Task Backfill_DryRun()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, null, "{\"boxType\":\"B1\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName, DryRun = true }, "tester");

        Assert.True(result.DryRun);
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.True(String.IsNullOrEmpty(saved.ExtJson), "试运行不应写库");
    }

    [Fact(DisplayName = "回填_没有已固化字段时明确拒绝")]
    public async Task Backfill_NoFields()
    {
        var result = await _service.BackfillAsync(new BackfillRequest { Event = "NO_SUCH_EVENT_" + Guid.NewGuid().ToString("N")[..6] }, "tester");

        Assert.False(result.Success);
        Assert.Contains("没有可回填的字段", result.Reason);
    }

    [Fact(DisplayName = "回填_可指定单个字段_即使未标记已固化")]
    public async Task Backfill_ExplicitField()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = "ORDER_COMPLETED";
        var raw = SeedRaw(eventName, null, "{\"customField\":\"X9\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName, FieldName = "customField" }, "tester");

        Assert.True(result.Success);
        Assert.Equal(["customField"], result.Fields);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.Contains("X9", saved.ExtJson);
        Assert.Equal(1, result.Updated);
    }

    [Fact(DisplayName = "回填_超过单次上限标记截断")]
    public async Task Backfill_Truncated()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        SeedRaw(eventName, null, "{\"boxType\":\"A\"}");
        SeedRaw(eventName, null, "{\"boxType\":\"B\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName, Limit = 1 }, "tester");

        Assert.True(result.Truncated);
        Assert.Equal(1, result.Scanned);
    }

    [Fact(DisplayName = "回填_时间区间倒挂直接拒绝")]
    public async Task Backfill_InvertedRange()
    {
        var result = await _service.BackfillAsync(new BackfillRequest
        {
            FieldName = "boxType",
            From = new DateTime(2026, 11, 12),
            To = new DateTime(2026, 11, 11),
        }, "tester");

        Assert.False(result.Success);
        Assert.Contains("不能晚于", result.Reason);
    }

    [Fact(DisplayName = "回填_报文里没有该字段时跳过")]
    public async Task Backfill_FieldAbsent()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        SeedRaw(eventName, null, "{\"other\":1}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName }, "tester");

        Assert.True(result.Success);
        Assert.Equal(0, result.Updated);
    }

    [Fact(DisplayName = "回填_boxType 固化列从原文补出历史值")]
    public async Task Backfill_BoxTypeColumnFilled()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, null, "{\"boxType\":\"A7\"}");
        Assert.True(String.IsNullOrEmpty(raw.BoxType));

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName }, "tester");

        Assert.True(result.Success);
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.Equal("A7", saved.BoxType);
    }

    [Fact(DisplayName = "回填_固化列已有值时不覆盖")]
    public async Task Backfill_BoxTypeColumnNotOverwritten()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, null, "{\"boxType\":\"NEW\"}");
        var preset = DeviceEventRaw.FindById(raw.Id);
        preset.BoxType = "KEEP";
        preset.Update();

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName }, "tester");

        Assert.True(result.Success);
        // 行计数为 1：固化列 BoxType 已有值不覆盖，但 ExtJson 缺 boxType 键会被补上
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.Equal("KEEP", saved.BoxType);
    }

    [Fact(DisplayName = "回填_试运行不写固化列")]
    public async Task Backfill_DryRunDoesNotWriteBoxType()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var eventName = RegisterField(tag, "boxType");
        var raw = SeedRaw(eventName, null, "{\"boxType\":\"A7\"}");

        var result = await _service.BackfillAsync(new BackfillRequest { Event = eventName, DryRun = true }, "tester");

        Assert.True(result.Success);
        Assert.Equal(1, result.Updated);

        var saved = DeviceEventRaw.FindById(raw.Id);
        Assert.True(String.IsNullOrEmpty(saved.BoxType), "试运行不应写固化列");
        Assert.True(String.IsNullOrEmpty(saved.ExtJson), "试运行不应写 ExtJson");
    }

    /// <summary>登记一个「已固化」字段；事件名带随机后缀，避免用例之间互相影响。</summary>
    private static String RegisterField(String tag, String fieldName)
    {
        var eventName = "ORDER_COMPLETED_" + tag.ToUpperInvariant();

        new FieldDict
        {
            Event = eventName,
            FieldName = fieldName,
            DisplayName = "盒型",
            JsonType = "String",
            Solidified = true,
        }.Insert();

        return eventName;
    }

    /// <summary>插入一条原始报文。</summary>
    private static DeviceEventRaw SeedRaw(String eventName, String extJson, String dataJson)
    {
        var time = DateTime.Now;
        var entity = new DeviceEventRaw
        {
            EventKey = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("D"),
            StreamId = "1-0",
            Event = eventName,
            DeviceId = "FBOX-BF-" + Guid.NewGuid().ToString("N")[..6],
            EventTimeRaw = time.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            EventTime = time,
            RawJson = "{\"event\":\"" + eventName + "\",\"data\":" + dataJson + "}",
            DataJson = dataJson,
            ExtJson = extJson,
            UnknownFields = "boxType",
            ParseVersion = EfbmDefaults.ParseVersion,
            SourceIp = "127.0.0.1",
            ReceivedAt = time,
            ProcessStatus = RawProcessStatus.Persisted,
            PersistedAt = time,
            RepeatCount = 0,
        };

        entity.Insert();
        return entity;
    }
}
