using EFBM.Core;
using EFBM.Data.Entities;

namespace EFBM.Web.Services;

/// <summary><c>DeviceEventRaw</c> 实体实现的原始报文存储（FR-6、FR-7）。</summary>
/// <remarks>
/// 并发插入同一 <c>EventKey</c> 时把底层的唯一索引冲突（SQLite 的 UNIQUE constraint failed /
/// MySQL 的 Duplicate entry）翻译为 <see cref="DuplicateEventKeyException"/>，交给编排层重新查询。
/// </remarks>
public sealed class EventRawStore : IRawEventStore
{
    /// <summary>按幂等键查找原始报文。</summary>
    /// <param name="eventKey">幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录视图；不存在返回 <c>null</c>。</returns>
    public Task<RawEventRecord> FindByEventKeyAsync(String eventKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (String.IsNullOrEmpty(eventKey)) return Task.FromResult<RawEventRecord>(null);

        var entity = DeviceEventRaw.FindAll(DeviceEventRaw._.EventKey == eventKey).FirstOrDefault();
        return Task.FromResult(entity == null ? null : ToRecord(entity));
    }

    /// <summary>插入一条待处理记录；撞唯一索引时抛 <see cref="DuplicateEventKeyException"/>。</summary>
    /// <param name="record">记录视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>插入后的记录（含主键）。</returns>
    public Task<RawEventRecord> InsertAsync(RawEventRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();

        var entity = new DeviceEventRaw
        {
            EventKey = record.EventKey,
            MessageId = record.MessageId,
            StreamId = record.StreamId,
            Event = record.Event,
            DeviceId = record.DeviceId,
            EventTimeRaw = record.EventTimeRaw,
            EventTime = record.EventTime == DateTime.MinValue ? DateTime.UtcNow : record.EventTime,
            RawJson = record.RawJson,
            DataJson = record.DataJson,
            ExtJson = record.ExtJson,
            UnknownFields = record.UnknownFields,
            ParseVersion = record.ParseVersion,
            ParseWarning = record.ParseWarning,
            SourceIp = record.SourceIp,
            ReceivedAt = record.ReceivedAt == DateTime.MinValue ? DateTime.UtcNow : record.ReceivedAt,
            ProcessStatus = record.ProcessStatus,
            ReplayOf = record.ReplayOf,
            RepeatCount = 0,
            PersistedAt = EfbmDefaults.UnrequeuedAt,
        };

        try
        {
            entity.Insert();
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            throw new DuplicateEventKeyException(record.EventKey);
        }

        record.Id = entity.Id;
        return Task.FromResult(record);
    }

    /// <summary>标记已落库。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task MarkPersistedAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = DeviceEventRaw.FindById(id);
        if (entity != null)
        {
            entity.ProcessStatus = RawProcessStatus.Persisted;
            entity.PersistedAt = DateTime.UtcNow;
            entity.Update();
        }

        return Task.CompletedTask;
    }

    /// <summary>标记死信。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task MarkDeadAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = DeviceEventRaw.FindById(id);
        if (entity != null)
        {
            entity.ProcessStatus = RawProcessStatus.Dead;
            entity.Update();
        }

        return Task.CompletedTask;
    }

    /// <summary>累加重复投递次数；只观测，不触碰任何业务数据。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task IncrementRepeatAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = DeviceEventRaw.FindById(id);
        if (entity != null)
        {
            entity.RepeatCount += 1;
            entity.Update();
        }

        return Task.CompletedTask;
    }

    /// <summary>实体转 Core 视图。</summary>
    private static RawEventRecord ToRecord(DeviceEventRaw entity) => new()
    {
        Id = entity.Id,
        EventKey = entity.EventKey,
        MessageId = entity.MessageId,
        StreamId = entity.StreamId,
        Event = entity.Event,
        DeviceId = entity.DeviceId,
        EventTimeRaw = entity.EventTimeRaw,
        EventTime = entity.EventTime,
        RawJson = entity.RawJson,
        DataJson = entity.DataJson,
        ExtJson = entity.ExtJson,
        UnknownFields = entity.UnknownFields,
        ParseVersion = entity.ParseVersion,
        ParseWarning = entity.ParseWarning,
        SourceIp = entity.SourceIp,
        ReceivedAt = entity.ReceivedAt,
        ProcessStatus = entity.ProcessStatus,
        RepeatCount = entity.RepeatCount,
        ReplayOf = entity.ReplayOf,
    };

    /// <summary>识别 SQLite / MySQL 的唯一索引冲突，便于编排层走幂等路径。</summary>
    private static Boolean IsDuplicateKey(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary><c>DeadLetter</c> 实体实现的死信存储（FR-12）。</summary>
public sealed class DeadLetterStore : IDeadLetterStore
{
    /// <summary>保存一条死信。</summary>
    /// <param name="record">死信记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task SaveAsync(DeadLetterRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();

        new DeadLetter
        {
            MessageId = record.MessageId,
            StreamId = record.StreamId,
            EventKey = record.EventKey,
            RawJson = record.RawJson,
            RetryCount = record.RetryCount,
            LastError = record.LastError,
            ReplayStatus = 0,
        }.Insert();

        return Task.CompletedTask;
    }
}

/// <summary><c>Device</c> 实体实现的设备登记（FR-13）。</summary>
/// <remarks>
/// <c>LastReportTime</c> 只取大更新（乱序不回退），且仅在变化时落库，稳态不产生多余写。
/// 自动登记关闭时不创建新设备，但已有设备的 <c>LastReportTime</c> 照常推进。
/// </remarks>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly EfbmOptions _options;

    /// <summary>构造设备登记。</summary>
    /// <param name="options">EFBM 配置节。</param>
    public DeviceRegistry(EfbmOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>确保设备存在并推进最后上报时间。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="eventTimeUtc">本条事件时间 UTC。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task EnsureDeviceAsync(String deviceId, DateTime eventTimeUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (String.IsNullOrEmpty(deviceId)) return Task.CompletedTask;
        if (eventTimeUtc == DateTime.MinValue) eventTimeUtc = DateTime.UtcNow;

        var entity = Device.FindAll(Device._.DeviceId == deviceId).FirstOrDefault();
        if (entity == null)
        {
            if (!_options.DeviceAutoRegister) return Task.CompletedTask;

            new Device
            {
                DeviceId = deviceId,
                Name = deviceId,
                LastReportTime = eventTimeUtc,
                Enabled = true,
            }.Insert();
            return Task.CompletedTask;
        }

        if (entity.LastReportTime < eventTimeUtc)
        {
            entity.LastReportTime = eventTimeUtc;
            entity.Update();
        }

        return Task.CompletedTask;
    }
}
