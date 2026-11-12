using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Consumers.Handlers;

namespace EFBM.Web.Consumers.Handlers;

/// <summary>设备状态切换：写状态流水 + 更新设备当前状态原值（FR-10）。</summary>
/// <remarks>
/// <c>StatusRaw</c> 原值存储，不做枚举校验；展示层用 <c>StatusDict</c> 映射，未命中原样显示。
/// 未知状态照常落库（解析层已打 W104 告警）。
/// </remarks>
public sealed class DeviceStatusChangedHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.DeviceStatusChanged;

    /// <summary>处理状态切换事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = HandlerData.ParseData(envelope);
        var status = HandlerData.GetString(data, "status");
        var eventKey = envelope.EventKey;

        // 状态流水：EventKey 查重保证重投幂等
        if (!String.IsNullOrEmpty(eventKey))
        {
            var exists = DeviceStatusLog.FindAll(DeviceStatusLog._.EventKey == eventKey).Any();
            if (!exists)
            {
                new DeviceStatusLog
                {
                    EventKey = eventKey,
                    MessageId = envelope.MessageId,
                    DeviceId = envelope.DeviceId,
                    StatusRaw = status,
                    EventTime = envelope.EventTimeUtc == DateTime.MinValue ? DateTime.UtcNow : envelope.EventTimeUtc,
                    ExtJson = envelope.ExtJson,
                    ParseVersion = envelope.ParseVersion,
                }.Insert();
            }
        }

        // 设备当前状态原值更新（设备记录由编排层统一登记 / 推进 LastReportTime）
        if (!String.IsNullOrEmpty(envelope.DeviceId))
        {
            var device = Device.FindAll(Device._.DeviceId == envelope.DeviceId).FirstOrDefault();
            if (device != null && device.StatusRaw != status)
            {
                device.StatusRaw = status;
                device.Update();
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>故障发生：新增故障记录（需求说明 §3.3、FR-9）。</summary>
/// <remarks>EventKey 查重：重投 / 中断恢复不会重复报同一故障。</remarks>
public sealed class FaultOccurredHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.FaultOccurred;

    /// <summary>处理故障发生事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var eventKey = envelope.EventKey;
        if (String.IsNullOrEmpty(eventKey)) return Task.CompletedTask;

        var exists = FaultRecord.FindAll(FaultRecord._.EventKey == eventKey).Any();
        if (exists) return Task.CompletedTask;

        var data = HandlerData.ParseData(envelope);
        new FaultRecord
        {
            EventKey = eventKey,
            DeviceId = envelope.DeviceId,
            Message = HandlerData.GetString(data, "message"),
            OccurredAt = envelope.EventTimeUtc == DateTime.MinValue ? DateTime.UtcNow : envelope.EventTimeUtc,
            ClearedAt = EfbmDefaults.UnrequeuedAt,
            Status = 0,
            ExtJson = envelope.ExtJson,
            ParseVersion = envelope.ParseVersion,
        }.Insert();

        return Task.CompletedTask;
    }
}

/// <summary>故障清除：关闭最近一条未清除故障（需求说明 §3.3、FR-9）。</summary>
/// <remarks>
/// 没有 data，只按 <c>deviceId</c> 找 <c>OccurredAt</c> 最新且未清除的故障关闭。
/// 找不到未清除故障时<b>宽容跳过</b>（清除先于发生到达、或已手动关闭），只记告警日志，
/// 绝不抛异常让消息卡在 PEL。幂等：已由本 EventKey 关闭过的故障不再改写。
/// </remarks>
public sealed class FaultClearedHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.FaultCleared;

    /// <summary>处理故障清除事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var eventTime = envelope.EventTimeUtc == DateTime.MinValue ? DateTime.UtcNow : envelope.EventTimeUtc;
        var fault = FaultRecord
            .FindAll(FaultRecord._.DeviceId == envelope.DeviceId & FaultRecord._.Status == 0)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();

        if (fault == null)
        {
            NewLife.Log.XTrace.WriteLine("故障清除无未清除记录可关闭 deviceId={0} eventKey={1}（清除先到或已关闭，跳过）", envelope.DeviceId, envelope.EventKey);
            return Task.CompletedTask;
        }

        fault.ClearedAt = eventTime;
        fault.ClearEventKey = envelope.EventKey;
        fault.Status = 1;
        fault.Update();

        return Task.CompletedTask;
    }
}

/// <summary>未知事件兜底：只存原始报文（由编排层统一落 <c>DeviceEventRaw</c>），不写聚合表。</summary>
/// <remarks>
/// FR-26：未知事件必须原样落库；补上正式 handler 后可从 <c>RawJson</c> 重放解析。
/// </remarks>
public sealed class UnknownEventHandler : IEventHandler
{
    /// <summary>兜底注册键，命中所有未登记事件。</summary>
    public String Event => IEventHandler.UnknownEventName;

    /// <summary>空实现：原始报文已由编排层保证留存。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
