using System.Text.Json;
using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;
using XCode;

namespace EFBM.Web.Services;

/// <summary>一个已登记字段（来源可以是代码字典，也可以是后台登记表）。</summary>
public sealed class FieldRegistryEntry
{
    /// <summary>所属事件；空表示信封字段。</summary>
    public String Event { get; init; }

    /// <summary>字段名。</summary>
    public String FieldName { get; init; }

    /// <summary>中文名。</summary>
    public String DisplayName { get; init; }

    /// <summary>JSON 类型。</summary>
    public String JsonType { get; init; }

    /// <summary>是否已固化为正式列。</summary>
    public Boolean Solidified { get; init; }

    /// <summary>已固化时的落库列。</summary>
    public String Column { get; init; }

    /// <summary>来源：<c>代码</c>（<see cref="EventFieldRegistry"/>）或 <c>登记表</c>。</summary>
    public String Source { get; init; }

    /// <summary>登记表主键；代码字典为 <c>null</c>。</summary>
    public Int32? DictId { get; init; }

    /// <summary>备注。</summary>
    public String Remark { get; init; }
}

/// <summary>从报文里聚合出来的未登记字段。</summary>
public sealed class FieldCandidate
{
    /// <summary>所属事件。</summary>
    public String Event { get; init; }

    /// <summary>字段名。</summary>
    public String FieldName { get; init; }

    /// <summary>统计窗口内出现次数。</summary>
    public Int32 Count { get; init; }

    /// <summary>窗口内首次出现时间。</summary>
    public DateTime FirstSeen { get; init; }

    /// <summary>窗口内最近出现时间。</summary>
    public DateTime LastSeen { get; init; }

    /// <summary>样例值（取自 <c>ExtJson</c>，取不到时取 <c>DataJson</c> 同名节点）。</summary>
    public String Sample { get; init; }

    /// <summary>样例所在设备，便于找对方核对。</summary>
    public String SampleDeviceId { get; init; }

    /// <summary>样例报文主键。</summary>
    public Int64 SampleEventId { get; init; }

    /// <summary>是否已登记（代码字典或登记表任一命中）。</summary>
    public Boolean Registered { get; init; }

    /// <summary>已登记时的中文名。</summary>
    public String DisplayName { get; init; }

    /// <summary>已登记时的 JSON 类型。</summary>
    public String JsonType { get; init; }

    /// <summary>是否已固化为正式列。</summary>
    public Boolean Solidified { get; init; }

    /// <summary>推断类型：样例是对象 / 数组 / 数字 / 字符串。</summary>
    public String InferredType { get; init; }
}

/// <summary>字段字典与新增字段监测的一次快照。</summary>
public sealed class FieldEvolutionSnapshot
{
    /// <summary>统计窗口起点（接收时间）。</summary>
    public DateTime From { get; init; }

    /// <summary>统计窗口终点。</summary>
    public DateTime To { get; init; }

    /// <summary>实际扫描的报文条数。</summary>
    public Int32 ScannedRows { get; init; }

    /// <summary>窗口内报文是否超过扫描上限而只统计了一部分。</summary>
    public Boolean Truncated { get; init; }

    /// <summary>扫描上限。</summary>
    public Int32 MaxRows { get; init; }

    /// <summary>代码字典里的字段（按事件分组）。</summary>
    public IReadOnlyList<FieldRegistryEntry> Known { get; init; } = Array.Empty<FieldRegistryEntry>();

    /// <summary>聚合出的未登记字段（含已补登但仍需展示的项）。</summary>
    public IReadOnlyList<FieldCandidate> Candidates { get; init; } = Array.Empty<FieldCandidate>();

    /// <summary>登记表里的全部记录。</summary>
    public IReadOnlyList<FieldRegistryEntry> Registered { get; init; } = Array.Empty<FieldRegistryEntry>();
}

/// <summary>字段登记结果。</summary>
public sealed class FieldRegisterResult
{
    /// <summary>是否成功。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因。</summary>
    public String Reason { get; init; }

    /// <summary>写入后的登记记录。</summary>
    public FieldDict Dict { get; init; }

    /// <summary>构造成功结果。</summary>
    /// <param name="dict">登记记录。</param>
    /// <returns>成功结果。</returns>
    public static FieldRegisterResult Ok(FieldDict dict) => new() { Success = true, Dict = dict };

