using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EFBM.Core;

// 📋 生效: instructions=[newlife-global,development] | skills=[project-architecture] | agent=default

/// <summary>访问控制与请求签名的纯函数实现：IP 白名单、HMAC 签名、定长时间比较（AUTH-4b）。</summary>
/// <remarks>
/// 放在 <see cref="EFBM.Core"/> 而不是控制器里，是因为这些逻辑全是「差一位就全放行 / 全拒绝」的类型：
/// 写在控制器里只能靠手工 curl 验证，写在这里可以逐条覆盖（CIDR 边界、时间窗、签名篡改）。
///
/// IP 白名单语义：<b>空清单表示不限制</b>，一旦配置了规则就只放行命中的地址。
/// 规则支持精确地址、CIDR（<c>10.0.0.0/8</c>）与通配 <c>*</c>；IPv4 与 IPv6 都按字节前缀比较。
/// 反向代理后的真实地址由调用方决定（可信代理由 <c>EFBM:TrustForwardedHeaders</c> 控制）。
///
/// HMAC 语义：<c>X-Timestamp</c> 为 Unix 秒，签名 = <c>hex(HMAC-SHA256(secret, timestamp + "\n" + body))</c>。
/// 把时间戳拼进签名（而不是只签 body）是为了让重放窗口可判定；时间窗外的请求一律拒绝。
/// </remarks>
public static class AccessPolicy
{
    /// <summary>默认 HMAC 时间窗（秒）：允许设备与服务器之间最多 5 分钟时钟偏差。</summary>
    public const Int32 DefaultHmacWindowSeconds = 300;

    /// <summary>判断来源 IP 是否命中白名单。</summary>
    /// <param name="ip">来源 IP 字符串，可为空。</param>
    /// <param name="rules">白名单规则；为空或 <c>null</c> 表示不限制。</param>
    /// <returns>放行返回 <c>true</c>。</returns>
    public static Boolean IsIpAllowed(String ip, IEnumerable<String> rules)
    {
        var list = Normalize(rules);
        if (list.Length == 0) return true;

        if (String.IsNullOrWhiteSpace(ip)) return false;
        if (!IPAddress.TryParse(ip.Trim(), out var address)) return false;
        address = Normalize(address);

        foreach (var rule in list)
        {
            if (rule == "*") return true;
            if (Matches(address, rule)) return true;
        }

        return false;
    }

