using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>故障处置：未关闭列表与手动关闭（真实 SQLite，WEB-5b）。</summary>
/// <remarks>
/// 手动关闭是唯一会绕过设备事实改故障状态的入口，必须逐条钉住：
/// 只能关「未清除」的、不能改发生时间与描述、必须填原因、重复关闭要挡住。
/// 与其它 sqlite 测试同属一个 collection 串行执行；每个用例用独立 deviceId / EventKey 隔离。
/// </remarks>
[Collection("sqlite")]
public sealed class FaultDisposalServiceTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-fault-close");

    private readonly FaultDisposalService _service = new();

    /// <summary>独立 SQLite 文件；显式建表绕开 XCode 的进程级表结构缓存。</summary>
    public FaultDisposalServiceTests()
    {
        DAL.AddConnStr("EFBM", ConnectionStringTuning.Normalize($"Data Source={_dbFile};Provider=SQLite"), null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("EFBM").CheckTables();
    }

    /// <summary>插入一条故障记录。</summary>
    private static FaultRecord Seed(String deviceId, Int32 status = FaultDisposalService.StatusOpen,
        String message = "急停被按下", DateTime? occurredAt = null)
    {
        var time = occurredAt ?? new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Local);

        var entity = new FaultRecord
        {
            EventKey = Guid.NewGuid().ToString("N"),
            DeviceId = deviceId,
            Message = message,
            OccurredAt = time,
            ClearedAt = EfbmDefaults.UnrequeuedAt,
            Status = status,
            ParseVersion = EfbmDefaults.ParseVersion,
        };

        entity.Insert();
        return entity;
    }

    [Fact(DisplayName = "故障处置_未关闭列表只含未清除且按发生时间正序")]
    public async Task QueryOpen_OnlyOpenAndOldestFirst()
    {
        var device = "FBOX-OPEN-" + Guid.NewGuid().ToString("N")[..6];
        var older = Seed(device, occurredAt: new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Local));
        var newer = Seed(device, occurredAt: new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Local));
        Seed(device, FaultDisposalService.StatusCleared);
        Seed(device, FaultDisposalService.StatusClosed);
        Seed("FBOX-OTHER-" + Guid.NewGuid().ToString("N")[..6]);

        var page = await _service.QueryOpenAsync(device, 1, 20);

        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { older.Id, newer.Id }, page.Items.Select(e => e.Id).ToArray());
        Assert.All(page.Items, e => Assert.Equal(FaultDisposalService.StatusOpen, e.Status));
        Assert.All(page.Items, e => Assert.Equal(device, e.DeviceId));
    }

    [Fact(DisplayName = "故障处置_没有未关闭故障时返回空页")]
    public async Task QueryOpen_Empty()
    {
        var page = await _service.QueryOpenAsync("FBOX-NONE-" + Guid.NewGuid().ToString("N")[..6], 1, 20);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
    }

    [Fact(DisplayName = "故障处置_分页大小被夹在合理区间")]
    public async Task QueryOpen_PageSizeClamped()
    {
        var device = "FBOX-PAGE-" + Guid.NewGuid().ToString("N")[..6];
        Seed(device);

        var page = await _service.QueryOpenAsync(device, 0, 100000);

        Assert.Equal(1, page.Page);
        Assert.Equal(FaultDisposalService.MaxPageSize, page.PageSize);
    }

    [Fact(DisplayName = "故障处置_手动关闭写状态与清除时间且不动设备事实")]
    public async Task Close_SetsStatusAndClearedAt()
    {
        var device = "FBOX-CLOSE-" + Guid.NewGuid().ToString("N")[..6];
        var fault = Seed(device, message: "气源压力低");
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await _service.CloseAsync(fault.Id, "现场已换气源，设备侧没有恢复事件", "运维小王");

        Assert.True(result.Success, result.Reason);
        Assert.Equal(FaultDisposalService.StatusClosed, result.Fault.Status);

        var saved = FaultRecord.FindById(fault.Id);
        Assert.NotNull(saved);
        Assert.Equal(FaultDisposalService.StatusClosed, saved.Status);
        Assert.True(saved.ClearedAt >= before);
        Assert.Equal(fault.OccurredAt, saved.OccurredAt);
        Assert.Equal("气源压力低", saved.Message);
        Assert.Equal(device, saved.DeviceId);
    }

    [Fact(DisplayName = "故障处置_关闭后不再出现在未关闭列表")]
    public async Task Close_RemovesFromOpenList()
    {
        var device = "FBOX-GONE-" + Guid.NewGuid().ToString("N")[..6];
        var fault = Seed(device);

        await _service.CloseAsync(fault.Id, "设备已报废", "运维小李");
        var page = await _service.QueryOpenAsync(device, 1, 20);

        Assert.Equal(0, page.Total);
    }

    [Fact(DisplayName = "故障处置_已手动关闭的不能重复关闭")]
    public async Task Close_RejectsClosed()
    {
        var fault = Seed("FBOX-DUP-" + Guid.NewGuid().ToString("N")[..6], FaultDisposalService.StatusClosed);

        var result = await _service.CloseAsync(fault.Id, "再关一次", "运维");

        Assert.False(result.Success);
        Assert.Contains("已手动关闭", result.Reason);
    }

    [Fact(DisplayName = "故障处置_设备已恢复的不能手动关闭")]
    public async Task Close_RejectsCleared()
    {
        var fault = Seed("FBOX-CLEARED-" + Guid.NewGuid().ToString("N")[..6], FaultDisposalService.StatusCleared);

        var result = await _service.CloseAsync(fault.Id, "关掉", "运维");

        Assert.False(result.Success);
        Assert.Contains("设备恢复", result.Reason);
    }

    [Fact(DisplayName = "故障处置_关闭原因必填")]
    public async Task Close_RequiresRemark()
    {
        var fault = Seed("FBOX-REMARK-" + Guid.NewGuid().ToString("N")[..6]);

        var result = await _service.CloseAsync(fault.Id, "   ", "运维");

        Assert.False(result.Success);
        Assert.Contains("关闭原因", result.Reason);
        Assert.Equal(FaultDisposalService.StatusOpen, FaultRecord.FindById(fault.Id)?.Status);
    }

    [Fact(DisplayName = "故障处置_记录不存在或ID非法时给出人话")]
    public async Task Close_MissingRecord()
    {
        var missing = await _service.CloseAsync(Int64.MaxValue, "原因", "运维");
        Assert.False(missing.Success);
        Assert.Contains("不存在", missing.Reason);

        var bad = await _service.CloseAsync(0, "原因", "运维");
        Assert.False(bad.Success);
        Assert.Contains("大于 0", bad.Reason);
    }

    [Fact(DisplayName = "故障处置_持续时长按天时分文案")]
    public void DescribeDuration_Formats()
    {
        var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Local);

        Assert.Equal("—", FaultDisposalService.DescribeDuration(null));
        Assert.Equal("35 分钟", FaultDisposalService.DescribeDuration(new FaultRecord { OccurredAt = start, ClearedAt = start.AddMinutes(35) }));
        Assert.Equal("2 小时 30 分钟", FaultDisposalService.DescribeDuration(new FaultRecord { OccurredAt = start, ClearedAt = start.AddMinutes(150) }));
        Assert.Equal("1 天 2 小时", FaultDisposalService.DescribeDuration(new FaultRecord { OccurredAt = start, ClearedAt = start.AddHours(26) }));
    }

    [Fact(DisplayName = "故障处置_未关闭故障的持续时长算到当前")]
    public void DescribeDuration_OpenEnded()
    {
        var text = FaultDisposalService.DescribeDuration(new FaultRecord
        {
            OccurredAt = DateTime.UtcNow.AddMinutes(-10),
            ClearedAt = EfbmDefaults.UnrequeuedAt,
        });

        Assert.Equal("10 分钟", text);
    }

    [Theory(DisplayName = "故障处置_状态文案")]
    [InlineData(0, "未清除")]
    [InlineData(1, "已恢复")]
    [InlineData(2, "手动关闭")]
    [InlineData(9, "9")]
    public void DescribeStatus_Maps(Int32 status, String expected)
        => Assert.Equal(expected, FaultDisposalService.DescribeStatus(status));
}