    /// <summary>构造失败结果。</summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>失败结果。</returns>
    public static FieldRegisterResult Fail(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>字段字典与新增字段监测（FR-27、WEB-10a / WEB-10b）。</summary>
/// <remarks>
/// 两条数据来源分开处理，避免把「代码里的契约」和「现场补登的说明」混成一锅：
/// <list type="number">
/// <item><b>代码字典</b> <see cref="EventFieldRegistry"/>：解析与展示用的已知字段，随版本发布，只读；</item>
/// <item><b>登记表</b> <c>FieldDict</c>：现场把新字段的中文名 / 类型 / 所属事件补登进来，不需要重新发版。</item>
/// </list>
/// 未登记字段来自 <c>DeviceEventRaw.UnknownFields</c>：在限定时间窗口内扫描<b>最近 N 条</b>报文聚合，
/// 不做全表扫描（原始报文表按月分表，全表聚合会把后台拖死），因此「首次出现时间」是<b>窗口内</b>首次。
/// </remarks>
public sealed class FieldEvolutionService
{
    /// <summary>默认扫描天数。</summary>
    public const Int32 DefaultDays = 7;

    /// <summary>默认扫描条数上限。</summary>
    public const Int32 DefaultMaxRows = 2000;

    /// <summary>扫描条数硬上限。</summary>
    public const Int32 MaxRowsLimit = 5000;

    /// <summary>窗口上限（天），防止有人传 3650 把库拖垮。</summary>
    public const Int32 MaxDays = 90;

    /// <summary>允许的 JSON 类型，与《上报字段字典》保持一致。</summary>
    public static readonly String[] JsonTypes = ["String", "Number(Int32)", "Number(Decimal)", "Boolean", "Object", "Array"];

    /// <summary>扫描未知字段并合并登记信息。</summary>
    /// <param name="days">统计窗口天数；<c>≤0</c> 取默认 7 天，超过 90 天按 90 天。</param>
    /// <param name="maxRows">扫描条数上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>快照。</returns>
    public Task<FieldEvolutionSnapshot> ScanAsync(Int32 days, Int32 maxRows, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = days <= 0 ? DefaultDays : Math.Min(days, MaxDays);
        var limit = maxRows <= 0 ? DefaultMaxRows : Math.Min(maxRows, MaxRowsLimit);
        var to = DateTime.Now;
        var from = to.AddDays(-window);
        var known = BuildKnown();
        var registered = LoadRegistered();

        // 只扫有未识别字段的报文；多取一条用于判断是否被截断
        var exp = DeviceEventRaw._.ReceivedAt >= from & DeviceEventRaw._.UnknownFields != "";
        var rows = DeviceEventRaw.FindAll(exp, DeviceEventRaw._.Id.Desc(), null, 0, limit + 1);
        var truncated = rows.Count > limit;

        var buckets = new Dictionary<String, Bucket>(StringComparer.Ordinal);
        var scanned = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanned >= limit) break;
            scanned++;

            if (String.IsNullOrWhiteSpace(row.UnknownFields)) continue;

            // 同一行里同名同事件只算一次，避免把「一行多次出现」当成多条样本
            var seen = new HashSet<String>(StringComparer.Ordinal);
            foreach (var raw in row.UnknownFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eventName = row.Event ?? String.Empty;
                var key = eventName + "\u0001" + raw;
                if (!seen.Add(key)) continue;

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket { Event = eventName, Field = raw };
                    buckets[key] = bucket;
                }

                bucket.Touch(row.ReceivedAt, row);
            }
        }

        var lookup = new Dictionary<String, FieldRegistryEntry>(StringComparer.Ordinal);
        foreach (var item in known) lookup[item.Event + "\u0001" + item.FieldName] = item;
        foreach (var item in registered) lookup.TryAdd(item.Event + "\u0001" + item.FieldName, item);

