using System.Text.Json;
using EFBM.Core;
using EFBM.Data.Entities;

namespace EFBM.Web.Consumers.Handlers;

/// <summary>事件处理器共用的 <c>data</c> 取值工具：按名取值 + 缺省，不做任何拒绝。</summary>
/// <remarks>
/// 与解析层同一原则：字段缺失 / 类型不符一律给缺省值（告警已由 <c>ParseWarning</c> 打过），
/// 绝不因业务字段问题让消息失败——那会让它留在 PEL 里反复重投。
/// </remarks>
public static class HandlerData
{
    /// <summary>尝试解析 <c>data</c> 节点为 JSON 对象；空或非对象返回 <c>null</c>。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <returns>JSON 对象节点；无有效 data 时为 <c>null</c>。</returns>
    public static JsonElement? ParseData(EventEnvelope envelope)
    {
        if (envelope == null || String.IsNullOrWhiteSpace(envelope.DataJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(envelope.DataJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>按名取字符串；缺失或类型不符返回 <c>null</c>。</summary>
    /// <param name="data">data 节点。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字符串值或 <c>null</c>。</returns>
    public static String GetString(JsonElement? data, String name)
    {
        if (data == null) return null;
        if (!data.Value.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return null;

        var value = element.GetString();
        return String.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>按名取非负整数；缺失、类型不符或负数返回 <c>null</c>（告警已由解析层打标）。</summary>
    /// <param name="data">data 节点。</param>
    /// <param name="name">字段名。</param>
    /// <returns>整数值或 <c>null</c>。</returns>
    public static Int32? GetInt32(JsonElement? data, String name)
    {
        if (data == null) return null;
        if (!data.Value.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number) return null;

        return element.TryGetInt32(out var value) && value >= 0 ? value : null;
    }
}

/// <summary>工单聚合的共享规则：建 / 更工单、累计值取大收敛、派生完成状态（FR-9）。</summary>
/// <remarks>
/// 「乱序不回退」的三条铁律：
/// ① <c>CompletedQty</c> 只取大，旧值绝不覆盖新值；
/// ② <c>StartedAt</c> 只取小（工单开始事件晚到时把开始时间提前到真实起点）；
/// ③ <c>CompletedAt</c> 首次达标即定格，之后的重复事件不改写。
/// 工单不存在的任何产量事件都会先建工单（<c>StartedAt</c> 用本条事件时间占位），
/// 避免乱序到达的产量事件丢聚合。
/// </remarks>
public static class WorkOrderSync
{
    /// <summary>进行中状态文案。</summary>
    public const String StatusRunning = "进行中";

    /// <summary>已完成状态文案（由 completedQty &gt;= planQty 派生）。</summary>
    public const String StatusCompleted = "已完成";

    /// <summary>建 / 更工单并收敛累计值。</summary>
    /// <param name="envelope">上报信封。</param>
    /// <param name="orderId">工单号；为空时跳过（告警已由解析层打标）。</param>
    /// <param name="boxId">盒型 / 盒号原值；可为 <c>null</c>。</param>
    /// <param name="planQty">计划数量；可为 <c>null</c>（工单已有非零值时不覆盖）。</param>
    /// <param name="completedQty">累计完成数；可为 <c>null</c>（只取大收敛）。</param>
    public static void Upsert(EventEnvelope envelope, String orderId, String boxId, Int32? planQty, Int32? completedQty)
    {
        if (String.IsNullOrEmpty(orderId)) return;

        var eventTime = envelope.EventTimeUtc == DateTime.MinValue ? DateTime.UtcNow : envelope.EventTimeUtc;
        var entity = WorkOrder.FindAll(WorkOrder._.OrderId == orderId).FirstOrDefault();
        if (entity == null)
        {
            entity = new WorkOrder
            {
                OrderId = orderId,
                BoxId = boxId,
                DeviceId = envelope.DeviceId,
                PlanQty = planQty ?? 0,
                CompletedQty = completedQty ?? 0,
                Status = StatusRunning,
                StartedAt = eventTime,
                LastReportAt = eventTime,
                ExtJson = envelope.ExtJson,
                ParseVersion = envelope.ParseVersion,
            };

            if (entity.PlanQty > 0 && entity.CompletedQty >= entity.PlanQty)
            {
                entity.Status = StatusCompleted;
                entity.CompletedAt = eventTime;
            }

            entity.Insert();
            return;
        }

        var dirty = false;

        // 空位补齐：盒型 / 计划数量只在工单没有时才取报文值，不反向覆盖
        if (String.IsNullOrEmpty(entity.BoxId) && !String.IsNullOrEmpty(boxId)) { entity.BoxId = boxId; dirty = true; }
        if (entity.PlanQty == 0 && planQty is > 0) { entity.PlanQty = planQty.Value; dirty = true; }

        // 累计值取大收敛：旧值绝不覆盖新值（乱序 / 重复投递都安全）
        if (completedQty is > 0 && completedQty.Value > entity.CompletedQty)
        {
            entity.CompletedQty = completedQty.Value;
            dirty = true;
        }

        // 开始时间只取小：ORDER_STARTED 晚到时把起点提前到真实开工时刻
        if (eventTime < entity.StartedAt) { entity.StartedAt = eventTime; dirty = true; }

        if (eventTime > entity.LastReportAt) { entity.LastReportAt = eventTime; dirty = true; }

        // 派生完成状态：首次达标定格，之后不改写
        if (entity.Status != StatusCompleted && entity.PlanQty > 0 && entity.CompletedQty >= entity.PlanQty)
        {
            entity.Status = StatusCompleted;
            entity.CompletedAt = eventTime;
            dirty = true;
        }

        if (dirty) entity.Update();
    }
}
