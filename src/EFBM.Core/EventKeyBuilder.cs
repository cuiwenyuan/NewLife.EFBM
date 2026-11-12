using System.Security.Cryptography;
using System.Text;

namespace EFBM.Core;

/// <summary>全链路幂等键生成器。</summary>
/// <remarks>
/// <c>EventKey = SHA1(event|deviceId|timestamp)</c>，其中 <c>timestamp</c> 使用设备侧原始字符串，
/// <b>不含 <c>data</c></b>，因此业务字段新增 / 改名 / 缺失都不会改变历史去重结果。
/// 单件折盒节拍大于 1 秒，同一设备不会在相同 timestamp 产生两条不同事件，三元组天然唯一（需求说明 §5.2）。
/// </remarks>
public static class EventKeyBuilder
{
    /// <summary>计算幂等键。</summary>
    /// <param name="eventName">报文 <c>event</c> 值。</param>
    /// <param name="deviceId">报文 <c>deviceId</c> 值。</param>
    /// <param name="timestampRaw">报文 <c>timestamp</c> 原始字符串，不得先做时间归一化。</param>
    /// <returns>40 位小写十六进制 SHA1 字符串。</returns>
    public static String Compute(String eventName, String deviceId, String timestampRaw)
    {
        // 三个字段都按原始值拼接：任何 trim / 大小写 / 时区转换都会改变历史去重结果
        var input = String.Concat(eventName ?? String.Empty, "|", deviceId ?? String.Empty, "|", timestampRaw ?? String.Empty);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>按信封计算幂等键，语义同 <see cref="Compute"/>。</summary>
    /// <param name="envelope">已解析的报文信封。</param>
    /// <returns>40 位小写十六进制 SHA1 字符串。</returns>
    public static String Compute(EventEnvelope envelope)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        return Compute(envelope.Event, envelope.DeviceId, envelope.TimestampRaw);
    }
}
