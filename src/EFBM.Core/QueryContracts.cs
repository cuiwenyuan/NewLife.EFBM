namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>查询统计 API 的分页包装（FR-16）。</summary>
/// <remarks>
/// 与后台内部的 <c>PagedResult</c> 分开定义：外部系统只依赖 <see cref="EFBM.Core"/>，
/// 不应当为了一个字段名去引用宿主的服务层。字段名用 <c>camelCase</c> 由 JSON 序列化策略统一处理，
/// 结构一旦发布即视为契约，新增字段只能追加、不能改名。
/// </remarks>
/// <typeparam name="T">列表项类型。</typeparam>
public sealed class QueryPage<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>符合条件总条数。</summary>
    public Int64 Total { get; init; }

    /// <summary>当前页码，从 1 开始。</summary>
    public Int32 Page { get; init; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; init; } = 20;

    /// <summary>总页数。</summary>
    public Int32 PageCount => PageSize <= 0 ? 0 : (Int32)((Total + PageSize - 1) / PageSize);

    /// <summary>构造空页。</summary>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>空分页结果。</returns>
    public static QueryPage<T> Empty(Int32 page, Int32 pageSize) => new() { Page = page, PageSize = pageSize };
}

/// <summary>设备状态查询条件（WEB-2a）。</summary>
public sealed class DeviceQuery
{
    /// <summary>设备编号，精确匹配。</summary>
    public String DeviceId { get; set; }

    /// <summary>是否启用，<c>null</c> 不限。</summary>
    public Boolean? Enabled { get; set; }

    /// <summary>状态原值，精确匹配。</summary>
    public String StatusRaw { get; set; }

    /// <summary>最后上报时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>最后上报时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>页码。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>设备状态视图：原值 + 字典中文 + 最后上报时间（FR-10、FR-16）。</summary>
/// <remarks>
/// <see cref="StatusRaw"/> 是设备上报的原值，<b>不做枚举校验</b>；
/// <see cref="StatusText"/> 是状态字典里的中文名，字典里没有时回落为原值，
/// 调用方必须以原值做逻辑判断，中文名只用于展示。
/// </remarks>
public sealed class DeviceStatusView
{
    /// <summary>设备编号。</summary>
    public String DeviceId { get; init; }

    /// <summary>设备名称。</summary>
    public String Name { get; init; }

    /// <summary>状态原值。</summary>
    public String StatusRaw { get; init; }

    /// <summary>状态中文名；字典未登记时与 <see cref="StatusRaw"/> 相同。</summary>
    public String StatusText { get; init; }

    /// <summary>是否启用。</summary>
    public Boolean Enabled { get; init; }

