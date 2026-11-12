using System.Globalization;

namespace EFBM.Web.Queue;

/// <summary>安全裁剪的纯算法层：把「各消费组已确认边界 + 保留下限」计算成 <c>XTRIM MINID</c> 的阈值。</summary>
/// <remarks>
/// 核心不变量：<b>绝不删除任何消费组尚未确认的消息</b>。因此裁剪点 = 各组「最小已确认 ID」的最小值，
/// 再与「保留下限」（最近 N 天）取小——保留下限只会让裁剪点更靠前（删更多已确认旧数据），
/// 永远不会越过未确认边界。<see cref="ComputeEffectiveMs"/> 的 <c>Min(safe, floor)</c> 即此语义。
/// 本类不含任何 Redis 依赖，便于单元测试。
/// </remarks>
public static class StreamTrimCalculator
{
    /// <summary>Unix 纪元，用于 Stream ID（毫秒精度）与时间互转。</summary>
    internal static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>从 Stream ID（<c>毫秒-序号</c>）解析出毫秒前缀；非法 / 通配符返回 -1。</summary>
    /// <param name="id">Stream 消息 ID，如 <c>1731400000000-0</c>；<c>-</c> / <c>+</c> / <c>$</c> / <c>0</c> 视为无具体边界。</param>
    /// <returns>毫秒前缀；无法解析返回 -1。</returns>
    public static Int64 ParseMs(String id)
    {
        if (String.IsNullOrEmpty(id)) return -1;
        if (id is "-" or "+" or "$" or "0") return -1;

        var dash = id.IndexOf('-');
        var num = dash > 0 ? id[..dash] : id;
        return Int64.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? ms : -1;
    }

    /// <summary>按各消费组边界计算安全裁剪点（毫秒）。</summary>
    /// <param name="groups">各消费组视图；每个组取 pending 最小 ID（有未确认时）或 last-delivered-id（已全确认时）。</param>
    /// <param name="skipReason">无法裁剪时给出的原因（无消费组 / 某组边界不可解析）。</param>
    /// <returns>安全裁剪点的毫秒时间戳；需跳过时返回 -1 并在 <paramref name="skipReason"/> 说明。</returns>
    public static Int64 ComputeSafeMs(IReadOnlyList<GroupView> groups, out String skipReason)
    {
        skipReason = null;
        if (groups == null || groups.Count == 0)
        {
            skipReason = "无消费组，无法确定安全裁剪边界，跳过以免误删";
            return -1;
        }

        Int64? min = null;
        foreach (var g in groups)
        {
            // 有未确认消息：以最旧的 pending（StartId）为边界，确保它不被删除
            var ms = g.PendingCount > 0 && !String.IsNullOrEmpty(g.PendingMinId)
                ? ParseMs(g.PendingMinId)
                : ParseMs(g.LastDeliveredId);

            // 任一消费组边界无法解析时，保守地整体跳过，避免越过该组的未确认消息
            if (ms < 0)
            {
                skipReason = $"消费组 {g.Name ?? "(未知)"} 的裁剪边界无法解析，跳过以免误删";
                return -1;
            }

            min = min == null ? ms : Math.Min(min.Value, ms);
        }

        return min ?? -1;
    }

    /// <summary>合并「安全边界」与「保留下限」得到最终裁剪点。</summary>
    /// <param name="safeMs">各消费组最小已确认 ID 的毫秒。</param>
    /// <param name="floorMs">保留下限（最近 N 天对应的毫秒时间戳）。</param>
    /// <returns>
    /// <c>Min(safeMs, floorMs)</c>：下限只会让裁剪点更靠前（删更多已确认旧数据），
    /// 永远不会越过未确认边界；<paramref name="safeMs"/> 非法时返回 -1（不裁剪）。
    /// </returns>
    public static Int64 ComputeEffectiveMs(Int64 safeMs, Int64 floorMs)
        => safeMs <= 0 ? -1 : Math.Min(safeMs, floorMs);
}
