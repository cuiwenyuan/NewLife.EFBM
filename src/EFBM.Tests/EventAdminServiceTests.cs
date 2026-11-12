using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,xcode,testing-strategy] | skills=[xcode-data-modeling,testing-strategy] | agent=default

/// <summary>后台管理接口的集成测试：真实 SQLite + 真实存储实现（P4 验收）。</summary>
/// <remarks>
/// 覆盖功能清单 WEB-6a（检索 / 原文）、WEB-6c（标记与重放）、CON-6a（死信重放）的后端部分：
/// 多条件过滤、分页与总数、原文详情、手动标记、重放生成新 <c>MessageId</c> 且 <c>replayOf</c> 指向原消息、
/// <c>eventKey</c> 与原文逐字不变、Redis 关闭时拒绝重放、死信重放成功后回写 <c>ReplayStatus</c>。
///
/// 与 <see cref="SqliteSmokeTests"/> / <see cref="PersistPipelineTests"/> 同属 sqlite collection 必须串行；
/// 类内共享一张表（xUnit 每个测试方法重建类实例，逐方法换文件会撞 XCode 静态建表缓存），
/// <b>每个用例用独立 deviceId / eventKey 隔离</b>，断言不依赖其他用例是否先跑过。
/// </remarks>
[Collection("sqlite")]
public sealed class EventAdminServiceTests
{
    private static readonly String _dbFile = Path.Combine(Path.GetTempPath(), $"efbm-admin-{Guid.NewGuid():N}.db");

    private readonly EventQueryService _query = new();
    private readonly FakeEventQueue _queue = new();
    private readonly EfbmOptions _options = new() { RedisEnabled = true };
    private readonly EventReplayService _replay;

    /// <summary>独立 SQLite 文件 + 假队列。</summary>
    public EventAdminServiceTests()
    {
        DAL.AddConnStr("EFBM", $"Data Source={_dbFile};Provider=SQLite", null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        // 显式建表：XCode 对「已检查过表结构」有进程级缓存，而 sqlite collection 里其它用例会删掉
        // 自己的临时库文件，缓存命中时会跳过建表，导致本类新文件上出现 "no such table"。
        DAL.Create("EFBM").CheckTables();

        _replay = new EventReplayService(_options, _queue);
    }

    /// <summary>插入一条原始报文，返回实体。</summary>
    private static DeviceEventRaw Seed(String eventKey, String deviceId = "FBOX-A01", String eventName = "ORDER_COMPLETED",
        Int32 processStatus = RawProcessStatus.Persisted, DateTime? receivedAt = null, String rawJson = null)
    {
        var time = receivedAt ?? new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Local);

        var entity = new DeviceEventRaw
        {
            EventKey = eventKey,
            MessageId = Guid.NewGuid().ToString("D"),
            StreamId = "1-0",
            Event = eventName,
            DeviceId = deviceId,
            EventTimeRaw = "2026-11-12T09:00:00+08:00",
            EventTime = time,
            RawJson = rawJson ?? $"{{\"event\":\"{eventName}\",\"deviceId\":\"{deviceId}\",\"timestamp\":\"2026-11-12T09:00:00+08:00\",\"data\":{{\"completedQty\":1}}}}",
            DataJson = "{\"completedQty\":1}",
            ExtJson = null,
            UnknownFields = "{\"newField\":\"abc\"}",
            ParseVersion = EfbmDefaults.ParseVersion,
            ParseWarning = null,
            SourceIp = "127.0.0.1",
            ReceivedAt = time,
            ProcessStatus = processStatus,
            PersistedAt = processStatus == RawProcessStatus.Persisted ? time : EfbmDefaults.UnrequeuedAt,
            RepeatCount = 0,
        };

        entity.Insert();
        return entity;
    }

    [Fact(DisplayName = "后台检索_按设备与事件过滤_只返回命中记录")]
    public async Task Query_ByDeviceAndEvent_Filters()
    {
        Seed("k-1", "FBOX-F1", EventNames.OrderStarted);
        Seed("k-2", "FBOX-F2", EventNames.OrderStarted);
        Seed("k-3", "FBOX-F1", EventNames.OrderCompleted);

        var result = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-F1", Event = EventNames.OrderStarted });

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("k-1", item.EventKey);
        Assert.Equal("已落库", item.ProcessStatusText);
    }