    /// <summary>最后上报时间（UTC）。</summary>
    public DateTime LastReportTime { get; init; }
}

/// <summary>工单进度查询条件（WEB-4a）。</summary>
public sealed class WorkOrderQuery
{
    /// <summary>工单号，精确匹配。</summary>
    public String OrderId { get; set; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>派生状态：进行中 / 已完成。</summary>
    public String Status { get; set; }

    /// <summary>开工时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>开工时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>页码。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>工单进度视图（FR-16）。</summary>
public sealed class WorkOrderProgressView
{
    /// <summary>工单号。</summary>
    public String OrderId { get; init; }

    /// <summary>盒型 / 盒号原值。</summary>
    public String BoxId { get; init; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; init; }

    /// <summary>计划数量。</summary>
    public Int32 PlanQty { get; init; }

    /// <summary>累计完成数（取大收敛后的值）。</summary>
    public Int32 CompletedQty { get; init; }

    /// <summary>完成比例，0~1；计划数为 0 时为 <c>null</c>。</summary>
    public Double? Progress => PlanQty <= 0 ? null : Math.Round(Math.Min(1d, (Double)CompletedQty / PlanQty), 4);

    /// <summary>派生状态。</summary>
    public String Status { get; init; }

    /// <summary>开工时间（UTC）。</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>最后上报时间（UTC）。</summary>
    public DateTime LastReportAt { get; init; }

    /// <summary>派生完成时间（UTC）。</summary>
    public DateTime CompletedAt { get; init; }
}

/// <summary>逐件明细查询条件（WEB-4a）。</summary>
public sealed class ProductionQuery
{
    /// <summary>工单号。</summary>
    public String OrderId { get; set; }

    /// <summary>盒型 / 盒号。</summary>
    public String BoxId { get; set; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>事件时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>事件时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>页码。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>逐件明细视图。</summary>
public sealed class ProductionItemView
{
    /// <summary>幂等键。</summary>
    public String EventKey { get; init; }

    /// <summary>工单号。</summary>
    public String OrderId { get; init; }

    /// <summary>盒型 / 盒号原值。</summary>
    public String BoxId { get; init; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; init; }

    /// <summary>是否逐件完成事件。</summary>
    public Boolean IsPiece { get; init; }

    /// <summary>计划数量。</summary>
    public Int32 PlanQty { get; init; }

    /// <summary>事件时刻的累计完成数。</summary>
    public Int32 CompletedQty { get; init; }

    /// <summary>设备侧事件时间（UTC）。</summary>
    public DateTime EventTime { get; init; }

    /// <summary>扩展字段 JSON（未固化字段的原始值）。</summary>
    public String ExtJson { get; init; }
}

/// <summary>产量统计查询条件。</summary>
public sealed class OutputQuery
{
    /// <summary>统计维度：<c>order</c>（按工单，默认）或 <c>device</c>（按设备）。</summary>
    public String GroupBy { get; set; } = "order";

    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>工单号。</summary>
    public String OrderId { get; set; }

    /// <summary>事件时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>事件时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>扫描行数上限；超限时结果标记 <see cref="OutputSummary.Truncated"/>。</summary>
    public Int32 ScanLimit { get; set; } = OutputSummary.DefaultScanLimit;
}

/// <summary>产量统计结果。</summary>
public sealed class OutputSummary
{
    /// <summary>默认扫描行数上限。</summary>
    public const Int32 DefaultScanLimit = 50000;

    /// <summary>扫描上限的硬上限，防止外部系统一次拉全表。</summary>
    public const Int32 MaxScanLimit = 200000;

    /// <summary>统计维度（order / device）。</summary>
    public String GroupBy { get; init; }

    /// <summary>各维度汇总。</summary>
    public IReadOnlyList<OutputStatView> Items { get; init; } = Array.Empty<OutputStatView>();

    /// <summary>实际扫描的明细行数。</summary>
    public Int32 ScannedRows { get; init; }

    /// <summary>是否因扫描上限被截断；<c>true</c> 时结果只是下限。</summary>
    public Boolean Truncated { get; init; }

    /// <summary>统计时间范围下界（UTC）；未指定为 <c>null</c>。</summary>
    public DateTime? From { get; init; }

    /// <summary>统计时间范围上界（UTC）；未指定为 <c>null</c>。</summary>
    public DateTime? To { get; init; }

    /// <summary>生成时间（UTC）。</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>单个维度的产量汇总。</summary>
/// <remarks>
/// <see cref="CompletedQty"/> 是<b>各工单累计值的加总</b>，不是明细行 <c>completedQty</c> 的求和：
/// 后者是累计语义，逐行相加会把同一批产量重复计入。
/// </remarks>
public sealed class OutputStatView
{
    /// <summary>分组键：按工单时为工单号，按设备时为设备编号。</summary>
    public String Key { get; init; }

    /// <summary>设备编号（按设备分组时与 <see cref="Key"/> 相同）。</summary>
    public String DeviceId { get; init; }

    /// <summary>涉及的工单数。</summary>
    public Int32 OrderCount { get; init; }

    /// <summary>明细条数（逐件完成事件数）。</summary>
    public Int32 BoxCount { get; init; }

    /// <summary>累计完成数（各工单取大后加总）。</summary>
    public Int32 CompletedQty { get; init; }

    /// <summary>首条明细时间（UTC）。</summary>
    public DateTime FirstAt { get; init; }

    /// <summary>末条明细时间（UTC）。</summary>
    public DateTime LastAt { get; init; }
}

/// <summary>故障查询条件（WEB-5a）。</summary>
public sealed class FaultQuery
{
    /// <summary>设备编号。</summary>
    public String DeviceId { get; set; }

    /// <summary>状态：0 未清除 / 1 已清除 / 2 手动关闭；<c>null</c> 不限。</summary>
    public Int32? Status { get; set; }

    /// <summary>发生时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>发生时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>是否只看未关闭（含手动关闭前的未清除）。</summary>
    public Boolean OpenOnly { get; set; }

    /// <summary>页码。</summary>
    public Int32 Page { get; set; } = 1;

    /// <summary>每页条数。</summary>
    public Int32 PageSize { get; set; } = 20;
}

/// <summary>故障视图，含持续时长（FR-16）。</summary>
public sealed class FaultView
{
    /// <summary>主键。</summary>
    public Int64 Id { get; init; }

    /// <summary>设备编号。</summary>
    public String DeviceId { get; init; }

    /// <summary>故障信息。</summary>
    public String Message { get; init; }

    /// <summary>发生时间（UTC）。</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>清除时间（UTC）；未清除为 <c>null</c>。</summary>
    public DateTime? ClearedAt { get; init; }

    /// <summary>状态：0 未清除 / 1 已清除 / 2 手动关闭。</summary>
    public Int32 Status { get; init; }

    /// <summary>状态中文名。</summary>
    public String StatusText => DescribeStatus(Status);

    /// <summary>是否仍未关闭。</summary>
    public Boolean IsOpen => Status == 0;

    /// <summary>持续时长（秒）：已关闭取 <see cref="ClearedAt"/> 与发生时间之差，未关闭取到当前时刻。</summary>
    public Int64? DurationSeconds { get; init; }

    /// <summary>把状态原值转中文；未知值原样显示，避免编造含义。</summary>
    /// <param name="status">状态原值。</param>
    /// <returns>展示文案。</returns>
    public static String DescribeStatus(Int32 status) => status switch
    {
        0 => "未清除",
        1 => "已清除",
        2 => "手动关闭",
        _ => status.ToString(),
    };
}

/// <summary>Stream 积压视图（WEB-11a 的队列部分，FR-17、FR-23）。</summary>
/// <remarks>
/// 与后台监控页共用 <see cref="IQueueMetricsProbe"/>，但只暴露外部系统需要的字段：
/// 各组 lag / pending 与「是否僵尸」，不暴露 Redis 内存、裁剪水位等运维内部信息。
/// </remarks>
public sealed class StreamBacklogView
{
    /// <summary>是否启用共享流。</summary>
    public Boolean Enabled { get; init; }

    /// <summary>是否连接成功。</summary>
    public Boolean Connected { get; init; }

    /// <summary>Stream key。</summary>
    public String StreamKey { get; init; }

    /// <summary>Stream 当前长度。</summary>
    public Int64 Length { get; init; }

    /// <summary>各组积压合计。</summary>
    public Int64 TotalLag { get; init; }

    /// <summary>各组未确认合计。</summary>
    public Int64 TotalPending { get; init; }

    /// <summary>各组状态。</summary>
    public IReadOnlyList<StreamGroupBacklog> Groups { get; init; } = Array.Empty<StreamGroupBacklog>();

    /// <summary>采样时间（UTC）。</summary>
    public DateTime SampledAt { get; init; }

    /// <summary>失败原因摘要。</summary>
    public String Error { get; init; }
}

/// <summary>单个消费组的积压状态。</summary>
public sealed class StreamGroupBacklog
{
    /// <summary>消费组名。</summary>
    public String Name { get; init; }

    /// <summary>消费者个数。</summary>
    public Int32 Consumers { get; init; }

    /// <summary>未确认消息数。</summary>
    public Int64 Pending { get; init; }

    /// <summary>尚未投递的消息数。</summary>
    public Int64 Lag { get; init; }

    /// <summary>是否因采样上限只能给出下限。</summary>
    public Boolean LagCapped { get; init; }

    /// <summary>是否存在积压却无人消费的情况。</summary>
    public Boolean IsZombie { get; init; }

    /// <summary>诊断提示。</summary>
    public String Advice { get; init; }
}
