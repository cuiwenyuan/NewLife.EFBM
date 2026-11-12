using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>统一查询统计 API 的数据层（WEB-11a、WEB-2a / 4a / 5a，真实 SQLite）。</summary>
/// <remarks>
/// 重点不是「能查出来」，而是几处容易写错的地方：
/// ① 状态中文必须回落为原值而不是消失；② 产量按工单取累计峰值再加总，不能逐行求和；
/// ③ 未清除故障的持续时长算到当前时刻，不能把 1970 哨兵值当成清除时间；④ 分页上限与倒挂区间。
/// </remarks>
[Collection("sqlite")]
public sealed class QueryApiServiceTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-query-api");

    private readonly QueryApiService _service = new();

    /// <summary>独立 SQLite 文件；显式建表绕开 XCode 的进程级表结构缓存。</summary>
    public QueryApiServiceTests()
    {
        DAL.AddConnStr("EFBM", ConnectionStringTuning.Normalize($"Data Source={_dbFile};Provider=SQLite"), null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("EFBM").CheckTables();
    }

    [Fact(DisplayName = "设备查询_状态中文来自字典_未登记回落原值")]
    public async Task Devices_StatusText()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        StatusDict.Find(StatusDict._.StatusCode == "RUNNING")?.Delete();
        new StatusDict { StatusCode = "RUNNING", DisplayName = "运行中", Enabled = true }.Insert();

        SeedDevice($"FBOX-ST-{tag}-A", "RUNNING");
        SeedDevice($"FBOX-ST-{tag}-B", "UNKNOWN_X");

        var page = await _service.QueryDevicesAsync(new DeviceQuery { DeviceId = $"FBOX-ST-{tag}-A" });

        Assert.Single(page.Items);
        Assert.Equal("RUNNING", page.Items[0].StatusRaw);
        Assert.Equal("运行中", page.Items[0].StatusText);

        var unknown = await _service.GetDeviceAsync($"FBOX-ST-{tag}-B");
        Assert.Equal("UNKNOWN_X", unknown.StatusText);
    }

    [Fact(DisplayName = "设备查询_页大小超上限被收敛_倒挂区间返回空")]
    public async Task Devices_ClampAndInverted()
    {
        var page = await _service.QueryDevicesAsync(new DeviceQuery { PageSize = 9999 });
        Assert.Equal(QueryApiService.MaxPageSize, page.PageSize);

        var inverted = await _service.QueryDevicesAsync(new DeviceQuery
        {
            From = new DateTime(2026, 11, 12),
            To = new DateTime(2026, 11, 11),
        });

        Assert.Empty(inverted.Items);
        Assert.Equal(0, inverted.Total);
    }

    [Fact(DisplayName = "设备查询_不存在的设备返回 null")]
    public async Task Device_Missing()
    {
        Assert.Null(await _service.GetDeviceAsync("NOT-EXIST-" + Guid.NewGuid().ToString("N")[..6]));
        Assert.Null(await _service.GetDeviceAsync(null));
    }

    [Fact(DisplayName = "工单查询_按状态过滤并给出进度比例")]
    public async Task Orders_Progress()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        SeedOrder($"WO-{tag}-1", "FBOX-1", 100, 25, "进行中");
        SeedOrder($"WO-{tag}-2", "FBOX-1", 0, 5, "已完成");

        var running = await _service.QueryOrdersAsync(new WorkOrderQuery { OrderId = $"WO-{tag}-1" });
        Assert.Single(running.Items);
        Assert.Equal(0.25, running.Items[0].Progress);

        // 计划数为 0 时不给比例，避免除零得到一个编造的 100%
        var zero = await _service.QueryOrdersAsync(new WorkOrderQuery { OrderId = $"WO-{tag}-2" });
        Assert.Null(zero.Items[0].Progress);
    }

    [Fact(DisplayName = "明细查询_按工单过滤_分页正确")]
    public async Task Production_FilterAndPage()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        for (var i = 0; i < 5; i++)
            SeedRecord($"WO-P-{tag}", "FBOX-1", i + 1, new DateTime(2026, 11, 10, 8, 0, 0).AddMinutes(i));

        var first = await _service.QueryProductionAsync(new ProductionQuery { OrderId = $"WO-P-{tag}", Page = 1, PageSize = 2 });
        var second = await _service.QueryProductionAsync(new ProductionQuery { OrderId = $"WO-P-{tag}", Page = 3, PageSize = 2 });

        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, first.PageCount);
        Assert.Single(second.Items);

        // 默认按事件时间倒序：第一页第一条是最新的
        Assert.Equal(5, first.Items[0].CompletedQty);
    }

    [Fact(DisplayName = "产量统计_按工单取累计峰值_按设备加总")]
    public async Task Output_MaxThenSum()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var device = $"FBOX-OUT-{tag}";

        // 同一工单累计值 1 / 2 / 3：取峰值 3，而不是 1+2+3=6
        SeedRecord($"WO-OUT-{tag}-A", device, 1, new DateTime(2026, 11, 10, 8, 0, 0));
        SeedRecord($"WO-OUT-{tag}-A", device, 2, new DateTime(2026, 11, 10, 8, 1, 0));
        SeedRecord($"WO-OUT-{tag}-A", device, 3, new DateTime(2026, 11, 10, 8, 2, 0));

        // 另一工单累计 5 / 5：取峰值 5
        SeedRecord($"WO-OUT-{tag}-B", device, 5, new DateTime(2026, 11, 10, 9, 0, 0));
        SeedRecord($"WO-OUT-{tag}-B", device, 5, new DateTime(2026, 11, 10, 9, 1, 0));

        var byOrder = await _service.QueryOutputAsync(new OutputQuery { DeviceId = device, GroupBy = "order" });
        Assert.Equal(2, byOrder.Items.Count);
        Assert.Equal(3, byOrder.Items.Single(e => e.Key == $"WO-OUT-{tag}-A").CompletedQty);
        Assert.Equal(5, byOrder.Items.Single(e => e.Key == $"WO-OUT-{tag}-B").CompletedQty);

        var byDevice = await _service.QueryOutputAsync(new OutputQuery { DeviceId = device, GroupBy = "device" });
        Assert.Single(byDevice.Items);
        Assert.Equal(8, byDevice.Items[0].CompletedQty);
        Assert.Equal(2, byDevice.Items[0].OrderCount);
        Assert.Equal(5, byDevice.Items[0].BoxCount);
    }

    [Fact(DisplayName = "产量统计_超过扫描上限标记截断")]
    public async Task Output_Truncated()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var device = $"FBOX-TR-{tag}";

        for (var i = 0; i < 4; i++)
            SeedRecord($"WO-TR-{tag}", device, i + 1, new DateTime(2026, 11, 11, 8, 0, 0).AddMinutes(i));

        var summary = await _service.QueryOutputAsync(new OutputQuery { DeviceId = device, ScanLimit = 2 });

        Assert.True(summary.Truncated);
        Assert.Equal(2, summary.ScannedRows);
    }

    [Fact(DisplayName = "故障查询_已清除按时长算_未清除算到当前")]
    public async Task Faults_Duration()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var device = $"FBOX-FT-{tag}";
        var occurred = DateTime.UtcNow.AddHours(-2);

        SeedFault(device, occurred, occurred.AddMinutes(30), 1);
        SeedFault(device, DateTime.UtcNow.AddMinutes(-5), EfbmDefaults.UnrequeuedAt, 0);

        var page = await _service.QueryFaultsAsync(new FaultQuery { DeviceId = device });

        Assert.Equal(2, page.Total);

        var cleared = page.Items.Single(e => e.Status == 1);
        Assert.Equal(1800, cleared.DurationSeconds);
        Assert.Equal("已清除", cleared.StatusText);
        Assert.NotNull(cleared.ClearedAt);

        var open = page.Items.Single(e => e.Status == 0);
        Assert.Null(open.ClearedAt);
        Assert.True(open.IsOpen);
        Assert.InRange(open.DurationSeconds!.Value, 250, 400);
        Assert.Equal("未清除", open.StatusText);
    }

    [Fact(DisplayName = "故障查询_只看未清除")]
    public async Task Faults_OpenOnly()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var device = $"FBOX-FO-{tag}";

        SeedFault(device, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 1);
        SeedFault(device, DateTime.UtcNow.AddHours(-1), EfbmDefaults.UnrequeuedAt, 0);

        var open = await _service.QueryFaultsAsync(new FaultQuery { DeviceId = device, OpenOnly = true });

        Assert.Single(open.Items);
        Assert.Equal(0, open.Items[0].Status);
    }

    [Fact(DisplayName = "Stream积压_未接入时返回结构完整而不是抛异常")]
    public async Task Stream_Unavailable()
    {
        var disabled = await _service.GetStreamBacklogAsync(null, new EfbmOptions { RedisEnabled = false });
        Assert.False(disabled.Enabled);
        Assert.False(disabled.Connected);
        Assert.NotNull(disabled.Error);
        Assert.Empty(disabled.Groups);

        var noProbe = await _service.GetStreamBacklogAsync(null, new EfbmOptions { RedisEnabled = true, StreamKey = "efbm:test" });
        Assert.True(noProbe.Enabled);
        Assert.Equal("efbm:test", noProbe.StreamKey);
    }

    [Fact(DisplayName = "Stream积压_从指标探测映射组字段")]
    public async Task Stream_MapsGroups()
    {
        var probe = new FakeMetricsProbe
        {
            Metrics = new QueueMetrics
            {
                Enabled = true,
                Connected = true,
                StreamKey = "efbm:events",
                Length = 120,
                SampledAt = DateTime.Now,
                Groups =
                [
                    new QueueGroupMetric { Name = "efbm-persist", Consumers = 1, Pending = 2, Lag = 3, IsPersistGroup = true },
                    new QueueGroupMetric { Name = "efbm-mes", Consumers = 0, Pending = 0, Lag = 9, IsZombie = true, Advice = "有积压但没有消费者" },
                ],
            },
        };

        var view = await _service.GetStreamBacklogAsync(probe, new EfbmOptions { RedisEnabled = true });

        Assert.True(view.Connected);
        Assert.Equal(12, view.TotalLag);
        Assert.Equal(2, view.TotalPending);
        Assert.Equal(2, view.Groups.Count);
        Assert.True(view.Groups[1].IsZombie);
        Assert.Equal("有积压但没有消费者", view.Groups[1].Advice);
    }

    /// <summary>插入设备。</summary>
    private static void SeedDevice(String deviceId, String statusRaw)
    {
        new Device
        {
            DeviceId = deviceId,
            Name = "测试设备 " + deviceId,
            StatusRaw = statusRaw,
            LastReportTime = DateTime.Now,
            Enabled = true,
        }.Insert();
    }

    /// <summary>插入工单。</summary>
    private static void SeedOrder(String orderId, String deviceId, Int32 planQty, Int32 completedQty, String status)
    {
        new WorkOrder
        {
            OrderId = orderId,
            BoxId = "BOX-1",
            DeviceId = deviceId,
            PlanQty = planQty,
            CompletedQty = completedQty,
            Status = status,
            StartedAt = new DateTime(2026, 11, 10, 8, 0, 0),
            LastReportAt = new DateTime(2026, 11, 10, 9, 0, 0),
        }.Insert();
    }

    /// <summary>插入逐件明细；<c>Id</c> 是时间缩放主键，手工指定避免依赖插入顺序。</summary>
    private static void SeedRecord(String orderId, String deviceId, Int32 completedQty, DateTime eventTime)
    {
        new ProductionRecord
        {
            EventKey = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("N"),
            OrderId = orderId,
            BoxId = "BOX-1",
            DeviceId = deviceId,
            IsPiece = true,
            PlanQty = 100,
            CompletedQty = completedQty,
            EventTime = eventTime,
            ParseVersion = EfbmDefaults.ParseVersion,
        }.Insert();
    }

    /// <summary>插入故障记录。</summary>
    private static void SeedFault(String deviceId, DateTime occurredAt, DateTime clearedAt, Int32 status)
    {
        new FaultRecord
        {
            EventKey = Guid.NewGuid().ToString("N"),
            DeviceId = deviceId,
            Message = "急停被按下",
            OccurredAt = occurredAt,
            ClearedAt = clearedAt,
            Status = status,
            ParseVersion = EfbmDefaults.ParseVersion,
        }.Insert();
    }

    /// <summary>固定返回给定指标的探测替身。</summary>
    private sealed class FakeMetricsProbe : IQueueMetricsProbe
    {
        public QueueMetrics Metrics { get; set; }

        public Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Metrics);
    }
}
