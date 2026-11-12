namespace EFBM.Core;

/// <summary>当前字段字典（<c>ParseVersion=1.0</c>）的代码化清单，见《Doc/上报字段字典.md》。</summary>
/// <remarks>
/// 这是<b>解析、展示与监测用的登记表，不是字段白名单</b>。未登记字段一律不拒绝：
/// 原样写入 <c>RawJson</c> / <c>ExtJson</c> / <c>UnknownFields</c> 并透传给所有消费组。
/// 对方提供新字段清单时：更新本表与字典文档 → 递增 <c>ParseVersion</c> → 需要查询时按需求说明 §3.7 固化并回溯回填历史。
/// </remarks>
public static class EventFieldRegistry
{
    /// <summary>字段元数据。</summary>
    public sealed class Field
    {
        /// <summary>报文字段名。</summary>
        public String Name { get; init; }

        /// <summary>中文名。</summary>
        public String DisplayName { get; init; }

        /// <summary>JSON 类型：String / Number(Int32) / Object。</summary>
        public String JsonType { get; init; }

        /// <summary>适用事件名；空数组表示顶层信封字段。</summary>
        public String[] Events { get; init; }

        /// <summary>业务上是否应当存在；缺失只打 W100，不拒绝。</summary>
        public Boolean Required { get; init; }

        /// <summary>已固化的落库列；未固化为 <c>ExtJson</c>。</summary>
        public String Column { get; init; }

        /// <summary>是否已有正式列。</summary>
        public Boolean Solidified { get; init; }

        /// <summary>样例值。</summary>
        public String Sample { get; init; }
    }

    /// <summary>当前解析版本。</summary>
    public const String ParseVersion = EfbmDefaults.ParseVersion;

    /// <summary><c>status</c> 展示字典初版，见《Doc/上报字段字典.md》§3.2；<b>不是校验白名单</b>。</summary>
    public static readonly String[] KnownStatuses = ["IDLE", "RUNNING", "PAUSED", "FAULT"];

    /// <summary>当前版本全部已登记字段。</summary>
    public static readonly Field[] All = BuildFields();

    private static Field[] BuildFields()
    {
        var orderEvents = new[]
        {
            EventNames.OrderStarted,
            EventNames.OrderProgress,
            EventNames.OrderCompleted,
        };
        var qtyEvents = new[] { EventNames.OrderProgress, EventNames.OrderCompleted };

        return new[]
        {
            // 通用信封
            new Field { Name = "event", DisplayName = "事件类型", JsonType = "String", Events = Array.Empty<String>(), Required = true, Column = "DeviceEventRaw.Event", Solidified = true, Sample = "ORDER_COMPLETED" },
            new Field { Name = "deviceId", DisplayName = "设备编号", JsonType = "String", Events = Array.Empty<String>(), Required = true, Column = "DeviceEventRaw.DeviceId", Solidified = true, Sample = "FBOX-A01" },
            new Field { Name = "timestamp", DisplayName = "事件时间", JsonType = "String", Events = Array.Empty<String>(), Required = true, Column = "DeviceEventRaw.EventTimeRaw", Solidified = true, Sample = "2026-07-23T18:00:00+08:00" },
            new Field { Name = "data", DisplayName = "业务载荷", JsonType = "Object", Events = Array.Empty<String>(), Required = false, Column = "DeviceEventRaw.DataJson", Solidified = true, Sample = "{}" },

            // data 字段（当前版本 6 个，全部已固化）
            new Field { Name = "orderId", DisplayName = "工单号", JsonType = "String", Events = orderEvents, Required = true, Column = "WorkOrder.OrderId", Solidified = true, Sample = "WO-20260723-001" },
            new Field { Name = "boxId", DisplayName = "盒型 / 盒号", JsonType = "String", Events = orderEvents, Required = true, Column = "ProductionRecord.BoxId", Solidified = true, Sample = "BOX-0001" },
            new Field { Name = "planQty", DisplayName = "计划数量", JsonType = "Number(Int32)", Events = qtyEvents, Required = true, Column = "WorkOrder.PlanQty", Solidified = true, Sample = "1000" },
            new Field { Name = "completedQty", DisplayName = "累计完成数", JsonType = "Number(Int32)", Events = qtyEvents, Required = true, Column = "WorkOrder.CompletedQty", Solidified = true, Sample = "1000" },
            new Field { Name = "status", DisplayName = "设备状态原值", JsonType = "String", Events = new[] { EventNames.DeviceStatusChanged }, Required = true, Column = "DeviceStatusLog.StatusRaw", Solidified = true, Sample = "RUNNING" },
            new Field { Name = "message", DisplayName = "故障信息", JsonType = "String", Events = new[] { EventNames.FaultOccurred }, Required = true, Column = "FaultRecord.Message", Solidified = true, Sample = "报错信息" },
        };
    }

    /// <summary>取某事件业务上应有的已登记 <c>data</c> 字段（不含顶层信封字段）。</summary>
    /// <param name="eventName">事件名；未知事件返回空集合。</param>
    /// <returns>该事件已登记的字段数组。</returns>
    public static Field[] GetFields(String eventName) =>
        All.Where(e => e.Events != null && Array.IndexOf(e.Events, eventName) >= 0).ToArray();

    /// <summary>判断字段是否为某事件的已登记字段。</summary>
    /// <param name="eventName">事件名。</param>
    /// <param name="fieldName">字段名。</param>
    /// <returns>已登记返回 <c>true</c>。</returns>
    public static Boolean IsKnown(String eventName, String fieldName) =>
        GetFields(eventName).Any(e => String.Equals(e.Name, fieldName, StringComparison.Ordinal));

    /// <summary>判断状态值是否命中当前展示字典；未命中只打 W104，绝不拒绝。</summary>
    /// <param name="status">报文 <c>data.status</c> 原值。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    public static Boolean IsKnownStatus(String status) =>
        !String.IsNullOrEmpty(status) && Array.IndexOf(KnownStatuses, status) >= 0;

    /// <summary>筛选未登记字段名，供 <c>UnknownFields</c> / <c>ExtJson</c> 与后台字段监测使用。</summary>
    /// <param name="eventName">事件名。</param>
    /// <param name="fieldNames">报文中实际出现的 <c>data</c> 字段名。</param>
    /// <returns>未登记字段名数组，顺序与输入一致。</returns>
    public static String[] FindUnknown(String eventName, IEnumerable<String> fieldNames)
    {
        if (fieldNames == null) return Array.Empty<String>();
        return fieldNames.Where(e => !String.IsNullOrEmpty(e) && !IsKnown(eventName, e)).Distinct(StringComparer.Ordinal).ToArray();
    }
}
