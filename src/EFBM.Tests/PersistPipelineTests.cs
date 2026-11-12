using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Consumers;
using EFBM.Web.Consumers.Handlers;
using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,xcode,testing-strategy] | skills=[xcode-data-modeling,testing-strategy] | agent=default

/// <summary>消费落库全链路集成测试：真实 SQLite + 真实 handler + 真实存储实现（P3 验收）。</summary>
/// <remarks>
/// 验收标准（开发计划 §3 P3）：逐件明细全量保存、工单累计值正确、乱序不回退、
/// 重复投递只产生 1 条原始报文 + 1 条明细、未知事件原样落库。
/// 与 <see cref="SqliteSmokeTests"/> 同属 sqlite collection：<c>DAL.AddConnStr</c> 覆盖同名连接，
/// 两个真实库测试类并行会互相抢连接串，必须串行。
/// </remarks>
[Collection("sqlite")]
public sealed class PersistPipelineTests
{
    /// <summary>类级共享库文件：xUnit 每个测试方法都会重建类实例，逐方法换文件会撞上
    /// XCode 静态建表缓存（新文件里没有表）；类内测试串行共享一张表，数据用不同单号 / 设备隔离。
    /// 文件在测试进程退出时统一删除，避免每次运行都在临时目录留下残留。</summary>
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-p3");

    private readonly EventPersistService _persist;
    private readonly EfbmOptions _options = new() { DeviceAutoRegister = true };