    /// <summary>把配置里的逗号 / 分号 / 换行分隔清单拆成规则数组并去掉空白项。</summary>
    /// <param name="value">原始配置值。</param>
    /// <returns>规则数组；无有效项时返回空数组。</returns>
    public static String[] ParseList(String value)
    {
        if (String.IsNullOrWhiteSpace(value)) return Array.Empty<String>();

        return value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Length > 0)
            .ToArray();
    }

    /// <summary>计算请求签名。</summary>
    /// <param name="secret">密钥（设备 AppSecret）。</param>
    /// <param name="timestamp">Unix 秒字符串。</param>
    /// <param name="body">请求体原文。</param>
    /// <returns>小写十六进制签名。</returns>
    public static String ComputeSignature(String secret, String timestamp, String body)
    {
        if (secret == null) throw new ArgumentNullException(nameof(secret));

        var payload = Encoding.UTF8.GetBytes((timestamp ?? String.Empty) + "\n" + (body ?? String.Empty));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    /// <summary>校验请求签名与时间窗。</summary>
    /// <param name="secret">密钥。</param>
    /// <param name="timestamp">请求头里的 Unix 秒。</param>
    /// <param name="body">请求体原文。</param>
    /// <param name="signature">请求头里的签名。</param>
    /// <param name="windowSeconds">允许的时间偏差秒数；<c>≤0</c> 取默认值。</param>
    /// <param name="now">当前时间；测试可注入。</param>
    /// <returns>通过返回 <c>true</c>。</returns>
    public static Boolean VerifySignature(String secret, String timestamp, String body, String signature, Int32 windowSeconds = DefaultHmacWindowSeconds, DateTime? now = null)
    {
        if (String.IsNullOrEmpty(secret) || String.IsNullOrEmpty(timestamp) || String.IsNullOrEmpty(signature)) return false;
        if (!Int64.TryParse(timestamp, out var seconds)) return false;

        var window = windowSeconds <= 0 ? DefaultHmacWindowSeconds : windowSeconds;
        var current = (now ?? DateTime.UtcNow).ToUniversalTime();
        var sent = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        if (Math.Abs((current - sent).TotalSeconds) > window) return false;

        var expected = ComputeSignature(secret, timestamp, body);
        return SecureEquals(expected, signature.Trim());
    }

    /// <summary>定长时间字符串比较，避免通过响应耗时逐字符猜测密钥。</summary>
    /// <param name="expected">期望值。</param>
    /// <param name="actual">实际值。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    public static Boolean SecureEquals(String expected, String actual)
    {
        if (expected == null || actual == null) return false;

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        if (left.Length != right.Length) return false;

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>判断 Redis 地址是否落在私网 / 环回 / 链路本地范围内（外网部署不允许公网 Redis）。</summary>
    /// <param name="host">主机名或 IP，可带端口与 IPv6 方括号。</param>
    /// <returns>私网返回 <c>true</c>；主机名解析失败时返回 <c>true</c> 并交由调用方告警，避免 DNS 抖动导致启动失败。</returns>
    public static Boolean IsPrivateHost(String host)
    {
        if (String.IsNullOrWhiteSpace(host)) return true;

        var name = host.Trim();
        if (name.StartsWith('['))
        {
            // [::1]:6379 形式
            var end = name.IndexOf(']');
            if (end > 0) name = name[1..end];
        }
        else if (name.Count(c => c == ':') == 1)
        {
            // host:port 形式；IPv6 不带方括号时有多个冒号，不做端口拆分
            name = name[..name.IndexOf(':')];
        }

        name = name.Trim();
        if (String.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(name, out var address)) return IsPrivateAddress(Normalize(address));

        try
        {
            var addresses = Dns.GetHostAddresses(name);
            if (addresses.Length == 0) return true;
            return addresses.All(e => IsPrivateAddress(Normalize(e)));
        }
        catch (Exception)
        {
            // 解析不了就交给 Redis 连接阶段报错；这里不因为 DNS 不可用而拒绝启动
            return true;
        }
    }

    /// <summary>判断单个地址是否私网 / 环回 / 链路本地。</summary>
    /// <param name="address">已归一化的地址。</param>
    /// <returns>私网返回 <c>true</c>。</returns>
    public static Boolean IsPrivateAddress(IPAddress address)
    {
        if (address == null) return true;
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                10 => true,
                127 => true,
                169 => b[1] == 254,
                172 => b[1] >= 16 && b[1] <= 31,
                192 => b[1] == 168,
                _ => b[0] == 0,
            };
        }

        var bytes = address.GetAddressBytes();
        // fe80::/10 链路本地、fc00::/7 唯一本地地址、::1 环回
        if (address.Equals(IPAddress.IPv6Loopback)) return true;
        if ((bytes[0] & 0xFE) == 0xFC) return true;
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
        return false;
    }

    /// <summary>判断地址是否命中单条规则。</summary>
    private static Boolean Matches(IPAddress address, String rule)
    {
        var index = rule.IndexOf('/');
        if (index < 0)
        {
            return IPAddress.TryParse(rule, out var single) && Normalize(single).Equals(address);
        }

        if (!IPAddress.TryParse(rule[..index], out var network)) return false;
        if (!Int32.TryParse(rule[(index + 1)..], out var prefix)) return false;

        network = Normalize(network);
        var netBytes = network.GetAddressBytes();
        var addrBytes = address.GetAddressBytes();
        if (netBytes.Length != addrBytes.Length) return false;

        var maxPrefix = netBytes.Length * 8;
        if (prefix < 0 || prefix > maxPrefix) return false;

        var fullBytes = prefix / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (netBytes[i] != addrBytes[i]) return false;
        }

        var remainingBits = prefix % 8;
        if (remainingBits == 0) return true;

        var mask = (Byte)(0xFF << (8 - remainingBits));
        return (netBytes[fullBytes] & mask) == (addrBytes[fullBytes] & mask);
    }

    /// <summary>IPv4 映射到 IPv6 的地址还原成 IPv4，避免同一台机器因两种写法被判成两条。</summary>
    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static String[] Normalize(IEnumerable<String> rules) =>
        rules == null ? Array.Empty<String>() : rules.Where(e => !String.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToArray();
}
