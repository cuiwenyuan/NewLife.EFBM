using EFBM.Core;

namespace EFBM.Tests;

/// <summary>字段字典初版（ParseVersion=1.0）的契约测试，对应《Doc/上报字段字典.md》§3 与验收用例 C16。</summary>
public class EventFieldRegistryTests
{
    [Fact(DisplayName = "字段字典_解析版本冻结为1.0")]
    public void ParseVersion_IsFrozenAtOneZero()
    {
        Assert.Equal("1.0", EventFieldRegistry.ParseVersion);
        Assert.Equal(EventFieldRegistry.ParseVersion, EfbmDefaults.ParseVersion);
    }

    [Theory(DisplayName = "字段字典_各已知事件_登记字段数正确")]
    [InlineData(EventNames.OrderStarted, 2)]
    [InlineData(EventNames.OrderProgress, 4)]
    [InlineData(EventNames.OrderCompleted, 4)]
    [InlineData(EventNames.DeviceStatusChanged, 1)]
    [InlineData(EventNames.FaultOccurred, 1)]
    [InlineData(EventNames.FaultCleared, 0)]
    public void KnownEvents_HaveExpectedFieldCount(String eventName, Int32 expected)
    {
        Assert.True(EventNames.IsKnown(eventName));
        Assert.Equal(expected, EventFieldRegistry.GetFields(eventName).Length);
    }

    [Fact(DisplayName = "字段字典_逐件完成_含当前六个已登记字段")]
    public void OrderCompleted_HasCurrentSixDataFieldsAmongThem()
    {
        var names = EventFieldRegistry.GetFields(EventNames.OrderCompleted).Select(e => e.Name).ToArray();

        Assert.Contains("orderId", names);
        Assert.Contains("boxId", names);
        Assert.Contains("planQty", names);
        Assert.Contains("completedQty", names);
    }

    [Fact(DisplayName = "字段字典_未知字段_被识别但不作为拒绝依据")]
    public void UnknownFields_AreDetectedButNotRejected()
    {
        var fields = new[] { "orderId", "boxType", "operatorId", "orderId" };
        var unknown = EventFieldRegistry.FindUnknown(EventNames.OrderCompleted, fields);

        Assert.Equal(new[] { "boxType", "operatorId" }, unknown);
        Assert.True(EventFieldRegistry.IsKnown(EventNames.OrderCompleted, "orderId"));
        Assert.False(EventFieldRegistry.IsKnown(EventNames.OrderCompleted, "boxType"));
    }

    [Fact(DisplayName = "字段字典_未知事件_无登记字段且不在白名单")]
    public void UnknownEvent_HasNoFieldsAndIsNotInWhitelist()
    {
        // 未知事件没有已登记字段，但接收端仍须原样入流：注册表不是白名单
        const String newEvent = "NEW_EVENT_X";

        Assert.False(EventNames.IsKnown(newEvent));
        Assert.Empty(EventFieldRegistry.GetFields(newEvent));
        // 未知事件的字段全部进入 UnknownFields，但接收端不拒绝报文
        Assert.Equal(new[] { "anyField" }, EventFieldRegistry.FindUnknown(newEvent, new[] { "anyField" }));
    }
}