    [Fact(DisplayName = "后台检索_按接收时间区间过滤_边界含两端")]
    public async Task Query_ByTimeRange_Inclusive()
    {
        Seed("t-1", "FBOX-T", receivedAt: new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Local));
        Seed("t-2", "FBOX-T", receivedAt: new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Local));
        Seed("t-3", "FBOX-T", receivedAt: new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Local));

        var result = await _query.QueryAsync(new EventQuery
        {
            DeviceId = "FBOX-T",
            From = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Local),
            To = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Local),
        });

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, x => x.EventKey == "t-1");
        Assert.Contains(result.Items, x => x.EventKey == "t-2");
        Assert.DoesNotContain(result.Items, x => x.EventKey == "t-3");
    }

    [Fact(DisplayName = "后台检索_时间区间倒挂_返回空集")]
    public async Task Query_FromAfterTo_ReturnsEmpty()
    {
        Seed("r-1", "FBOX-R");

        var result = await _query.QueryAsync(new EventQuery
        {
            DeviceId = "FBOX-R",
            From = new DateTime(2026, 11, 13),
            To = new DateTime(2026, 11, 12),
        });

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact(DisplayName = "后台检索_分页与总数_第二页取剩余记录且不重叠")]
    public async Task Query_Paging_ReturnsSecondPage()
    {
        for (var i = 0; i < 5; i++) Seed($"p-{i}", deviceId: "FBOX-PAGE");

        var first = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-PAGE", Page = 1, PageSize = 2 });
        var second = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-PAGE", Page = 2, PageSize = 2 });

        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(2, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));

        // 主键倒序：第一页是最新的两条
        Assert.True(first.Items[0].Id > first.Items[1].Id);
    }

    [Fact(DisplayName = "后台检索_每页条数超上限_被压到 200")]
    public async Task Query_PageSize_Clamped()
    {
        Seed("c-1", deviceId: "FBOX-CLAMP");

        var result = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-CLAMP", Page = 1, PageSize = 5000 });

        Assert.Equal(EventQueryService.MaxPageSize, result.PageSize);
    }

    [Fact(DisplayName = "后台检索_按处理状态过滤_只返回死信记录")]
    public async Task Query_ByProcessStatus_FiltersDead()
    {
        Seed("s-1", "FBOX-DEAD", processStatus: RawProcessStatus.Dead);
        Seed("s-2", "FBOX-DEAD", processStatus: RawProcessStatus.Persisted);

        var result = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-DEAD", ProcessStatus = RawProcessStatus.Dead });

        Assert.Equal(1, result.Total);
        Assert.Equal("s-1", result.Items[0].EventKey);
        Assert.Equal("死信", result.Items[0].ProcessStatusText);
    }

    [Fact(DisplayName = "后台检索_按关键词匹配原文_命中 data 内字段")]
    public async Task Query_ByKeyword_MatchesRawJson()
    {
        Seed("w-1", "FBOX-KW", rawJson: "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-KW\",\"data\":{\"orderId\":\"WO-KW-9\"}}");
        Seed("w-2", "FBOX-KW", rawJson: "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-KW\",\"data\":{\"orderId\":\"WO-OTHER\"}}");

        var result = await _query.QueryAsync(new EventQuery { DeviceId = "FBOX-KW", Keyword = "WO-KW-9" });

        Assert.Equal(1, result.Total);
        Assert.Equal("w-1", result.Items[0].EventKey);
    }

    [Fact(DisplayName = "后台详情_返回原文与未识别字段_列表不含原文")]
    public async Task Get_ReturnsRawAndUnknownFields()
    {
        var entity = Seed("d-1", "FBOX-D");

        var detail = await _query.GetAsync(entity.Id);

        Assert.NotNull(detail);
        Assert.Contains("ORDER_COMPLETED", detail.RawJson);
        Assert.Contains("newField", detail.UnknownFields);
        Assert.Equal(entity.EventKey, detail.Item.EventKey);
        Assert.Equal(entity.RawJson, detail.RawJson);
    }

    [Fact(DisplayName = "后台详情_记录不存在_返回空")]
    public async Task Get_Missing_ReturnsNull()
    {
        Assert.Null(await _query.GetAsync(999999));
    }

    [Fact(DisplayName = "后台标记_写入处理人与备注_不改动报文内容")]
    public async Task Mark_WritesAuditFields()
    {
        var entity = Seed("m-1", "FBOX-M");
        var rawBefore = entity.RawJson;

        var ok = await _replay.MarkAsync(entity.Id, 1, "已人工核对", "tester");

        Assert.True(ok);
        var after = DeviceEventRaw.FindById(entity.Id);
        Assert.Equal(1, after.HandleStatus);
        Assert.Equal("已人工核对", after.HandleRemark);
        Assert.Equal("tester", after.HandleBy);
        Assert.Equal(rawBefore, after.RawJson);
    }

    [Fact(DisplayName = "后台标记_记录不存在_返回失败")]
    public async Task Mark_Missing_ReturnsFalse()
    {
        Assert.False(await _replay.MarkAsync(999999, 1, null, "tester"));
    }

    [Fact(DisplayName = "后台重放_生成新消息ID并带replayOf_幂等键与原文不变")]
    public async Task Replay_NewMessageIdKeepsEventKeyAndRaw()
    {
        var entity = Seed("rp-1", "FBOX-RP");

        var result = await _replay.ReplayEventAsync(entity.Id, "tester");

        Assert.True(result.Success);
        var message = Assert.Single(_queue.Added);
        Assert.NotEqual(entity.MessageId, message.MessageId);
        Assert.Equal(entity.MessageId, message.ReplayOf);
        Assert.Equal(entity.EventKey, message.EventKey);
        Assert.Equal(entity.RawJson, message.RawJson);
        Assert.Equal(entity.DeviceId, message.DeviceId);
        Assert.Equal("1-0", result.StreamId);
    }

    [Fact(DisplayName = "后台重放_已是重放记录_继续指向最初原消息")]
    public async Task Replay_OfReplay_KeepsOriginalChain()
    {
        var entity = Seed("rp-2", "FBOX-RP");
        entity.ReplayOf = "original-message-id";
        entity.Update();

        await _replay.ReplayEventAsync(entity.Id, "tester");

        var message = Assert.Single(_queue.Added);
        Assert.Equal("original-message-id", message.ReplayOf);
    }

    [Fact(DisplayName = "后台重放_Redis未启用_拒绝并提示改用日志补录")]
    public async Task Replay_RedisDisabled_Rejected()
    {
        var entity = Seed("rp-3", "FBOX-RP");
        _options.RedisEnabled = false;

        var result = await _replay.ReplayEventAsync(entity.Id, "tester");

        Assert.False(result.Success);
        Assert.Contains("--replay", result.Reason);
        Assert.Empty(_queue.Added);
    }

    [Fact(DisplayName = "后台重放_记录不存在_返回失败")]
    public async Task Replay_Missing_Rejected()
    {
        var result = await _replay.ReplayEventAsync(999999, "tester");

        Assert.False(result.Success);
        Assert.Equal("记录不存在", result.Reason);
    }

    [Fact(DisplayName = "后台重放_队列异常_如实失败且不谎报成功")]
    public async Task Replay_QueueFailure_ReportsError()
    {
        var entity = Seed("rp-4", "FBOX-RP");
        _queue.ThrowOnAdd = true;

        var result = await _replay.ReplayEventAsync(entity.Id, "tester");

        Assert.False(result.Success);
        Assert.Contains("redis down", result.Reason);
    }

    [Fact(DisplayName = "死信重放_还原消息体并回写重放状态")]
    public async Task ReplayDeadLetter_WritesBackStatus()
    {
        var original = new StreamMessage
        {
            MessageId = "dl-original",
            EventKey = "dl-1",
            Event = EventNames.OrderCompleted,
            DeviceId = "FBOX-A01",
            Timestamp = "2026-11-12T09:00:00+08:00",
            RawJson = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-11-12T09:00:00+08:00\",\"data\":{\"completedQty\":1}}",
            ReceivedAt = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Local),
            SourceIp = "127.0.0.1",
        };

        var dead = new DeadLetter
        {
            MessageId = original.MessageId,
            StreamId = "1-1",
            EventKey = original.EventKey,
            RawJson = EventIngestService.Serialize(original),
            RetryCount = 5,
            LastError = "db down",
            ReplayStatus = 0,
        };
        dead.Insert();

        var result = await _replay.ReplayDeadLetterAsync(dead.Id, "tester");

        Assert.True(result.Success);
        var message = Assert.Single(_queue.Added);
        Assert.Equal("dl-1", message.EventKey);
        Assert.Equal("dl-original", message.ReplayOf);
        Assert.Equal(original.RawJson, message.RawJson);

        var after = DeadLetter.FindById(dead.Id);
        Assert.Equal(1, after.ReplayStatus);
        Assert.Equal("tester", after.ReplayBy);
        Assert.NotEqual(DateTime.MinValue, after.ReplayAt);
    }

    [Fact(DisplayName = "死信重放_消息体损坏_拒绝且不标记已重放")]
    public async Task ReplayDeadLetter_BrokenPayload_Rejected()
    {
        var entity = new DeadLetter
        {
            MessageId = "dl-broken",
            StreamId = "1-2",
            EventKey = "dl-broken",
            RawJson = "{not-json",
            RetryCount = 5,
            LastError = "parse fail",
            ReplayStatus = 0,
        };
        entity.Insert();

        var result = await _replay.ReplayDeadLetterAsync(entity.Id, "tester");

        Assert.False(result.Success);
        Assert.Contains("损坏", result.Reason);
        Assert.Empty(_queue.Added);
        Assert.Equal(0, DeadLetter.FindById(entity.Id).ReplayStatus);
    }

    [Fact(DisplayName = "死信重放_记录不存在_返回失败")]
    public async Task ReplayDeadLetter_Missing_Rejected()
    {
        var result = await _replay.ReplayDeadLetterAsync(999999, "tester");

        Assert.False(result.Success);
        Assert.Equal("死信记录不存在", result.Reason);
    }

    [Fact(DisplayName = "后台DI_注册后可解析检索与重放服务")]
    public void DependencyInjection_ResolvesAdminServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddSingleton<IEventQueue>(_queue);
        services.AddEFBMAdmin();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<EventQueryService>());
        Assert.NotNull(provider.GetRequiredService<EventReplayService>());
    }

    [Fact(DisplayName = "死信查询_按重放状态过滤_命中指定记录")]
    public async Task QueryDeadLetters_ByReplayStatus()
    {
        new DeadLetter
        {
            MessageId = "dl-q1",
            StreamId = "2-1",
            EventKey = "dl-q1",
            RawJson = "{}",
            RetryCount = 3,
            LastError = "first",
            ReplayStatus = 0,
        }.Insert();

        new DeadLetter
        {
            MessageId = "dl-q2",
            StreamId = "2-2",
            EventKey = "dl-q2",
            RawJson = "{}",
            RetryCount = 4,
            LastError = "second",
            ReplayStatus = 1,
        }.Insert();

        var pending = await _query.QueryDeadLettersAsync(new DeadLetterQuery { ReplayStatus = 0 });

        var hit = Assert.Single(pending.Items, x => x.EventKey == "dl-q1");
        Assert.Equal("first", hit.LastError);
        Assert.DoesNotContain(pending.Items, x => x.EventKey == "dl-q2");

        var replayed = await _query.QueryDeadLettersAsync(new DeadLetterQuery { ReplayStatus = 1 });
        Assert.Contains(replayed.Items, x => x.EventKey == "dl-q2");
        Assert.DoesNotContain(replayed.Items, x => x.EventKey == "dl-q1");
    }
}
