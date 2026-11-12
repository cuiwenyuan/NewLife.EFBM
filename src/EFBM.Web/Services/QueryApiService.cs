using EFBM.Core;
using EFBM.Data.Entities;
using XCode;

namespace EFBM.Web.Services;

/// <summary>统一查询统计 API 的数据层实现（FR-16）。</summary>
/// <remarks>
/// 所有查询都只读、只走 XCode 实体，不拼 SQL 字符串：外部系统拿到的字段名与库表语义一致，
/// 固化字段后无需改接口。分页统一收敛（页码 ≥1、每页 ≤ <see cref="MaxPageSize"/>），
/// 时间区间倒挂直接返回空集而不是交给数据库解释。
///
/// 「产量」刻意<b>不做逐行求和</b>：明细里的 <c>completedQty</c> 是累计值，
/// 逐行相加会把同一批产量重复计入；正确做法是按工单取最大值再加总。
/// </remarks>
public sealed class QueryApiService
{
    /// <summary>每页条数上限。</summary>
    public const Int32 MaxPageSize = 200;

    private static readonly Object _dictLock = new();
    private static DateTime _dictAt;
    private static Dictionary<String, String> _dict = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>分页查询设备状态（WEB-2a）。</summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备状态分页。</returns>
    public Task<QueryPage<DeviceStatusView>> QueryDevicesAsync(DeviceQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new DeviceQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);
        if (Inverted(query.From, query.To)) return Task.FromResult(QueryPage<DeviceStatusView>.Empty(page, size));

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : Device._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, query.Enabled.HasValue ? Device._.Enabled == query.Enabled.Value : null);
        exp = And(exp, String.IsNullOrWhiteSpace(query.StatusRaw) ? null : Device._.StatusRaw == query.StatusRaw.Trim());
        exp = And(exp, query.From.HasValue ? Device._.LastReportTime >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? Device._.LastReportTime <= query.To.Value : null);

        var total = Device.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(QueryPage<DeviceStatusView>.Empty(page, size));

        var list = Device.FindAll(exp, Device._.LastReportTime.Desc(), null, (Int64)(page - 1) * size, size);
        var dict = GetStatusDict();

        return Task.FromResult(new QueryPage<DeviceStatusView>
        {
            Items = list.Select(e => ToView(e, dict)).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>按设备编号读取单台设备状态。</summary>
    /// <param name="deviceId">设备编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备状态；不存在返回 <c>null</c>。</returns>
    public Task<DeviceStatusView> GetDeviceAsync(String deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (String.IsNullOrWhiteSpace(deviceId)) return Task.FromResult<DeviceStatusView>(null);

        var entity = Device.Find(Device._.DeviceId == deviceId.Trim());
        return Task.FromResult(entity == null ? null : ToView(entity, GetStatusDict()));
    }

    /// <summary>分页查询工单进度（WEB-4a）。</summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工单分页。</returns>
    public Task<QueryPage<WorkOrderProgressView>> QueryOrdersAsync(WorkOrderQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new WorkOrderQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);
        if (Inverted(query.From, query.To)) return Task.FromResult(QueryPage<WorkOrderProgressView>.Empty(page, size));

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.OrderId) ? null : WorkOrder._.OrderId == query.OrderId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : WorkOrder._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.Status) ? null : WorkOrder._.Status == query.Status.Trim());
        exp = And(exp, query.From.HasValue ? WorkOrder._.StartedAt >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? WorkOrder._.StartedAt <= query.To.Value : null);

        var total = WorkOrder.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(QueryPage<WorkOrderProgressView>.Empty(page, size));

        var list = WorkOrder.FindAll(exp, WorkOrder._.StartedAt.Desc(), null, (Int64)(page - 1) * size, size);

        return Task.FromResult(new QueryPage<WorkOrderProgressView>
        {
            Items = list.Select(ToView).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>按工单号读取单个工单进度。</summary>
    /// <param name="orderId">工单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工单进度；不存在返回 <c>null</c>。</returns>
    public Task<WorkOrderProgressView> GetOrderAsync(String orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (String.IsNullOrWhiteSpace(orderId)) return Task.FromResult<WorkOrderProgressView>(null);

        var entity = WorkOrder.Find(WorkOrder._.OrderId == orderId.Trim());
        return Task.FromResult(entity == null ? null : ToView(entity));
    }

    /// <summary>分页查询逐件完成明细（WEB-4a）。</summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>明细分页。</returns>
    public Task<QueryPage<ProductionItemView>> QueryProductionAsync(ProductionQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new ProductionQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);
        if (Inverted(query.From, query.To)) return Task.FromResult(QueryPage<ProductionItemView>.Empty(page, size));

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.OrderId) ? null : ProductionRecord._.OrderId == query.OrderId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.BoxId) ? null : ProductionRecord._.BoxId == query.BoxId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : ProductionRecord._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, query.From.HasValue ? ProductionRecord._.EventTime >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? ProductionRecord._.EventTime <= query.To.Value : null);

        var total = ProductionRecord.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(QueryPage<ProductionItemView>.Empty(page, size));

        var list = ProductionRecord.FindAll(exp, ProductionRecord._.EventTime.Desc(), null, (Int64)(page - 1) * size, size);

        return Task.FromResult(new QueryPage<ProductionItemView>
        {
            Items = list.Select(ToView).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>按工单或设备汇总产量（WEB-4a）。</summary>
    /// <param name="query">统计条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>产量汇总。</returns>
    public Task<OutputSummary> QueryOutputAsync(OutputQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new OutputQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var groupBy = String.Equals(query.GroupBy, "device", StringComparison.OrdinalIgnoreCase) ? "device" : "order";
        var limit = query.ScanLimit <= 0 ? OutputSummary.DefaultScanLimit : Math.Min(query.ScanLimit, OutputSummary.MaxScanLimit);

        if (Inverted(query.From, query.To))
            return Task.FromResult(new OutputSummary { GroupBy = groupBy, From = query.From, To = query.To });

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.OrderId) ? null : ProductionRecord._.OrderId == query.OrderId.Trim());
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : ProductionRecord._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, query.From.HasValue ? ProductionRecord._.EventTime >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? ProductionRecord._.EventTime <= query.To.Value : null);

        // 多取一条用来判断是否被截断，不额外做一次 count
        var list = ProductionRecord.FindAll(exp, ProductionRecord._.EventTime.Asc(), null, 0, limit + 1);
        var truncated = list.Count > limit;
        if (truncated) list = list.Take(limit).ToList();

        // 先按工单聚合，取每单的累计完成数峰值
        var orders = list
            .Where(e => !String.IsNullOrWhiteSpace(e.OrderId))
            .GroupBy(e => e.OrderId, StringComparer.Ordinal)
            .Select(g => new
            {
                OrderId = g.Key,
                DeviceId = g.Select(e => e.DeviceId).FirstOrDefault(e => !String.IsNullOrWhiteSpace(e)),
                BoxCount = g.Count(),
                CompletedQty = g.Max(e => e.CompletedQty),
                FirstAt = g.Min(e => e.EventTime),
                LastAt = g.Max(e => e.EventTime),
            })
            .ToList();

        var items = groupBy == "device"
            ? orders.GroupBy(e => e.DeviceId ?? String.Empty, StringComparer.Ordinal)
                .Select(g => new OutputStatView
                {
                    Key = g.Key,
                    DeviceId = g.Key,
                    OrderCount = g.Count(),
                    BoxCount = g.Sum(e => e.BoxCount),
                    CompletedQty = g.Sum(e => e.CompletedQty),
                    FirstAt = g.Min(e => e.FirstAt),
                    LastAt = g.Max(e => e.LastAt),
                })
                .OrderByDescending(e => e.CompletedQty)
                .ToList()
            : orders
                .Select(e => new OutputStatView
                {
                    Key = e.OrderId,
                    DeviceId = e.DeviceId,
                    OrderCount = 1,
                    BoxCount = e.BoxCount,
                    CompletedQty = e.CompletedQty,
                    FirstAt = e.FirstAt,
                    LastAt = e.LastAt,
                })
                .OrderByDescending(e => e.LastAt)
                .ToList();

        return Task.FromResult(new OutputSummary
        {
            GroupBy = groupBy,
            Items = items,
            ScannedRows = list.Count,
            Truncated = truncated,
            From = query.From,
            To = query.To,
        });
    }

    /// <summary>分页查询故障记录，含持续时长（WEB-5a）。</summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>故障分页。</returns>
    public Task<QueryPage<FaultView>> QueryFaultsAsync(FaultQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new FaultQuery();
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var size = ClampSize(query.PageSize);
        if (Inverted(query.From, query.To)) return Task.FromResult(QueryPage<FaultView>.Empty(page, size));

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(query.DeviceId) ? null : FaultRecord._.DeviceId == query.DeviceId.Trim());
        exp = And(exp, query.Status.HasValue ? FaultRecord._.Status == query.Status.Value : null);
        exp = And(exp, query.OpenOnly ? FaultRecord._.Status == 0 : null);
        exp = And(exp, query.From.HasValue ? FaultRecord._.OccurredAt >= query.From.Value : null);
        exp = And(exp, query.To.HasValue ? FaultRecord._.OccurredAt <= query.To.Value : null);

        var total = FaultRecord.FindCount(exp, null, null, 0, 0);
        if (total <= 0) return Task.FromResult(QueryPage<FaultView>.Empty(page, size));

        var list = FaultRecord.FindAll(exp, FaultRecord._.OccurredAt.Desc(), null, (Int64)(page - 1) * size, size);
        var now = DateTime.UtcNow;

        return Task.FromResult(new QueryPage<FaultView>
        {
            Items = list.Select(e => ToView(e, now)).ToList(),
            Total = total,
            Page = page,
            PageSize = size,
        });
    }

    /// <summary>读取共享流积压（WEB-11a 的队列部分）。</summary>
    /// <param name="probe">指标探测实现，可为 <c>null</c>（未接入共享流）。</param>
    /// <param name="options">EFBM 配置节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>积压视图；探测失败也返回结构完整的结果。</returns>
    public async Task<StreamBacklogView> GetStreamBacklogAsync(IQueueMetricsProbe probe, EfbmOptions options, CancellationToken cancellationToken = default)
    {
        if (probe == null || options == null || !options.RedisEnabled)
        {
            return new StreamBacklogView
            {
                Enabled = options?.RedisEnabled ?? false,
                StreamKey = options?.StreamKey,
                SampledAt = DateTime.UtcNow,
                Error = options?.RedisEnabled == true ? "未接入共享流" : "共享流已关闭（EFBM:RedisEnabled=false）",
            };
        }

        var metrics = await probe.GetMetricsAsync(cancellationToken).ConfigureAwait(false);

        return new StreamBacklogView
        {
            Enabled = metrics.Enabled,
            Connected = metrics.Connected,
            StreamKey = metrics.StreamKey,
            Length = metrics.Length,
            TotalLag = metrics.TotalLag,
            TotalPending = metrics.TotalPending,
            SampledAt = metrics.SampledAt.ToUniversalTime(),
            Error = metrics.Error,
            Groups = metrics.Groups.Select(e => new StreamGroupBacklog
            {
                Name = e.Name,
                Consumers = e.Consumers,
                Pending = e.Pending,
                Lag = e.Lag,
                LagCapped = e.LagCapped,
                IsZombie = e.IsZombie,
                Advice = e.Advice,
            }).ToList(),
        };
    }

    /// <summary>设备实体转视图。</summary>
    private static DeviceStatusView ToView(Device entity, Dictionary<String, String> dict)
    {
        var raw = entity.StatusRaw;
        var text = !String.IsNullOrEmpty(raw) && dict.TryGetValue(raw, out var name) ? name : raw;

        return new DeviceStatusView
        {
            DeviceId = entity.DeviceId,
            Name = entity.Name,
            StatusRaw = raw,
            StatusText = text,
            Enabled = entity.Enabled,
            LastReportTime = entity.LastReportTime,
        };
    }

    /// <summary>工单实体转视图。</summary>
    private static WorkOrderProgressView ToView(WorkOrder entity) => new()
    {
        OrderId = entity.OrderId,
        BoxId = entity.BoxId,
        DeviceId = entity.DeviceId,
        PlanQty = entity.PlanQty,
        CompletedQty = entity.CompletedQty,
        Status = entity.Status,
        StartedAt = entity.StartedAt,
        LastReportAt = entity.LastReportAt,
        CompletedAt = entity.CompletedAt,
    };

    /// <summary>明细实体转视图。</summary>
    private static ProductionItemView ToView(ProductionRecord entity) => new()
    {
        EventKey = entity.EventKey,
        OrderId = entity.OrderId,
        BoxId = entity.BoxId,
        DeviceId = entity.DeviceId,
        IsPiece = entity.IsPiece,
        PlanQty = entity.PlanQty,
        CompletedQty = entity.CompletedQty,
        EventTime = entity.EventTime,
        ExtJson = entity.ExtJson,
    };

    /// <summary>故障实体转视图，未关闭的持续时长算到当前时刻。</summary>
    private static FaultView ToView(FaultRecord entity, DateTime now)
    {
        // 未清除的 ClearedAt 落库为 1970 哨兵值，不能当成「1970 年就清除了」
        var cleared = entity.ClearedAt > EfbmDefaults.UnrequeuedAt ? entity.ClearedAt : (DateTime?)null;
        var end = cleared ?? (now > entity.OccurredAt ? now : entity.OccurredAt);
        var duration = entity.OccurredAt > EfbmDefaults.UnrequeuedAt
            ? (Int64)Math.Max(0, (end - entity.OccurredAt).TotalSeconds)
            : (Int64?)null;

        return new FaultView
        {
            Id = entity.Id,
            DeviceId = entity.DeviceId,
            Message = entity.Message,
            OccurredAt = entity.OccurredAt,
            ClearedAt = cleared,
            Status = entity.Status,
            DurationSeconds = duration,
        };
    }

    /// <summary>状态字典：60 秒缓存。字典是小表且几乎不变，每条查询都读一次库不值得。</summary>
    private static Dictionary<String, String> GetStatusDict()
    {
        lock (_dictLock)
        {
            if (_dictAt != default && (DateTime.UtcNow - _dictAt).TotalSeconds < 60) return _dict;
        }

        var map = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in StatusDict.FindAll())
            {
                if (!String.IsNullOrEmpty(item.StatusCode) && !String.IsNullOrEmpty(item.DisplayName))
                    map[item.StatusCode] = item.DisplayName;
            }
        }
        catch (Exception)
        {
            // 字典读不出来不影响状态查询：中文名回落为原值，比整页 500 好
            return map;
        }

        lock (_dictLock)
        {
            _dict = map;
            _dictAt = DateTime.UtcNow;
        }

        return map;
    }

    /// <summary>以 AND 叠加条件，任一侧为空时返回非空的一侧。</summary>
    private static Expression And(Expression left, Expression right)
    {
        if (left == null) return right;
        if (right == null) return left;
        return left & right;
    }

    /// <summary>时间区间倒挂属于调用方错误，直接返回空集。</summary>
    private static Boolean Inverted(DateTime? from, DateTime? to) => from.HasValue && to.HasValue && from > to;

    /// <summary>归一化每页条数。</summary>
    private static Int32 ClampSize(Int32 size) => size <= 0 ? 20 : Math.Min(size, MaxPageSize);
}
