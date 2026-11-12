using EFBM.Core;

namespace EFBM.Tests;

/// <summary>接收链路编排测试：入流即 200、非法即弃、鉴权失败、降级与双失败 503（验收用例 C1 / C2 / C8 / C16）。</summary>
public class EventIngestServiceTests
{
    private const String Piece = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":1}}";

    /// <summary>一组可独立构造的接收链路依赖。</summary>
    private sealed class Fixture
    {
        public FakeEventQueue Queue { get; } = new();
        public FakeCredentialStore Credentials { get; } = new();
        public FakeFallbackStore Fallback { get; } = new();
        public FakeRequestLog Log { get; } = new();
        public EventIngestService Service { get; }

        /// <summary>构造一组依赖。</summary>
        /// <param name="options">EFBM 配置；为空时用默认内网 AppKeyAppSecret 配置。</param>
        public Fixture(EfbmOptions options = null)
        {
            Credentials.With(FakeCredentialStore.Valid());
            Service = new EventIngestService(options ?? new EfbmOptions(), Queue, Credentials, Fallback, Log);
        }

        /// <summary>构造入流请求。</summary>
        /// <param name="raw">报文原文。</param>
        /// <param name="key">AppKey。</param>
        /// <param name="secret">AppSecret。</param>
        /// <returns>入流请求。</returns>
        public static IngestRequest Request(String raw = Piece, String key = "key-1", String secret = "secret-1") => new()
        {
            RawJson = raw,
            AppKey = key,
            AppSecret = secret,
            SourceIp = "10.0.0.9",
            ReceivedAt = DateTime.UtcNow,
            Method = "POST",
            Path = "/api/events",
            Scheme = "http",
            ContentType = "application/json",
            UserAgent = "FBox-Firmware/1.0",
            TraceId = "trace-1",
        };
    }

    [Fact(DisplayName = "入流_合法报文_立即200且同步链路零兜底写入")]
    public async Task ValidReport_Returns200WithoutAnyDbWrite()
    {
        var f = new Fixture();

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(0, result.Code);
        Assert.Equal("accepted", result.Message);
        Assert.True(result.Queued);
        Assert.False(result.Fallback);
        Assert.Single(f.Queue.Added);
        Assert.Empty(f.Fallback.Saved);          // 正常路径没有任何本地写入
        Assert.Equal(Piece, f.Queue.Added[0].RawJson);
        Assert.Equal("10.0.0.9", f.Queue.Added[0].SourceIp);
        Assert.Equal(EventKeyBuilder.Compute("ORDER_COMPLETED", "FBOX-A01", "2026-07-23T18:00:10+08:00"), result.EventKey);
        Assert.Equal(result.EventKey, f.Queue.Added[0].EventKey);
        Assert.True(Guid.TryParseExact(result.MessageId, "D", out _));
    }

    [Theory(DisplayName = "入流_非法JSON或缺必填_返回400且不留Stream或兜底记录")]
    [InlineData("{\"event\":\"ORDER_COMPLETED\",}")]
    [InlineData("{\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\"}")]
    [InlineData("not json")]
    public async Task InvalidReport_Returns400AndDiscards(String raw)
    {
        var f = new Fixture();

        var result = await f.Service.IngestAsync(Fixture.Request(raw));

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(400, result.Code);
        Assert.Empty(f.Queue.Added);
        Assert.Empty(f.Fallback.Saved);
        Assert.Contains("非法", result.Warning ?? String.Empty);
    }

    [Fact(DisplayName = "入流_凭据缺失_返回401且不入流")]
    public async Task MissingCredential_Returns401()
    {
        var f = new Fixture();

        var result = await f.Service.IngestAsync(Fixture.Request(key: null, secret: null));

        Assert.Equal(401, result.StatusCode);
        Assert.Empty(f.Queue.Added);
        Assert.Contains("FBOX-A01", result.Warning);
        Assert.DoesNotContain("secret-1", result.Message);   // 响应不含密钥
    }

    [Fact(DisplayName = "入流_凭据绑定设备与报文不一致_返回403")]
    public async Task DeviceMismatch_Returns403()
    {
        var f = new Fixture();
        var raw = Piece.Replace("FBOX-A01", "FBOX-B02");

        Assert.Equal(403, (await f.Service.IngestAsync(Fixture.Request(raw))).StatusCode);
        Assert.Empty(f.Queue.Added);
    }

    [Fact(DisplayName = "入流_外网配置下匿名上报_被拒")]
    public async Task Anonymous_Internet_IsRejected()
    {
        var options = new EfbmOptions { Deployment = "Internet", RequireHttps = true, AuthMode = AuthModes.Anonymous };
        var f = new Fixture(options);

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(403, result.StatusCode);
        Assert.Empty(f.Queue.Added);
    }

