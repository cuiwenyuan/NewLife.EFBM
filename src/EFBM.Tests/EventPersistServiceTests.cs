using EFBM.Core;

namespace EFBM.Tests;

/// <summary>消费落库编排的幂等 / 恢复 / 死信语义（FR-5、FR-6、FR-12、FR-24、FR-26）。</summary>
/// <remarks>
/// 用内存替身覆盖编排层全部分支；真实 SQLite 与 handler 的组合在 <see cref="PersistPipelineTests"/> 验证。
/// </remarks>
public sealed class EventPersistServiceTests
{
    [Fact(DisplayName = "消费_首次落库_插入原始报文并执行handler后置位")]
    public async Task FirstMessage_PersistsAndMarksPersisted()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();
        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.Persisted, outcome);
        Assert.Single(raw.Items);
        Assert.Equal(RawProcessStatus.Persisted, raw.Items[0].ProcessStatus);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(message.EventKey, raw.Items[0].EventKey);
        Assert.Equal("1-1", raw.Items[0].StreamId);
        Assert.Equal(1, devices.CallCount);
        Assert.Empty(dead.Saved);
    }

    [Fact(DisplayName = "消费_重复投递_只累加RepeatCount不重复落库")]
    public async Task RedeliveredPersistedMessage_OnlyCountsRepeat()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();
        await persist.PersistAsync(message, "1-1");

        // 同一 EventKey 第二次投递（PEL 重投 / 组重建重放 / 人工重放）
        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.Duplicate, outcome);
        Assert.Single(raw.Items);               // 不产生第二条原始报文
        Assert.Equal(1, raw.Items[0].RepeatCount);
        Assert.Equal(1, handler.CallCount);     // handler 不重复执行
        Assert.Empty(dead.Saved);
    }

    [Fact(DisplayName = "消费_中断恢复_Pending状态重新执行handler后置位")]
    public async Task InterruptedPendingMessage_RerunsHandler()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();
        // 模拟上次处理到一半：已插入但未置位（进程崩溃）
        var inserted = await new FakeRawEventStore().InsertAsync(RawEventRecord.From(
            ParseEnvelope(message), "1-1"));
        raw.Items.Add(inserted);

        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.Persisted, outcome);
        Assert.Equal(RawProcessStatus.Persisted, raw.Items[0].ProcessStatus);
        Assert.Single(raw.Items);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact(DisplayName = "消费_handler失败_异常上抛不置位_由上层决定不确认")]
    public async Task HandlerFailure_ThrowsWithoutMarking()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted, ThrowOnHandle = true };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();

        await Assert.ThrowsAsync<InvalidOperationException>(() => persist.PersistAsync(message, "1-1"));

        // 原始报文已插入但保持 Pending：下一次重投会重新执行 handler
        Assert.Single(raw.Items);
        Assert.Equal(RawProcessStatus.Pending, raw.Items[0].ProcessStatus);
        Assert.Empty(dead.Saved);
    }

    [Fact(DisplayName = "消费_消息体损坏_重解析失败直接入死信并确认")]
    public async Task CorruptedMessage_GoesDeadLetter()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = new StreamMessage
        {
            MessageId = Guid.NewGuid().ToString("D"),
            EventKey = "deadbeef",
            Event = EventNames.OrderStarted,
            DeviceId = "FBOX-A01",
            RawJson = "{broken json",
        };

        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.DeadLettered, outcome);
        Assert.Single(dead.Saved);
        Assert.Contains("重解析失败", dead.Saved[0].LastError);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(raw.Items);   // 未插入原始报文
    }

    [Fact(DisplayName = "消费_并发插入冲突_唯一索引兜底后走重复路径")]
    public async Task DuplicateInsertOnRace_TakesIdempotentPath()
    {
        var raw = new FakeRawEventStore { ThrowDuplicateOnInsert = true };
        // 预置另一实例已插入并完成处理（模拟：本实例插入时对方刚插完并置位）
        var message = PersistServiceFactory.Message();
        var competitor = RawEventRecord.From(ParseEnvelope(message), "9-9");
        var store = new FakeRawEventStore();
        var competitorRecord = await store.InsertAsync(competitor);
        competitorRecord.ProcessStatus = RawProcessStatus.Persisted;
        raw.Items.Add(competitorRecord);

        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.Duplicate, outcome);
        Assert.Single(raw.Items);
        Assert.Equal(1, raw.Items[0].RepeatCount);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact(DisplayName = "消费_未知事件_走兜底handler原始报文照常落库")]
    public async Task UnknownEvent_GoesFallbackHandler()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message(eventName: "SOME_NEW_EVENT");
        var outcome = await persist.PersistAsync(message, "1-1");

        Assert.Equal(PersistOutcome.Persisted, outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(1, fallback.CallCount);
        Assert.Equal("SOME_NEW_EVENT", raw.Items[0].Event);
    }

    [Fact(DisplayName = "消费_重放消息_命中已落库EventKey跳过不覆盖")]
    public async Task ReplayMessage_SkipsPersistedEventKey()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();
        await persist.PersistAsync(message, "1-1");

        // 后台重放：同一 EventKey 的新消息（replayOf 指向原 messageId）
        message.ReplayOf = message.MessageId;
        message.MessageId = Guid.NewGuid().ToString("D");
        var outcome = await persist.PersistAsync(message, "2-2");

        Assert.Equal(PersistOutcome.Duplicate, outcome);
        Assert.Single(raw.Items);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact(DisplayName = "死信_写入成功_原始报文置Dead且消息可确认")]
    public async Task DeadLetterAsync_MarksRawDead()
    {
        var raw = new FakeRawEventStore();
        var dead = new FakeDeadLetterStore();
        var devices = new FakeDeviceRegistry();
        var handler = new FakeHandler { Event = EventNames.OrderStarted };
        var fallback = new FakeHandler { Event = IEventHandler.UnknownEventName };
        var persist = PersistServiceFactory.Build(raw, dead, devices, handler, fallback);

        var message = PersistServiceFactory.Message();
        var record = await raw.InsertAsync(RawEventRecord.From(ParseEnvelope(message), "1-1"));

        await persist.DeadLetterAsync(message, "1-1", 5, "连续 5 次失败");

        Assert.Single(dead.Saved);
        Assert.Equal(5, dead.Saved[0].RetryCount);
        Assert.Equal(RawProcessStatus.Dead, raw.Items[0].ProcessStatus);
        Assert.Equal(record.Id, raw.Items[0].Id);
    }

    [Fact(DisplayName = "注册表_事件重复注册或缺兜底_构造即失败")]
    public void EventHandlerRegistry_RejectsBadRegistration()
    {
        var handler = new FakeHandler { Event = EventNames.OrderStarted };

        Assert.Throws<InvalidOperationException>(() => new EventHandlerRegistry([handler, new FakeHandler { Event = EventNames.OrderStarted }]));
        Assert.Throws<InvalidOperationException>(() => new EventHandlerRegistry([handler]));
    }

    /// <summary>用与编排一致的路径解析一条测试报文。</summary>
    private static EventEnvelope ParseEnvelope(StreamMessage message)
    {
        Assert.True(EventEnvelopeParser.TryParse(message.RawJson, new ParseContext
        {
            MessageId = message.MessageId,
            ReceivedAt = message.ReceivedAt,
        }, out var envelope, out _));
        return envelope;
    }
}