    /// <summary>独立 SQLite 文件 + 真实 handler 组合。</summary>
    public PersistPipelineTests()
    {
        DAL.AddConnStr("EFBM", ConnectionStringTuning.Normalize($"Data Source={_dbFile};Provider=SQLite"), null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        // 显式建表：本类不直接建表时依赖 Migration=On 的首次自动建表，而 XCode 的建表结果在进程内缓存，
        // 前一个测试类建过表后换文件就不再建，表现为 "no such table"
        DAL.Create("EFBM").CheckTables();

        var handlers = new IEventHandler[]
        {
            new OrderStartedHandler(),
            new OrderProgressHandler(),
            new OrderCompletedHandler(),
            new DeviceStatusChangedHandler(),
            new FaultOccurredHandler(),
            new FaultClearedHandler(),
            new UnknownEventHandler(),
        };

        _persist = new EventPersistService(_options,
            new EventRawStore(), new DeadLetterStore(), new DeviceRegistry(_options),
            new EventHandlerRegistry(handlers));
    }

    /// <summary>发送一条报文进消费管线。</summary>
    private Task<PersistOutcome> Consume(String eventName, String timestamp, String dataJson = null, String deviceId = "FBOX-A01") =>
        _persist.PersistAsync(PersistServiceFactory.Message(eventName, deviceId, timestamp, dataJson), "1-1");

    [Fact(DisplayName = "消费_工单开始_建工单并写开工时间")]
    public async Task OrderStarted_CreatesWorkOrder()
    {
        var outcome = await Consume(EventNames.OrderStarted, "2026-11-12T08:00:00+08:00",
            "\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\"");

        Assert.Equal(PersistOutcome.Persisted, outcome);
        var order = WorkOrder.FindAll(WorkOrder._.OrderId == "WO-001").Single();
        Assert.Equal(WorkOrderSync.StatusRunning, order.Status);
        Assert.True(order.StartedAt > DateTime.MinValue);
        Assert.Equal("BOX-0001", order.BoxId);
        Assert.Equal("FBOX-A01", order.DeviceId);
    }

    [Fact(DisplayName = "消费_逐件完成1000条_明细全量保存且工单累计正确")]
    public async Task ThousandPieces_AllPersistedWithCorrectTotal()
    {
        const Int32 total = 1000;
        for (var i = 1; i <= total; i++)
        {
            var ts = $"2026-11-12T08:00:00.{i:000}+08:00";
            var outcome = await Consume(EventNames.OrderCompleted, ts,
                $"\"orderId\":\"WO-002\",\"boxId\":\"BOX-0001\",\"planQty\":{total},\"completedQty\":{i}");
            Assert.Equal(PersistOutcome.Persisted, outcome);
        }

        Assert.Equal(total, ProductionRecord.FindAll(ProductionRecord._.OrderId == "WO-002").Count);
        var order = WorkOrder.FindAll(WorkOrder._.OrderId == "WO-002").Single();
        Assert.Equal(total, order.CompletedQty);
        Assert.Equal(total, order.PlanQty);
        Assert.Equal(WorkOrderSync.StatusCompleted, order.Status);   // completedQty >= planQty 派生
        Assert.True(order.CompletedAt > DateTime.MinValue);
        // 逐件事件的 IsPiece 标记
        Assert.All(ProductionRecord.FindAll(ProductionRecord._.OrderId == "WO-002"), r => Assert.True(r.IsPiece));
    }

    [Fact(DisplayName = "消费_乱序到达_累计值不回退开工时间取小")]
    public async Task OutOfOrder_NoRegression()
    {
        // 先到 08:10 的完成 100，再到 08:00 的开始，再到 08:05 的里程碑 50（旧值）
        await Consume(EventNames.OrderCompleted, "2026-11-12T08:10:00+08:00",
            "\"orderId\":\"WO-003\",\"planQty\":100,\"completedQty\":100");
        await Consume(EventNames.OrderStarted, "2026-11-12T08:00:00+08:00",
            "\"orderId\":\"WO-003\"");
        await Consume(EventNames.OrderProgress, "2026-11-12T08:05:00+08:00",
            "\"orderId\":\"WO-003\",\"planQty\":100,\"completedQty\":50");

        var order = WorkOrder.FindAll(WorkOrder._.OrderId == "WO-003").Single();
        Assert.Equal(100, order.CompletedQty);   // 旧值 50 不覆盖 100

        Assert.True(order.StartedAt <= order.CompletedAt);   // 开始时间被 08:00 的 STARTED 提前

        // 里程碑事件 IsPiece=false，逐件才 true
        Assert.Single(ProductionRecord.FindAll(ProductionRecord._.OrderId == "WO-003" & ProductionRecord._.IsPiece == false));
    }

    [Fact(DisplayName = "消费_同一报文重复投递_只产生一条原始报文与一条明细")]
    public async Task DuplicateDelivery_ExactlyOneRowEach()
    {
        var ts = "2026-11-12T09:30:00+08:00";
        await Consume(EventNames.OrderCompleted, ts, "\"orderId\":\"WO-004\",\"planQty\":10,\"completedQty\":3");
        // PEL 重投 / 组重建重放：同一 EventKey 再来一次
        var duplicate = await Consume(EventNames.OrderCompleted, ts, "\"orderId\":\"WO-004\",\"planQty\":10,\"completedQty\":3");

        Assert.Equal(PersistOutcome.Duplicate, duplicate);
        Assert.Single(DeviceEventRaw.FindAll(DeviceEventRaw._.Event == EventNames.OrderCompleted & DeviceEventRaw._.EventTimeRaw == ts));
        Assert.Single(ProductionRecord.FindAll(ProductionRecord._.OrderId == "WO-004"));
        var raw = DeviceEventRaw.FindAll(DeviceEventRaw._.EventTimeRaw == ts).Single();
        Assert.Equal(1, raw.RepeatCount);
        // 工单累计值也只受一次影响
        Assert.Equal(3, WorkOrder.FindAll(WorkOrder._.OrderId == "WO-004").Single().CompletedQty);
    }

    [Fact(DisplayName = "消费_状态切换_写流水并更新设备当前状态")]
    public async Task StatusChanged_WritesLogAndUpdatesDevice()
    {
        await Consume(EventNames.DeviceStatusChanged, "2026-11-12T09:10:00+08:00", "\"status\":\"RUNNING\"");
        await Consume(EventNames.DeviceStatusChanged, "2026-11-12T09:11:00+08:00", "\"status\":\"PAUSED\"");

        var logs = DeviceStatusLog.FindAll(DeviceStatusLog._.DeviceId == "FBOX-A01").OrderBy(e => e.EventTime).ToList();
        Assert.Equal(2, logs.Count);
        Assert.Equal("RUNNING", logs[0].StatusRaw);
        Assert.Equal("PAUSED", logs[1].StatusRaw);

        var device = Device.FindAll(Device._.DeviceId == "FBOX-A01").Single();
        Assert.Equal("PAUSED", device.StatusRaw);   // 当前状态为最新值
        Assert.True(device.LastReportTime > DateTime.MinValue);
    }

    [Fact(DisplayName = "消费_故障发生与清除_开闭配对且清除幂等")]
    public async Task FaultOccurredThenCleared_PairedAndIdempotent()
    {
        await Consume(EventNames.FaultOccurred, "2026-11-12T10:00:00+08:00", "\"message\":\"卡纸\"");
        await Consume(EventNames.FaultCleared, "2026-11-12T10:05:00+08:00");

        var fault = FaultRecord.FindAll(FaultRecord._.DeviceId == "FBOX-A01").Single();
        Assert.Equal(1, fault.Status);
        Assert.True(fault.ClearedAt > fault.OccurredAt);
        Assert.False(String.IsNullOrEmpty(fault.ClearEventKey));

        // 重投清除事件（Pending 恢复场景之外的重复执行）：不抛错，也不产生第二条故障
        await Consume(EventNames.FaultCleared, "2026-11-12T10:05:00+08:00");
        Assert.Single(FaultRecord.FindAll(FaultRecord._.DeviceId == "FBOX-A01"));

        // 清除先于发生到达：找不到未清除故障时宽容跳过
        var early = await Consume(EventNames.FaultCleared, "2026-11-12T09:00:00+08:00");
        Assert.Equal(PersistOutcome.Persisted, early);
    }

    [Fact(DisplayName = "消费_未知事件_原样落库且不写聚合表")]
    public async Task UnknownEvent_RawOnly()
    {
        var outcome = await Consume("MYSTERY_EVENT", "2026-11-12T11:00:00+08:00", "\"anything\":1");

        Assert.Equal(PersistOutcome.Persisted, outcome);
        var raw = DeviceEventRaw.FindAll(DeviceEventRaw._.Event == "MYSTERY_EVENT").Single();
        Assert.Contains("anything", raw.UnknownFields);
        Assert.Contains("anything", raw.ExtJson);
        // 未知事件不写任何聚合表：该 EventKey 不出现在各业务表（库被类内其它测试共享，必须按键断言）
        Assert.Empty(ProductionRecord.FindAll(ProductionRecord._.EventKey == raw.EventKey));
        Assert.Empty(DeviceStatusLog.FindAll(DeviceStatusLog._.EventKey == raw.EventKey));
        Assert.Empty(FaultRecord.FindAll(FaultRecord._.EventKey == raw.EventKey));
    }

    [Fact(DisplayName = "消费_设备自动登记_首报建设备并可关闭")]
    public async Task DeviceAutoRegister_CreatesAndCanBeDisabled()
    {
        await Consume(EventNames.DeviceStatusChanged, "2026-11-12T12:00:00+08:00", "\"status\":\"IDLE\"", deviceId: "FBOX-B02");
        var device = Device.FindAll(Device._.DeviceId == "FBOX-B02").Single();
        Assert.True(device.Enabled);

        // 关闭自动登记：新设备不建记录，但事件照常落库
        _options.DeviceAutoRegister = false;
        await Consume(EventNames.DeviceStatusChanged, "2026-11-12T12:01:00+08:00", "\"status\":\"IDLE\"", deviceId: "FBOX-C03");
        Assert.Empty(Device.FindAll(Device._.DeviceId == "FBOX-C03"));
        Assert.Single(DeviceEventRaw.FindAll(DeviceEventRaw._.DeviceId == "FBOX-C03"));
    }

    [Fact(DisplayName = "消费_原始报文字段完整_含DataJson与ParseWarning")]
    public async Task RawRecord_KeepsAllFields()
    {
        var ts = "2026-11-12T13:00:00+08:00";
        await Consume(EventNames.OrderCompleted, ts,
            "\"orderId\":\"WO-005\",\"boxId\":\"BOX-9\",\"planQty\":100,\"completedQty\":7,\"newField\":\"x\"");

        var raw = DeviceEventRaw.FindAll(DeviceEventRaw._.EventTimeRaw == ts).Single();
        Assert.Contains("orderId", raw.DataJson);
        Assert.Contains("newField", raw.UnknownFields);   // 未知字段监测（FR-27 的数据基础）
        Assert.Equal(EfbmDefaults.ParseVersion, raw.ParseVersion);
        Assert.Contains("newField", raw.RawJson);          // 原文全量
    }
}
