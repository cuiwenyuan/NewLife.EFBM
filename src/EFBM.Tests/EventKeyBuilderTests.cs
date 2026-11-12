using EFBM.Core;

namespace EFBM.Tests;

/// <summary>三元组幂等键的契约测试（需求说明 §5.2、验收用例 C6 / C16）。</summary>
public class EventKeyBuilderTests
{
    [Fact(DisplayName = "幂等键_相同三元组_生成相同40位SHA1")]
    public void SameTriple_ProducesSameKey()
    {
        var a = EventKeyBuilder.Compute("ORDER_COMPLETED", "FBOX-A01", "2026-07-23T18:00:00+08:00");
        var b = EventKeyBuilder.Compute("ORDER_COMPLETED", "FBOX-A01", "2026-07-23T18:00:00+08:00");

        Assert.Equal(a, b);
        Assert.Equal(40, a.Length);
        Assert.Equal(a.ToLowerInvariant(), a);
    }

    [Fact(DisplayName = "幂等键_data变化_不改变键")]
    public void DataChanges_DoNotChangeKey()
    {
        var envelope = new EventEnvelope
        {
            Event = "ORDER_COMPLETED",
            DeviceId = "FBOX-A01",
            TimestampRaw = "2026-07-23T18:00:00+08:00",
            DataJson = "{\"completedQty\":1}",
        };
        var before = EventKeyBuilder.Compute(envelope);

        // 设备侧新增字段 / 嵌套对象，都不参与幂等键
        envelope.DataJson = "{\"completedQty\":1,\"boxType\":\"A\",\"operator\":{\"id\":7}}";
        var after = EventKeyBuilder.Compute(envelope);

        Assert.Equal(before, after);
    }

    [Theory(DisplayName = "幂等键_三元组任一变化_键都不同")]
    [InlineData("ORDER_STARTED", "FBOX-A01", "2026-07-23T18:00:00+08:00")]
    [InlineData("ORDER_COMPLETED", "FBOX-A02", "2026-07-23T18:00:00+08:00")]
    [InlineData("ORDER_COMPLETED", "FBOX-A01", "2026-07-23T18:00:01+08:00")]
    public void AnyPartOfTriple_ChangesKey(String eventName, String deviceId, String timestamp)
    {
        var baseline = EventKeyBuilder.Compute("ORDER_COMPLETED", "FBOX-A01", "2026-07-23T18:00:00+08:00");

        Assert.NotEqual(baseline, EventKeyBuilder.Compute(eventName, deviceId, timestamp));
    }

    [Fact(DisplayName = "幂等键_空字段_不抛异常")]
    public void NullParts_DoNotThrow()
    {
        // 缺字段由接收层判 400，但键计算本身必须宽容，不能在日志链路里二次抛异常
        var key = EventKeyBuilder.Compute(null, null, null);
        Assert.Equal(40, key.Length);
    }
}
