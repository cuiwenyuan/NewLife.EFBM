using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>字段演进：未登记字段扫描与现场补登（真实 SQLite，FR-27 / FR-28 / WEB-10a / WEB-10b）。</summary>
/// <remarks>
/// 扫描逻辑的关键约束是「只看窗口内最近 N 条、按事件分组、同一行同名只算一次」，
/// 一旦写成全表聚合就会把后台拖死；补登的关键约束是「同一事件下字段名唯一」，
/// 重复登记要落到一句人话上，而不是把数据库的唯一索引异常抛给运维。
/// 与其它 sqlite 测试同属一个 collection 串行执行；每个用例用独立字段名隔离，断言不依赖执行顺序。
/// </remarks>
[Collection("sqlite")]
public sealed class FieldEvolutionServiceTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-field-evo");

    private readonly FieldEvolutionService _service = new();

    /// <summary>独立 SQLite 文件；显式建表绕开 XCode 的进程级表结构缓存。</summary>
    public FieldEvolutionServiceTests()
    {
        DAL.AddConnStr("EFBM", $"Data Source={_dbFile};Provider=SQLite", null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("EFBM").CheckTables();
    }

    /// <summary>插入一条带未识别字段的原始报文。</summary>
    private static DeviceEventRaw SeedRaw(String unknownFields, String extJson = null, String eventName = "ORDER_COMPLETED",
        String deviceId = "FBOX-FE-A01", DateTime? receivedAt = null)
    {
        var time = receivedAt ?? DateTime.Now.AddMinutes(-5);

        var entity = new DeviceEventRaw
        {
            EventKey = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("D"),
            StreamId = "1-0",
            Event = eventName,
            DeviceId = deviceId,
            EventTimeRaw = time.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            EventTime = time,
            RawJson = "{}",
            DataJson = "{}",
            ExtJson = extJson,
            UnknownFields = unknownFields,
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

    private static String UniqueField(String prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    [Fact(DisplayName = "字段演进_扫描聚合未登记字段并取最新样例")]
    public async Task Scan_AggregatesCandidates()
    {
        var field = UniqueField("boxType");
        var device = "FBOX-FE-" + Guid.NewGuid().ToString("N")[..6];

        SeedRaw($"{field},operator", $"{{\"{field}\":\"A\"}}", deviceId: device, receivedAt: DateTime.Now.AddMinutes(-30));
        SeedRaw(field, $"{{\"{field}\":\"B\"}}", deviceId: device, receivedAt: DateTime.Now.AddMinutes(-10));

        var snapshot = await _service.ScanAsync(1, 500);
        var candidate = snapshot.Candidates.Single(e => e.FieldName == field);

        Assert.Equal(2, candidate.Count);
        Assert.Equal("B", candidate.Sample);
        Assert.Equal(device, candidate.SampleDeviceId);
        Assert.Equal("String", candidate.InferredType);
        Assert.False(candidate.Registered);
        Assert.True(candidate.FirstSeen <= candidate.LastSeen);

        // 样例报文主键必须指向真实存在的报文，否则页面点进去就是 404
        Assert.True(candidate.SampleEventId > 0);
        Assert.NotNull(DeviceEventRaw.FindById(candidate.SampleEventId));
    }

    [Fact(DisplayName = "字段演进_同一行重复出现只算一次")]
    public async Task Scan_DedupesWithinRow()
    {
        var field = UniqueField("dup");

        SeedRaw($"{field},{field}");

        var snapshot = await _service.ScanAsync(1, 500);

        Assert.Equal(1, snapshot.Candidates.Single(e => e.FieldName == field).Count);
    }

    [Fact(DisplayName = "字段演进_补登后扫描结果标记为已登记")]
    public async Task Scan_ReflectsRegistration()
    {
        var field = UniqueField("solid");
        SeedRaw(field, $"{{\"{field}\":12}}");

        var before = await _service.ScanAsync(1, 500);
        Assert.False(before.Candidates.Single(e => e.FieldName == field).Registered);

        var register = await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "折盒数", "Number(Int32)", true, "现场确认", "运维");
        Assert.True(register.Success, register.Reason);

        var after = await _service.ScanAsync(1, 500);
        var candidate = after.Candidates.Single(e => e.FieldName == field);

        Assert.True(candidate.Registered);
        Assert.Equal("折盒数", candidate.DisplayName);
        Assert.True(candidate.Solidified);
        Assert.Equal("Number(Int32)", candidate.JsonType);
        Assert.Contains(after.Registered, e => e.FieldName == field && e.Source == "登记表");
    }

    [Fact(DisplayName = "字段演进_窗口与条数上限被夹住")]
    public async Task Scan_ClampsWindowAndLimit()
    {
        var snapshot = await _service.ScanAsync(100000, 100000);

        Assert.Equal(FieldEvolutionService.MaxRowsLimit, snapshot.MaxRows);
        Assert.InRange((snapshot.To - snapshot.From).TotalDays, FieldEvolutionService.MaxDays - 1, FieldEvolutionService.MaxDays);
    }

    [Fact(DisplayName = "字段演进_超过扫描上限时标记截断")]
    public async Task Scan_ReportsTruncated()
    {
        var field = UniqueField("trunc");
        SeedRaw(field);
        SeedRaw(field);

        var snapshot = await _service.ScanAsync(1, 1);

        Assert.True(snapshot.Truncated);
        Assert.Equal(1, snapshot.ScannedRows);
    }

    [Fact(DisplayName = "字段演进_代码字典始终随快照返回")]
    public async Task Scan_IncludesCodeRegistry()
    {
        var snapshot = await _service.ScanAsync(1, 100);

        Assert.NotEmpty(snapshot.Known);
        Assert.All(snapshot.Known, e => Assert.Equal("代码", e.Source));
        Assert.Contains(snapshot.Known, e => !String.IsNullOrEmpty(e.FieldName));
    }

    [Fact(DisplayName = "字段演进_补登必填项逐条校验")]
    public async Task Register_Validates()
    {
        var field = UniqueField("validate");

        Assert.Contains("字段名必填", (await _service.RegisterAsync(0, "ORDER_COMPLETED", "  ", "名", "String", false, null, "运维")).Reason);
        Assert.Contains("字母", (await _service.RegisterAsync(0, "ORDER_COMPLETED", "1" + field, "名", "String", false, null, "运维")).Reason);
        Assert.Contains("中文名", (await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "", "String", false, null, "运维")).Reason);
        Assert.Contains("JSON 类型", (await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "名", "Date", false, null, "运维")).Reason);
        Assert.Contains("不存在", (await _service.RegisterAsync(999999, "ORDER_COMPLETED", field, "名", "String", false, null, "运维")).Reason);
    }

    [Fact(DisplayName = "字段演进_同一事件下字段名重复登记被挡住")]
    public async Task Register_RejectsDuplicate()
    {
        var field = UniqueField("dupreg");

        var first = await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "第一次", "String", false, null, "运维");
        Assert.True(first.Success, first.Reason);

        var second = await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "第二次", "String", false, null, "运维");

        Assert.False(second.Success);
        Assert.Contains("已登记过", second.Reason);
    }

    [Fact(DisplayName = "字段演进_信封字段重名同样被挡住而不是抛约束异常")]
    public async Task Register_RejectsDuplicateEnvelopeField()
    {
        var field = UniqueField("env");

        var first = await _service.RegisterAsync(0, String.Empty, field, "信封字段", "String", false, null, "运维");
        Assert.True(first.Success, first.Reason);

        var second = await _service.RegisterAsync(0, String.Empty, field, "信封字段二", "String", false, null, "运维");

        Assert.False(second.Success);
        Assert.Contains("已登记过", second.Reason);
    }

    [Fact(DisplayName = "字段演进_按主键修改已有登记")]
    public async Task Register_UpdatesExisting()
    {
        var field = UniqueField("upd");

        var created = await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "旧名", "String", false, "旧备注", "运维");
        Assert.True(created.Success, created.Reason);

        var updated = await _service.RegisterAsync(created.Dict.Id, "ORDER_COMPLETED", field, "新名", "Number(Int32)", true, "新备注", "运维");

        Assert.True(updated.Success, updated.Reason);
        Assert.Equal(created.Dict.Id, updated.Dict.Id);
        Assert.Equal("新名", updated.Dict.DisplayName);
        Assert.True(updated.Dict.Solidified);
        Assert.Equal("新备注", updated.Dict.Remark);
    }

    [Fact(DisplayName = "字段演进_删除登记后不再命中")]
    public async Task Delete_Removes()
    {
        var field = UniqueField("del");
        var created = await _service.RegisterAsync(0, "ORDER_COMPLETED", field, "待删", "String", false, null, "运维");

        Assert.True(await _service.DeleteAsync(created.Dict.Id, "运维"));
        Assert.Null(FieldDict.FindById(created.Dict.Id));
        Assert.False(await _service.DeleteAsync(created.Dict.Id, "运维"));
        Assert.False(await _service.DeleteAsync(0, "运维"));
    }

    [Theory(DisplayName = "字段演进_按样例推断JSON类型")]
    [InlineData("123", "Number(Int32)")]
    [InlineData("-8", "Number(Int32)")]
    [InlineData("1.5", "Number(Decimal)")]
    [InlineData("true", "Boolean")]
    [InlineData("{\"a\":1}", "Object")]
    [InlineData("[1,2]", "Array")]
    [InlineData("abc", "String")]
    [InlineData("", "String")]
    [InlineData(null, "String")]
    public void InferType_Works(String sample, String expected)
        => Assert.Equal(expected, FieldEvolutionService.InferType(sample));

    [Theory(DisplayName = "字段演进_字段名只允许字母数字下划线且不以数字开头")]
    [InlineData("boxType", true)]
    [InlineData("_tmp", true)]
    [InlineData("f1_2", true)]
    [InlineData("1abc", false)]
    [InlineData("box-type", false)]
    [InlineData("box type", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidFieldName_Works(String name, Boolean expected)
        => Assert.Equal(expected, FieldEvolutionService.IsValidFieldName(name));

    [Fact(DisplayName = "字段演进_信封字段的展示文案")]
    public void DescribeEvent_Works()
    {
        Assert.Equal("信封字段（通用）", FieldEvolutionService.DescribeEvent(null));
        Assert.Equal("信封字段（通用）", FieldEvolutionService.DescribeEvent(" "));
        Assert.Equal("ORDER_COMPLETED", FieldEvolutionService.DescribeEvent("ORDER_COMPLETED"));
    }
}