    [Fact(DisplayName = "入流_内网匿名_通过且完全不查凭据库")]
    public async Task Anonymous_Intranet_PassesWithoutCredentialLookup()
    {
        var f = new Fixture(new EfbmOptions { AuthMode = AuthModes.Anonymous });

        Assert.Equal(200, (await f.Service.IngestAsync(Fixture.Request())).StatusCode);
        Assert.Equal(0, f.Credentials.CallCount);
    }

    [Fact(DisplayName = "入流_Redis不可用_写本地兜底仍返回200")]
    public async Task RedisDown_FallsBackAndStillReturns200()
    {
        var f = new Fixture();
        f.Queue.ThrowOnAdd = true;

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(200, result.StatusCode);
        Assert.False(result.Queued);
        Assert.True(result.Fallback);
        Assert.Equal("accepted-by-fallback", result.Message);
        Assert.Single(f.Fallback.Saved);
        Assert.Equal(result.MessageId, f.Fallback.Saved[0].MessageId);
        Assert.Contains("redis down", result.Warning);
    }

    [Fact(DisplayName = "入流_关闭本地兜底且Redis不可用_由文本日志兜住仍返回200")]
    public async Task RedisDown_WithoutFallback_IsCoveredByRequestLog()
    {
        var f = new Fixture(new EfbmOptions { LocalFallbackEnabled = false });
        f.Queue.ThrowOnAdd = true;

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("accepted-by-log", result.Message);
        Assert.False(result.Queued);
        Assert.False(result.Fallback);
        Assert.True(result.Logged);
        Assert.Empty(f.Fallback.Saved);
        Assert.Contains("未启用", result.Warning);
        Assert.Single(f.Log.Entries);
        Assert.Equal(Piece, f.Log.Entries[0].RawJson);
    }

    [Fact(DisplayName = "入流_纯日志模式_不写兜底表且返回accepted-by-log")]
    public async Task LogOnlyMode_SkipsFallbackTable()
    {
        var f = new Fixture(new EfbmOptions { RedisEnabled = false });
        f.Queue.ThrowOnAdd = true;          // 真实实现在 RedisEnabled=false 时也是直接抛，不进网络

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("accepted-by-log", result.Message);
        Assert.False(result.Queued);
        Assert.False(result.Fallback);
        Assert.True(result.Logged);
        Assert.Empty(f.Fallback.Saved);     // 日志已是唯一真相源，不再重复落库
        Assert.Contains("纯日志模式", result.Warning);
        // 日志里记录的状态必须与设备实际收到的响应一致（200 / accepted-by-log），不能写 503
        Assert.Equal(200, f.Log.Entries[0].StatusCode);
        Assert.Equal("accepted-by-log", f.Log.Entries[0].Message);
    }

    [Fact(DisplayName = "入流_队列兜底与日志三者全失效_返回503")]
    public async Task AllSinksDown_Returns503()
    {
        var f = new Fixture(new EfbmOptions { LocalFallbackEnabled = false });
        f.Queue.ThrowOnAdd = true;
        f.Log.ThrowOnWrite = true;

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(503, result.StatusCode);
        Assert.False(result.Logged);
        Assert.Equal("queue-unavailable", result.Message);
    }

    [Fact(DisplayName = "入流_队列与兜底同时失效_日志落盘则200且保留双原因")]
    public async Task BothDown_ButLogged_Returns200WithBothReasons()
    {
        var f = new Fixture();
        f.Queue.ThrowOnAdd = true;
        f.Fallback.ThrowOnSave = true;

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("accepted-by-log", result.Message);
        Assert.False(result.Queued);
        Assert.True(result.Logged);
        Assert.Contains("redis down", result.Warning);
        Assert.Contains("fallback db down", result.Warning);
        // 503 升级为 200 后仍要留下两个键，便于从日志补录后对齐幂等键
        Assert.Equal(40, result.EventKey.Length);
        Assert.Equal(result.EventKey, f.Log.Entries[0].EventKey);
        Assert.Equal(200, f.Log.Entries[0].StatusCode);
    }

    [Fact(DisplayName = "入流_队列兜底日志全部失效_才返回503且三个原因齐全")]
    public async Task AllDown_Returns503WithEveryReason()
    {
        var f = new Fixture();
        f.Queue.ThrowOnAdd = true;
        f.Fallback.ThrowOnSave = true;
        f.Log.ThrowOnWrite = true;

        var result = await f.Service.IngestAsync(Fixture.Request());

        Assert.Equal(503, result.StatusCode);
        Assert.False(result.Logged);
        Assert.Contains("redis down", result.Warning);
        Assert.Contains("fallback db down", result.Warning);
    }