        var candidates = buckets.Values
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Event, StringComparer.Ordinal)
            .ThenBy(e => e.Field, StringComparer.Ordinal)
            .Select(e =>
            {
                // 登记可以只写事件名，也可以写空事件（信封字段），两种都认
                lookup.TryGetValue(e.Event + "\u0001" + e.Field, out var entry);
                entry ??= lookup.GetValueOrDefault("\u0001" + e.Field);

                var sample = e.Sample;
                return new FieldCandidate
                {
                    Event = e.Event,
                    FieldName = e.Field,
                    Count = e.Count,
                    FirstSeen = e.FirstSeen,
                    LastSeen = e.LastSeen,
                    Sample = sample,
                    SampleDeviceId = e.SampleDeviceId,
                    SampleEventId = e.SampleEventId,
                    Registered = entry != null,
                    DisplayName = entry?.DisplayName,
                    JsonType = entry?.JsonType,
                    Solidified = entry?.Solidified ?? false,
                    InferredType = InferType(sample),
                };
            })
            .ToList();

        return Task.FromResult(new FieldEvolutionSnapshot
        {
            From = from,
            To = to,
            ScannedRows = scanned,
            Truncated = truncated,
            MaxRows = limit,
            Known = known,
            Candidates = candidates,
            Registered = registered,
        });
    }

    /// <summary>登记或修改一条字段说明。</summary>
    /// <param name="dictId">登记表主键；<c>0</c> 表示新增。</param>
    /// <param name="eventName">所属事件；空表示信封字段。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="displayName">中文名。</param>
    /// <param name="jsonType">JSON 类型，必须在 <see cref="JsonTypes"/> 内。</param>
    /// <param name="solidified">是否已固化为正式列。</param>
    /// <param name="remark">备注。</param>
    /// <param name="operatorName">操作人（写日志）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记结果。</returns>
    public Task<FieldRegisterResult> RegisterAsync(Int32 dictId, String eventName, String fieldName, String displayName,
        String jsonType, Boolean solidified, String remark, String operatorName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        fieldName = fieldName?.Trim();
        if (String.IsNullOrWhiteSpace(fieldName)) return Task.FromResult(FieldRegisterResult.Fail("字段名必填"));
        if (!IsValidFieldName(fieldName)) return Task.FromResult(FieldRegisterResult.Fail("字段名只能是字母、数字与下划线，且不以数字开头"));

        eventName = eventName?.Trim() ?? String.Empty;
        displayName = displayName?.Trim();
        if (String.IsNullOrWhiteSpace(displayName)) return Task.FromResult(FieldRegisterResult.Fail("中文名必填，否则页面上的字段还是天书"));

        jsonType = jsonType?.Trim();
        if (String.IsNullOrEmpty(jsonType) || Array.IndexOf(JsonTypes, jsonType) < 0)
            return Task.FromResult(FieldRegisterResult.Fail($"JSON 类型必须是 {String.Join(" / ", JsonTypes)} 之一"));

        var entity = dictId > 0 ? FieldDict.FindById(dictId) : null;
        if (dictId > 0 && entity == null) return Task.FromResult(FieldRegisterResult.Fail("登记记录不存在"));

        // 唯一索引是 (Event, FieldName)：手工查一次，给出一句人话而不是数据库的约束报错。
        // 这里不能用 FindByEventAndFieldName —— 它为信封字段（Event 为空）直接返回 null，
        // 会让「信封字段重名」漏到数据库约束上，变成一句看不出所以然的外键 / 唯一键异常。
        var duplicate = FieldDict.Find(FieldDict._.Event == eventName & FieldDict._.FieldName == fieldName);
        if (duplicate != null && duplicate.Id != (entity?.Id ?? 0))
            return Task.FromResult(FieldRegisterResult.Fail($"事件 {DescribeEvent(eventName)} 下的字段 {fieldName} 已登记过，请直接修改那条记录"));

        entity ??= new FieldDict { Event = eventName, FieldName = fieldName };
        entity.DisplayName = displayName;
        entity.JsonType = jsonType;
        entity.Solidified = solidified;
        entity.Remark = remark?.Trim();

        try
        {
            entity.Save();
        }
        catch (Exception ex)
        {
            // 并发登记时唯一索引仍可能先被别的请求占上，这时给人话而不是堆栈
            XTrace.WriteException(ex);
            return Task.FromResult(FieldRegisterResult.Fail($"保存失败，可能已被他人登记：{ex.Message}"));
        }

        XTrace.WriteLine("后台登记字段：event={0} field={1} 中文名={2} 类型={3} 固化={4} 操作人={5}",
            DescribeEvent(eventName), fieldName, displayName, jsonType, solidified, operatorName);

        return Task.FromResult(FieldRegisterResult.Ok(entity));
    }

    /// <summary>删除一条字段登记。</summary>
    /// <param name="id">登记表主键。</param>
    /// <param name="operatorName">操作人（写日志）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否删除成功。</returns>
    public Task<Boolean> DeleteAsync(Int32 id, String operatorName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = id > 0 ? FieldDict.FindById(id) : null;
        if (entity == null) return Task.FromResult(false);

        entity.Delete();

        XTrace.WriteLine("后台删除字段登记：event={0} field={1} 操作人={2}", DescribeEvent(entity.Event), entity.FieldName, operatorName);
        return Task.FromResult(true);
    }

    /// <summary>事件名展示：空表示信封字段。</summary>
    /// <param name="eventName">事件名。</param>
    /// <returns>展示文案。</returns>
    public static String DescribeEvent(String eventName)
        => String.IsNullOrWhiteSpace(eventName) ? "信封字段（通用）" : eventName;

    /// <summary>按样例值推断 JSON 类型，登记表单给个默认值。</summary>
    /// <param name="sample">样例值文本。</param>
    /// <returns>类型名。</returns>
    public static String InferType(String sample)
    {
        if (String.IsNullOrWhiteSpace(sample)) return "String";

        var text = sample.Trim();
        if (text.StartsWith('{')) return "Object";
        if (text.StartsWith('[')) return "Array";
        if (Boolean.TryParse(text, out _)) return "Boolean";
        if (Int64.TryParse(text, out _)) return "Number(Int32)";
        if (Double.TryParse(text, out _)) return "Number(Decimal)";

        return "String";
    }

    /// <summary>字段名合法性：字母 / 下划线开头，后接字母数字下划线；拒掉注入与空白。</summary>
    /// <param name="name">字段名。</param>
    /// <returns>合法返回 <c>true</c>。</returns>
    public static Boolean IsValidFieldName(String name)
    {
        if (String.IsNullOrEmpty(name) || name.Length > 50) return false;
        if (!Char.IsLetter(name[0]) && name[0] != '_') return false;

        foreach (var ch in name)
        {
            if (!Char.IsLetterOrDigit(ch) && ch != '_') return false;
        }

        return true;
    }

    /// <summary>把代码字典摊平成登记项，事件为空表示信封字段。</summary>
    private static List<FieldRegistryEntry> BuildKnown()
    {
        var list = new List<FieldRegistryEntry>();

        foreach (var field in EventFieldRegistry.All)
        {
            if (field.Events == null || field.Events.Length == 0)
            {
                list.Add(new FieldRegistryEntry
                {
                    Event = String.Empty,
                    FieldName = field.Name,
                    DisplayName = field.DisplayName,
                    JsonType = field.JsonType,
                    Solidified = field.Solidified,
                    Column = field.Column,
                    Source = "代码",
                });
                continue;
            }

            foreach (var eventName in field.Events)
            {
                list.Add(new FieldRegistryEntry
                {
                    Event = eventName,
                    FieldName = field.Name,
                    DisplayName = field.DisplayName,
                    JsonType = field.JsonType,
                    Solidified = field.Solidified,
                    Column = field.Column,
                    Source = "代码",
                });
            }
        }

        return list;
    }

    /// <summary>读取登记表；读失败按空表处理，页面仍可展示代码字典与扫描结果。</summary>
    private static List<FieldRegistryEntry> LoadRegistered()
    {
        try
        {
            return FieldDict.FindAll()
                .OrderBy(e => e.Event, StringComparer.Ordinal)
                .ThenBy(e => e.FieldName, StringComparer.Ordinal)
                .Select(e => new FieldRegistryEntry
                {
                    Event = e.Event ?? String.Empty,
                    FieldName = e.FieldName,
                    DisplayName = e.DisplayName,
                    JsonType = e.JsonType,
                    Solidified = e.Solidified,
                    Source = "登记表",
                    DictId = e.Id,
                    Remark = e.Remark,
                })
                .ToList();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return [];
        }
    }

    /// <summary>从样例报文里取字段值：先查 <c>ExtJson</c>，再退到 <c>DataJson</c>。</summary>
    private static String ReadSample(DeviceEventRaw row, String fieldName)
    {
        foreach (var json in new[] { row.ExtJson, row.DataJson })
        {
            if (String.IsNullOrWhiteSpace(json)) continue;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(fieldName, out var value))
                {
                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                }
            }
            catch (JsonException)
            {
                // 原文损坏不影响统计：样例留空即可
            }
        }

        return null;
    }

    /// <summary>聚合中间态。</summary>
    private sealed class Bucket
    {
        public String Event { get; init; }
        public String Field { get; init; }
        public Int32 Count { get; private set; }
        public DateTime FirstSeen { get; private set; } = DateTime.MaxValue;
        public DateTime LastSeen { get; private set; } = DateTime.MinValue;
        public String Sample { get; private set; }
        public String SampleDeviceId { get; private set; }
        public Int64 SampleEventId { get; private set; }

        public void Touch(DateTime receivedAt, DeviceEventRaw row)
        {
            Count++;
            if (receivedAt < FirstSeen) FirstSeen = receivedAt;
            if (receivedAt <= LastSeen) return;

            LastSeen = receivedAt;

            // 样例取最新一条，顺带记录来源设备与报文主键，方便点名找对方确认
            Sample = ReadSample(row, Field);
            SampleDeviceId = row.DeviceId;
            SampleEventId = row.Id;
        }
    }
}
