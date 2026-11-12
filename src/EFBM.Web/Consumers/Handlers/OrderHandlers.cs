using EFBM.Core;
using EFBM.Data.Entities;
using EFBM.Web.Consumers.Handlers;

namespace EFBM.Web.Consumers.Handlers;

/// <summary>工单开始生产：建 / 更工单并写入开工时间（需求说明 §3.3）。</summary>
/// <remarks>
/// 乱序安全：工单不存在则建（<c>StartedAt</c> = 本条事件时间）；已存在且本条时间更早则把
/// <c>StartedAt</c> 提前（开始事件晚到），更晚则不动——开工时间只取小。
/// </remarks>
public sealed class OrderStartedHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.OrderStarted;

    /// <summary>处理工单开始事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = HandlerData.ParseData(envelope);
        WorkOrderSync.Upsert(
            envelope,
            HandlerData.GetString(data, "orderId"),
            HandlerData.GetString(data, "boxId"),
            planQty: null,
            completedQty: null);

        return Task.CompletedTask;
    }
}

/// <summary>产量里程碑：写产量记录（非逐件）+ 累计值收敛（需求说明 §3.3）。</summary>
public sealed class OrderProgressHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.OrderProgress;

    /// <summary>处理产量里程碑事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OrderQtyHandlerCore.HandleAsync(envelope, isPiece: false);
    }
}

/// <summary>逐件完成：逐件明细全量保存 + 累计值收敛 + 派生完成状态（FR-8、FR-9）。</summary>
/// <remarks><c>ORDER_COMPLETED</c> 每折好一个盒子上报一次，事件名沿用对方报文。</remarks>
public sealed class OrderCompletedHandler : IEventHandler
{
    /// <summary>本处理器负责的事件名。</summary>
    public String Event => EventNames.OrderCompleted;

    /// <summary>处理逐件完成事件。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OrderQtyHandlerCore.HandleAsync(envelope, isPiece: true);
    }
}

/// <summary>产量类事件（里程碑 / 逐件完成）的共用落库逻辑。</summary>
internal static class OrderQtyHandlerCore
{
    /// <summary>写一条产量记录（EventKey 查重保证重投幂等）并收敛工单累计值。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="isPiece">是否逐件完成事件。</param>
    /// <returns>异步任务。</returns>
    public static Task HandleAsync(EventEnvelope envelope, Boolean isPiece)
    {
        var data = HandlerData.ParseData(envelope);
        var orderId = HandlerData.GetString(data, "orderId");
        var eventKey = envelope.EventKey;

        // 逐件明细 / 产量记录全量保存：唯一键查重，重投（含中断恢复）不会产生第二条
        if (!String.IsNullOrEmpty(orderId) && !String.IsNullOrEmpty(eventKey))
        {
            var exists = ProductionRecord.FindAll(ProductionRecord._.EventKey == eventKey).Any();
            if (!exists)
            {
                new ProductionRecord
                {
                    EventKey = eventKey,
                    MessageId = envelope.MessageId,
                    OrderId = orderId,
                    BoxId = HandlerData.GetString(data, "boxId"),
                    DeviceId = envelope.DeviceId,
                    IsPiece = isPiece,
                    PlanQty = HandlerData.GetInt32(data, "planQty") ?? 0,
                    CompletedQty = HandlerData.GetInt32(data, "completedQty") ?? 0,
                    EventTime = envelope.EventTimeUtc == DateTime.MinValue ? DateTime.UtcNow : envelope.EventTimeUtc,
                    ExtJson = envelope.ExtJson,
                    ParseVersion = envelope.ParseVersion,
                }.Insert();
            }
        }

        WorkOrderSync.Upsert(
            envelope,
            orderId,
            HandlerData.GetString(data, "boxId"),
            HandlerData.GetInt32(data, "planQty"),
            HandlerData.GetInt32(data, "completedQty"));

        return Task.CompletedTask;
    }
}
