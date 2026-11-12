using System.Text.Json;
using EFBM.Core;

namespace EFBM.Tests;

/// <summary>Stream 消息体契约测试：字段透传与 camelCase 序列化（需求说明 §3.6、验收用例 C9 / C12）。</summary>
public class StreamMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName = "Stream消息_由信封构造_复制全部契约字段")]
    public void FromEnvelope_CopiesContractFields()
    {
        var envelope = new EventEnvelope
        {
            Event = "ORDER_COMPLETED",
            DeviceId = "FBOX-A01",
            TimestampRaw = "2026-07-23T18:00:00+08:00",
            RawJson = "{\"event\":\"ORDER_COMPLETED\",\"data\":{\"boxType\":\"A\"}}",
            MessageId = "m-1",
            EventKey = "k-1",
            SourceIp = "10.0.0.1",
            ReceivedAt = new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc),
            ReplayOf = "m-0",
        };

        var message = StreamMessage.FromEnvelope(envelope);

        Assert.Equal("ORDER_COMPLETED", message.Event);
        Assert.Equal("FBOX-A01", message.DeviceId);
        Assert.Equal(envelope.TimestampRaw, message.Timestamp);
        Assert.Equal(envelope.RawJson, message.RawJson);
        Assert.Equal("m-1", message.MessageId);
        Assert.Equal("k-1", message.EventKey);
        Assert.Equal("10.0.0.1", message.SourceIp);
        Assert.Equal(envelope.ReceivedAt, message.ReceivedAt);
        Assert.Equal("m-0", message.ReplayOf);
    }

    [Fact(DisplayName = "Stream消息_JSON往返_camelCase且原文不变")]
    public void JsonRoundTrip_UsesCamelCaseAndKeepsRawJson()
    {
        const String raw = "{\"event\":\"ORDER_COMPLETED\",\"data\":{\"boxType\":\"A\",\"operator\":{\"id\":7}}}";
        var message = new StreamMessage
        {
            MessageId = "m-1",
            EventKey = "k-1",
            Event = "ORDER_COMPLETED",
            DeviceId = "FBOX-A01",
            Timestamp = "2026-07-23T18:00:00+08:00",
            RawJson = raw,
            ReceivedAt = DateTime.UtcNow,
            SourceIp = "127.0.0.1",
            ReplayOf = null,
        };

        var json = JsonSerializer.Serialize(message, Options);

        Assert.Contains("\"messageId\"", json);
        Assert.Contains("\"rawJson\"", json);
        Assert.DoesNotContain("\"MessageId\"", json);

        var restored = JsonSerializer.Deserialize<StreamMessage>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal("m-1", restored.MessageId);
        Assert.Equal(raw, restored.RawJson);   // 新增字段原样透传，不被改写
        Assert.Null(restored.ReplayOf);
    }
}
