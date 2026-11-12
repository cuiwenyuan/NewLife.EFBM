using System.Text.Json;
using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;
using XCode;

namespace EFBM.Web.Services;

/// <summary>字段固化回溯回填的请求参数（FLD-2c）。</summary>
public sealed class BackfillRequest
{
    /// <summary>只回填某个事件；为空表示全部事件。</summary>
    public String Event { get; set; }

    /// <summary>只回填某个字段；为空表示按登记表里「已固化」的字段全部回填。</summary>
    public String FieldName { get; set; }

    /// <summary>接收时间下界（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>接收时间上界（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>本次最多处理多少条报文。</summary>
    public Int32 Limit { get; set; } = FieldBackfillService.DefaultBatchSize;

    /// <summary>试运行：只统计会补多少条，不写库。</summary>
    public Boolean DryRun { get; set; }
}

/// <summary>回填结果。</summary>
public sealed class BackfillResult
{
    /// <summary>是否执行成功（前置条件满足即成功，单条失败见 <see cref="Failed"/>）。</summary>
    public Boolean Success { get; init; }

    /// <summary>失败原因。</summary>
    public String Reason { get; init; }

    /// <summary>本次回填的字段清单。</summary>
    public IReadOnlyList<String> Fields { get; init; } = Array.Empty<String>();

    /// <summary>扫描的报文条数。</summary>
    public Int32 Scanned { get; init; }

    /// <summary>实际写入（或试运行下「将写入」）的条数。</summary>
    public Int32 Updated { get; init; }

    /// <summary>失败条数。</summary>
    public Int32 Failed { get; init; }

    /// <summary>是否还有未处理的数据（受 <see cref="BackfillRequest.Limit"/> 限制）。</summary>
    public Boolean Truncated { get; init; }

    /// <summary>是否试运行。</summary>
    public Boolean DryRun { get; init; }

    /// <summary>构造前置条件失败结果。</summary>
    /// <param name="reason">原因。</param>
    /// <returns>失败结果。</returns>
    public static BackfillResult Blocked(String reason) => new() { Success = false, Reason = reason };
}

/// <summary>字段固化后的历史回溯回填（FLD-2c、FR-28）。</summary>
/// <remarks>
/// 为什么必须有它：设备<b>不重发</b>历史报文，字段固化只对「从今天起」生效，
/// 过去的记录里该字段仍留在 <c>ExtJson</c> / <c>RawJson</c> 中。回填把这些值补进
/// <c>ExtJson</c>，让新登记字段的历史数据也能被检索；若后续为该字段加了正式列，
/// 只需在这里增加一次赋值即可复用同一套扫描。
///
/// 三条不变量：
/// ① <b>不覆盖已有值</b>——<c>ExtJson</c> 里已存在的键一律跳过，回填只补空缺；
/// ② <b>不改原始报文</b>——<c>RawJson</c> 是唯一真相源，任何情况下都不重写；
/// ③ <b>必须留痕</b>——每次回填写一条审计（字段、条数、范围、操作人），
///    否则「这批历史是什么时候补的」将无从考证。
/// </remarks>
public sealed class FieldBackfillService
{
    /// <summary>默认单次处理条数。</summary>
    public const Int32 DefaultBatchSize = 500;

    /// <summary>单次处理硬上限，防止一次把几十万条报文读进内存。</summary>
    public const Int32 MaxBatchSize = 5000;

