using System.Text.Json;
using EFBM.Core;

namespace EFBM.Tests;

/// <summary>严格解析 + 宽容字段的契约测试（需求说明 §3.5 / §3.7、验收用例 C2 / C4 / C16 / C17）。</summary>
public class EventEnvelopeParserTests
{
    private const String Piece = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":1}}";

    /// <summary>6 类事件的合法样例。</summary>
    /// <returns>报文原文集合。</returns>
    public static IEnumerable<Object[]> ValidSamples()
    {
        yield return ["{\"event\":\"ORDER_STARTED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:00+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\"}}"];
        yield return ["{\"event\":\"ORDER_PROGRESS\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:05+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":100}}"];
        yield return [Piece];
        yield return ["{\"event\":\"DEVICE_STATUS_CHANGED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:15+08:00\",\"data\":{\"status\":\"RUNNING\"}}"];
        yield return ["{\"event\":\"FAULT_OCCURRED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:20+08:00\",\"data\":{\"message\":\"报错信息\"}}"];
        yield return ["{\"event\":\"FAULT_CLEARED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:25+08:00\"}"];
    }

    [Theory(DisplayName = "TryParse_六类合法事件_全部成功入流")]
    [MemberData(nameof(ValidSamples))]
    public void ValidSamples_AreAccepted(String raw)
    {
        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var envelope, out var error), error);
        Assert.NotNull(envelope);
        Assert.Equal(String.Empty, envelope.ParseWarning);
        Assert.Equal(EfbmDefaults.ParseVersion, envelope.ParseVersion);
        Assert.Equal(EventKeyBuilder.Compute(envelope.Event, envelope.DeviceId, envelope.TimestampRaw), envelope.EventKey);
    }

    [Fact(DisplayName = "TryParse_原文与生成字段_逐字保留且键为UUID")]
    public void RawJson_IsPreservedVerbatim()
    {
        var raw = " { \"event\" : \"ORDER_COMPLETED\", \"deviceId\":\"FBOX-A01\", \"timestamp\":\"2026-07-23T18:00:10+08:00\", \"data\" : { \"orderId\":\"WO-001\", \"boxId\":\"BOX-0001\", \"planQty\":1000, \"completedQty\":1 } } ";
        var receivedAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        var context = new ParseContext { MessageId = "m-fixed", ReceivedAt = receivedAt, SourceIp = "10.0.0.9", ReplayOf = "m-old" };

        Assert.True(EventEnvelopeParser.TryParse(raw, context, out var env, out _));

        Assert.Equal(raw, env.RawJson);                  // 空格与顺序都不改写
        Assert.Equal("m-fixed", env.MessageId);
        Assert.Equal(receivedAt, env.ReceivedAt);
        Assert.Equal("10.0.0.9", env.SourceIp);
        Assert.Equal("m-old", env.ReplayOf);
        Assert.Equal("1.0", env.ParseVersion);
        Assert.Contains("\"orderId\"", env.DataJson);   // GetRawText 连内部空格也保留
        Assert.Contains(env.DataJson, env.RawJson);
        Assert.Equal(new DateTime(2026, 7, 23, 10, 0, 10, DateTimeKind.Utc), env.EventTimeUtc);
    }

    [Fact(DisplayName = "TryParse_自动生成MessageId_为标准UUID格式")]
    public void MessageId_IsGeneratedWhenMissing()
    {
        Assert.True(EventEnvelopeParser.TryParse(Piece, null, out var env, out _));
        Assert.True(Guid.TryParseExact(env.MessageId, "D", out _));
    }

    [Theory(DisplayName = "TryParse_非法JSON或尾随逗号_返回400并丢弃")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"event\":\"ORDER_STARTED\",}")]
    [InlineData("{\"event\":\"ORDER_STARTED\"} // 注释")]
    [InlineData("{\"event\":\"ORDER_STARTED\"")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void InvalidJson_IsRejected(String raw)
    {
        Assert.False(EventEnvelopeParser.TryParse(raw, null, out var env, out var error));
        Assert.Null(env);
        Assert.False(String.IsNullOrEmpty(error));
    }

    [Theory(DisplayName = "TryParse_三个必填字段缺失或类型非法_返回400并丢弃")]
    [InlineData("{\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:00+08:00\"}")]
    [InlineData("{\"event\":\"ORDER_STARTED\",\"timestamp\":\"2026-07-23T18:00:00+08:00\"}")]
    [InlineData("{\"event\":\"ORDER_STARTED\",\"deviceId\":\"FBOX-A01\"}")]
    [InlineData("{\"event\":123,\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:00+08:00\"}")]
    [InlineData("{\"event\":\"  \",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:00+08:00\"}")]
    [InlineData("{\"event\":\"ORDER_STARTED\",\"deviceId\":null,\"timestamp\":\"2026-07-23T18:00:00+08:00\"}")]
    public void MissingRequiredFields_AreRejected(String raw)
    {
        Assert.False(EventEnvelopeParser.TryParse(raw, null, out _, out var error));
        Assert.Contains("必填字段", error);
    }

    [Fact(DisplayName = "TryParse_未知事件_打W103仍正常入流")]
    public void UnknownEvent_IsAcceptedWithWarning()
    {
        const String raw = "{\"event\":\"NEW_EVENT_X\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:00+08:00\",\"data\":{\"foo\":1}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Equal("NEW_EVENT_X", env.Event);
        Assert.Contains(ParseWarnings.UnknownEvent, env.ParseWarning);
        Assert.Contains(ParseWarnings.UnknownField, env.ParseWarning);
    }

    [Theory(DisplayName = "TryParse_未知或空status_打W104仍原样入流")]
    [InlineData("RUNNING", false)]
    [InlineData("IDLE", false)]
    [InlineData("UNKNOWN_STATE", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void Status_IsNeverValidated(String status, Boolean expectWarning)
    {
        var raw = status == null
            ? "{\"event\":\"DEVICE_STATUS_CHANGED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:15+08:00\",\"data\":{}}"
            : $"{{\"event\":\"DEVICE_STATUS_CHANGED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:15+08:00\",\"data\":{{\"status\":\"{status}\"}}}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Equal(expectWarning, env.ParseWarning.Contains(ParseWarnings.UnknownStatus));
    }

    [Fact(DisplayName = "TryParse_新增字段嵌套对象与数组_全量留存且只打W102")]
    public void UnknownFields_AreCollectedButNeverRejected()
    {
        const String raw = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":1,\"boxType\":\"A\",\"operator\":{\"id\":7},\"tags\":[\"x\",\"y\"]}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Contains(ParseWarnings.UnknownField, env.ParseWarning);
        Assert.Contains("boxType", env.UnknownFields);
        Assert.Contains("operator", env.UnknownFields);
        Assert.Contains("tags", env.UnknownFields);

        using var doc = JsonDocument.Parse(env.ExtJson);
        Assert.Equal("A", doc.RootElement.GetProperty("boxType").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("operator").GetProperty("id").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("tags").GetArrayLength());
        Assert.Equal(raw, env.RawJson);
    }

    [Fact(DisplayName = "TryParse_顶层未知字段_也计入UnknownFields并打W102")]
    public void TopLevelUnknownField_IsCollected()
    {
        const String raw = "{\"event\":\"FAULT_CLEARED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:25+08:00\",\"traceId\":\"t-1\"}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Contains("traceId", env.UnknownFields);
        Assert.Contains(ParseWarnings.UnknownField, env.ParseWarning);
    }

    [Fact(DisplayName = "TryParse_已识别字段缺失或类型不符_打W100与W101不拒绝")]
    public void MissingOrMismatchedKnownFields_OnlyWarn()
    {
        const String raw = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":{\"planQty\":\"1000\",\"completedQty\":-5}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Contains(ParseWarnings.MissingField, env.ParseWarning);   // 缺 orderId / boxId
        Assert.Contains(ParseWarnings.TypeMismatch, env.ParseWarning);   // planQty 是字符串
        Assert.Contains(ParseWarnings.BadNumber, env.ParseWarning);      // completedQty 为负
    }

    [Fact(DisplayName = "TryParse_data不是对象_打W106并保留原值")]
    public void DataNotObject_OnlyWarns()
    {
        const String raw = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\",\"data\":[1,2]}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Contains(ParseWarnings.DataNotObject, env.ParseWarning);
        Assert.Equal("[1,2]", env.DataJson);
    }

    [Fact(DisplayName = "TryParse_重复JSON键_打W108并以最后一次取值为准")]
    public void DuplicateKey_TakesLastValue()
    {
        const String raw = "{\"event\":\"ORDER_STARTED\",\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:10+08:00\"}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Equal("ORDER_COMPLETED", env.Event);
        Assert.Contains(ParseWarnings.DuplicateKey, env.ParseWarning);
    }

    [Theory(DisplayName = "TryParse_时间无偏移或无法解析_打W105仍入流")]
    [InlineData("2026-07-23T18:00:00")]
    [InlineData("not-a-time")]
    public void BadTimestamp_OnlyWarns(String timestamp)
    {
        var raw = $"{{\"event\":\"FAULT_CLEARED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"{timestamp}\"}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Contains(ParseWarnings.BadTimestamp, env.ParseWarning);
        Assert.Equal(timestamp, env.TimestampRaw);
    }

    [Fact(DisplayName = "TryParse_时间偏差一小时_不被拦截")]
    public void TimeSkew_IsNeverRejected()
    {
        const String raw = "{\"event\":\"ORDER_COMPLETED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T09:00:00+08:00\",\"data\":{\"orderId\":\"WO-001\",\"boxId\":\"BOX-0001\",\"planQty\":1000,\"completedQty\":1}}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out _, out _));
    }

    [Fact(DisplayName = "TryParse_新增业务字段_EventKey保持不变")]
    public void EventKey_IgnoresDataChanges()
    {
        Assert.True(EventEnvelopeParser.TryParse(Piece, null, out var before, out _));
        var withExtra = Piece.Replace("\"completedQty\":1}", "\"completedQty\":1,\"boxType\":\"A\"}");
        Assert.True(EventEnvelopeParser.TryParse(withExtra, null, out var after, out _));

        Assert.Equal(before.EventKey, after.EventKey);
    }

    [Fact(DisplayName = "TryParse_FAULT_CLEARED无data_不产生缺失字段告警")]
    public void FaultCleared_WithoutData_HasNoWarning()
    {
        const String raw = "{\"event\":\"FAULT_CLEARED\",\"deviceId\":\"FBOX-A01\",\"timestamp\":\"2026-07-23T18:00:25+08:00\"}";

        Assert.True(EventEnvelopeParser.TryParse(raw, null, out var env, out _));
        Assert.Equal(String.Empty, env.DataJson);
        Assert.Equal(String.Empty, env.ExtJson);
        Assert.Equal(String.Empty, env.UnknownFields);
        Assert.Equal(String.Empty, env.ParseWarning);
    }
}