    [Theory(DisplayName = "入流_未知事件未知状态时间偏差_一律正常入流且原文完整")]
    [InlineData("{\"event\":\"NEW_EVENT_X\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"foo\":{\"a\":1}}}")]
    [InlineData("{\"event\":\"DEVICE_STATUS_CHANGED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:15+08:00\",\"data\":{\"status\":\"WHATEVER\"}}")]
    [InlineData("{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T09:00:00+08:00\",\"data\":{\"completedQty\":-1}}")]
    public async Task TolerantContent_IsAlwaysAccepted(String raw)
    {
        var f = new Fixture();

        var result = await f.Service.IngestAsync(Fixture.Request(raw));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(raw, f.Queue.Added[0].RawJson);
    }

    [Fact(DisplayName = "入流_data新增字段_不改变EventKey且原文全量透传")]
    public async Task DataExtension_DoesNotChangeEventKey()
    {
        var f = new Fixture();
        var first = await f.Service.IngestAsync(Fixture.Request());
        var extended = Piece.Replace("\"completedQty\":1}", "\"completedQty\":1,\"boxType\":\"A\",\"operator\":{\"id\":7}}");
        var second = await f.Service.IngestAsync(Fixture.Request(extended));

        Assert.Equal(first.EventKey, second.EventKey);
        Assert.Equal(extended, f.Queue.Added[1].RawJson);
    }

    [Fact(DisplayName = "请求日志_成功失败都留痕_含设备事件状态与原文")]
    public async Task RequestLog_RecordsEveryOutcome()
    {
        var f = new Fixture();

        await f.Service.IngestAsync(Fixture.Request());
        await f.Service.IngestAsync(Fixture.Request("{\"event\":\"ORDER_STARTED\",}"));
        await f.Service.IngestAsync(Fixture.Request(key: null, secret: null));

        Assert.Equal(3, f.Log.Entries.Count);

        var ok = f.Log.Entries[0];
        Assert.Equal(200, ok.StatusCode);
        Assert.True(ok.Queued);
        Assert.Equal("accepted", ok.Message);
        Assert.Equal("ORDER_COMPLETED", ok.Event);
        Assert.Equal("FBOX-A01", ok.DeviceId);
        Assert.Equal("POST", ok.Method);
        Assert.Equal("/api/events", ok.Path);
        Assert.Equal("trace-1", ok.TraceId);
        Assert.Equal("FBox-Firmware/1.0", ok.UserAgent);
        Assert.Equal(Piece, ok.RawJson);

        Assert.Equal(400, f.Log.Entries[1].StatusCode);
        Assert.Equal("{\"event\":\"ORDER_STARTED\",}", f.Log.Entries[1].RawJson);

        // 鉴权失败同样要能定位是哪台设备、哪个事件在打
        Assert.Equal(401, f.Log.Entries[2].StatusCode);
        Assert.Equal("FBOX-A01", f.Log.Entries[2].DeviceId);
        Assert.Null(f.Log.Entries[2].AppKey);
    }

    [Fact(DisplayName = "请求日志_只落脱敏AppKey_绝不落AppSecret")]
    public async Task RequestLog_NeverStoresSecret()
    {
        var f = new Fixture();

        await f.Service.IngestAsync(Fixture.Request(key: "beta-fbox-a01"));

        var entry = f.Log.Entries[0];
        Assert.Equal("beta****01", entry.AppKey);
        Assert.Equal("****", RequestLogEntry.MaskKey("short"));
        Assert.Null(RequestLogEntry.MaskKey(null));

        var json = entry.ToJson();
        Assert.DoesNotContain("beta-fbox-a01", json);
        Assert.DoesNotContain("secret-1", json);
        Assert.DoesNotContain("\n", json);
    }

    [Fact(DisplayName = "兜底消息体_序列化往返_保留messageId与rawJson供回灌")]
    public void FallbackRoundTrip_KeepsIdentityAndRawJson()
    {
        var message = new StreamMessage
        {
            MessageId = "m-1",
            EventKey = "k-1",
            Event = "ORDER_COMPLETED",
            DeviceId = "FBOX-A01",
            Timestamp = "2026-07-23T18:00:10+08:00",
            RawJson = Piece,
            ReceivedAt = DateTime.UtcNow,
            SourceIp = "10.0.0.9",
            ReplayOf = "m-0",
        };

        var restored = EventIngestService.Deserialize(EventIngestService.Serialize(message));

        Assert.NotNull(restored);
        Assert.Equal("m-1", restored.MessageId);
        Assert.Equal("m-0", restored.ReplayOf);
        Assert.Equal(Piece, restored.RawJson);
        Assert.Null(EventIngestService.Deserialize("not json"));
        Assert.Null(EventIngestService.Deserialize(null));
    }
}