    /// <summary>执行一次回填。</summary>
    /// <param name="request">回填参数。</param>
    /// <param name="operatorName">操作人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回填结果。</returns>
    public Task<BackfillResult> BackfillAsync(BackfillRequest request, String operatorName, CancellationToken cancellationToken = default)
    {
        request ??= new BackfillRequest();
        cancellationToken.ThrowIfCancellationRequested();

        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
            return Task.FromResult(BackfillResult.Blocked("起始时间不能晚于结束时间"));

        var fields = ResolveFields(request.Event, request.FieldName);
        if (fields.Length == 0)
            return Task.FromResult(BackfillResult.Blocked(String.IsNullOrWhiteSpace(request.FieldName)
                ? "没有可回填的字段：请先在「字段演进」页把字段标记为已固化，或指定字段名"
                : $"字段 {request.FieldName} 未登记，无法回填"));

        var limit = request.Limit <= 0 ? DefaultBatchSize : Math.Min(request.Limit, MaxBatchSize);

        Expression exp = null;
        exp = And(exp, String.IsNullOrWhiteSpace(request.Event) ? null : DeviceEventRaw._.Event == request.Event.Trim());
        exp = And(exp, request.From.HasValue ? DeviceEventRaw._.ReceivedAt >= request.From.Value : null);
        exp = And(exp, request.To.HasValue ? DeviceEventRaw._.ReceivedAt <= request.To.Value : null);

        // 多取一条用于判断是否还有剩余；按主键正序，保证重复执行时是推进而不是随机命中
        var list = DeviceEventRaw.FindAll(exp, DeviceEventRaw._.Id.Asc(), null, 0, limit + 1);
        var truncated = list.Count > limit;
        if (truncated) list = list.Take(limit).ToList();

        var scanned = 0;
        var updated = 0;
        var failed = 0;

        foreach (var entity in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            try
            {
                if (TryMergeFields(entity, fields, out var merged) && merged != null)
                {
                    if (!request.DryRun)
                    {
                        entity.ExtJson = merged;
                        entity.Update();
                    }

                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                XTrace.WriteLine("字段回填失败：id={0} eventKey={1} {2}: {3}", entity.Id, entity.EventKey, ex.GetType().Name, ex.Message);
            }
        }

        var summary = $"字段={String.Join(",", fields)} 扫描={scanned} 回填={updated} 失败={failed}"
            + (truncated ? "（已达单次上限，可再次执行继续）" : String.Empty)
            + (request.DryRun ? "［试运行］" : String.Empty);

        XTrace.WriteLine("字段回溯回填：{0} 操作人={1}", summary, operatorName);
        AdminAudit.Write("字段回溯回填", failed == 0, summary, operatorName);

        return Task.FromResult(new BackfillResult
        {
            Success = true,
            Fields = fields,
            Scanned = scanned,
            Updated = updated,
            Failed = failed,
            Truncated = truncated,
            DryRun = request.DryRun,
        });
    }

    /// <summary>解析待回填字段：指定字段优先，否则取登记表里已固化的非信封字段。</summary>
    /// <param name="eventName">事件名，可为空。</param>
    /// <param name="fieldName">字段名，可为空。</param>
    /// <returns>字段名数组。</returns>
    public static String[] ResolveFields(String eventName, String fieldName)
    {
        if (!String.IsNullOrWhiteSpace(fieldName)) return [fieldName.Trim()];

        IEnumerable<FieldDict> query = FieldDict.FindAll();
        if (!String.IsNullOrWhiteSpace(eventName))
            query = query.Where(e => String.Equals(e.Event, eventName.Trim(), StringComparison.Ordinal));

        // 信封字段（Event 为空）已经落在正式列上，重复搬进 ExtJson 只会制造噪声
        return query
            .Where(e => e.Solidified && !String.IsNullOrWhiteSpace(e.FieldName) && !String.IsNullOrWhiteSpace(e.Event))
            .Select(e => e.FieldName.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>把报文里缺失的登记字段补进 <c>ExtJson</c>。</summary>
    /// <param name="entity">原始报文。</param>
    /// <param name="fields">待补字段。</param>
    /// <param name="mergedJson">合并后的 JSON；无需变更时为 <c>null</c>。</param>
    /// <returns>有变更返回 <c>true</c>。</returns>
    private static Boolean TryMergeFields(DeviceEventRaw entity, String[] fields, out String mergedJson)
    {
        mergedJson = null;

        var source = String.IsNullOrWhiteSpace(entity.DataJson) ? ExtractDataJson(entity.RawJson) : entity.DataJson;
        if (String.IsNullOrWhiteSpace(source)) return false;

        using var sourceDoc = JsonDocument.Parse(source);
        if (sourceDoc.RootElement.ValueKind != JsonValueKind.Object) return false;

        var map = new Dictionary<String, JsonElement>(StringComparer.Ordinal);
        if (!String.IsNullOrWhiteSpace(entity.ExtJson))
        {
            try
            {
                using var existing = JsonDocument.Parse(entity.ExtJson);
                if (existing.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in existing.RootElement.EnumerateObject()) map[property.Name] = property.Value.Clone();
                }
            }
            catch (JsonException)
            {
                // 已损坏的 ExtJson 不阻断回填：从原文重建一份，至少不会比现在更差
                map.Clear();
            }
        }

        var added = 0;
        foreach (var name in fields)
        {
            if (map.ContainsKey(name)) continue;
            if (!sourceDoc.RootElement.TryGetProperty(name, out var value)) continue;

            map[name] = value.Clone();
            added++;
        }

        if (added == 0) return false;

        mergedJson = JsonSerializer.Serialize(map);
        return true;
    }

    /// <summary>从原始报文里取 <c>data</c> 节点；没有则取根对象。</summary>
    /// <param name="rawJson">原始报文。</param>
    /// <returns>JSON 文本；无法解析返回 <c>null</c>。</returns>
    private static String ExtractDataJson(String rawJson)
    {
        if (String.IsNullOrWhiteSpace(rawJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? data.GetRawText()
                : doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>以 AND 叠加条件。</summary>
    private static Expression And(Expression left, Expression right)
    {
        if (left == null) return right;
        if (right == null) return left;
        return left & right;
    }
}
