using EFBM.Core;

namespace EFBM.Tests;

/// <summary>消费链路测试替身：原始报文 / 死信 / 设备登记三个抽象的内存实现。</summary>
internal sealed class FakeRawEventStore : IRawEventStore
{
    public List<RawEventRecord> Items { get; } = [];
    public Boolean ThrowOnInsert { get; set; }
    public Boolean ThrowDuplicateOnInsert { get; set; }
    public Int32 RepeatCalls { get; private set; }
    public Int64 LastMarkedId { get; private set; }

    public Task<RawEventRecord> FindByEventKeyAsync(String eventKey, CancellationToken cancellationToken = default)
    {
        var record = Items.FirstOrDefault(r => String.Equals(r.EventKey, eventKey, StringComparison.Ordinal));
        return Task.FromResult(record);
    }

    public Task<RawEventRecord> InsertAsync(RawEventRecord record, CancellationToken cancellationToken = default)
    {
        if (ThrowOnInsert) throw new InvalidOperationException("raw db down");
        if (ThrowDuplicateOnInsert)
        {
            // 模拟另一实例已插入：先放进去再抛，让编排层重新查询能查到
            Items.Add(record);
            throw new DuplicateEventKeyException(record.EventKey);
        }

        record.Id = Items.Count + 1;
        Items.Add(record);
        return Task.FromResult(record);
    }

    public Task MarkPersistedAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        LastMarkedId = id;
        var record = Items.FirstOrDefault(r => r.Id == id);
        if (record != null) record.ProcessStatus = RawProcessStatus.Persisted;
        return Task.CompletedTask;
    }

    public Task MarkDeadAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        var record = Items.FirstOrDefault(r => r.Id == id);
        if (record != null) record.ProcessStatus = RawProcessStatus.Dead;
        return Task.CompletedTask;
    }

    public Task IncrementRepeatAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        RepeatCalls++;
        var record = Items.FirstOrDefault(r => r.Id == id);
        if (record != null) record.RepeatCount += 1;
        return Task.CompletedTask;
    }

    /// <summary>直接预置一条已落库记录，模拟重复投递。</summary>
    public FakeRawEventStore WithPersisted(String eventKey)
    {
        Items.Add(new RawEventRecord { Id = Items.Count + 1, EventKey = eventKey, ProcessStatus = RawProcessStatus.Persisted, RepeatCount = 0 });
        return this;
    }
}

/// <summary>死信存储替身。</summary>
internal sealed class FakeDeadLetterStore : IDeadLetterStore
{
    public List<DeadLetterRecord> Saved { get; } = [];
    public Boolean ThrowOnSave { get; set; }

    public Task SaveAsync(DeadLetterRecord record, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave) throw new InvalidOperationException("dead letter db down");
        Saved.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>设备登记替身：记录调用，可开关自动登记。</summary>
internal sealed class FakeDeviceRegistry : IDeviceRegistry
{
    public Int32 CallCount { get; private set; }
    public List<String> Devices { get; } = [];
    public Boolean AutoRegister { get; set; } = true;

    public Task EnsureDeviceAsync(String deviceId, DateTime eventTimeUtc, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (AutoRegister && !String.IsNullOrEmpty(deviceId) && !Devices.Contains(deviceId)) Devices.Add(deviceId);
        return Task.CompletedTask;
    }
}

/// <summary>事件处理器替身：可数、可失败、可当兜底。</summary>
internal sealed class FakeHandler : IEventHandler
{
    public String Event { get; init; }
    public Int32 CallCount { get; private set; }
    public Boolean ThrowOnHandle { get; set; }
    public List<EventEnvelope> Seen { get; } = [];

    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Seen.Add(envelope);
        if (ThrowOnHandle) throw new InvalidOperationException("handler boom");
        return Task.CompletedTask;
    }
}

/// <summary>用替身构建编排服务的便捷工厂。</summary>
internal static class PersistServiceFactory
{
    /// <summary>用给定替身构建 <see cref="EventPersistService"/>。</summary>
    public static EventPersistService Build(
        IRawEventStore raw, IDeadLetterStore deadLetter, IDeviceRegistry devices, params IEventHandler[] handlers)
    {
        var options = new EfbmOptions { DeviceAutoRegister = true };
        return new EventPersistService(options, raw, deadLetter, devices, new EventHandlerRegistry(handlers));
    }

    /// <summary>构造一条合法的测试报文；<paramref name="dataJson"/> 传 data 节点的内容（不含外层 data 包装）。</summary>
    public static StreamMessage Message(String eventName = "ORDER_STARTED", String deviceId = "FBOX-A01", String timestamp = "2026-11-12T10:00:00+08:00", String dataJson = null)
    {
        var raw = $"{{\"event\":\"{eventName}\",\"deviceId\":\"{deviceId}\",\"timestamp\":\"{timestamp}\"";
        if (dataJson != null) raw += $",\"data\":{{{dataJson}}}";
        raw += "}";

        return new StreamMessage
        {
            MessageId = Guid.NewGuid().ToString("D"),
            EventKey = EventKeyBuilder.Compute(eventName, deviceId, timestamp),
            Event = eventName,
            DeviceId = deviceId,
            Timestamp = timestamp,
            RawJson = raw,
            ReceivedAt = DateTime.UtcNow,
            SourceIp = "127.0.0.1",
        };
    }
}
