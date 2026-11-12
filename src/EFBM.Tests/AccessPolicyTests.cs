using EFBM.Core;

namespace EFBM.Tests;

/// <summary>访问控制：IP 白名单、HMAC 签名与私网判定（AUTH-4b）。</summary>
/// <remarks>
/// 这类逻辑的失败模式都是「全放行」或「全拒绝」，靠手工 curl 很难覆盖边界，
/// 因此逐条钉住：CIDR 边界、IPv4 映射地址、通配、空清单、时间窗、签名篡改。
/// </remarks>
public sealed class AccessPolicyTests
{
    [Fact(DisplayName = "IP白名单_空清单表示不限制")]
    public void Ip_EmptyAllowsAll()
    {
        Assert.True(AccessPolicy.IsIpAllowed("203.0.113.9", null));
        Assert.True(AccessPolicy.IsIpAllowed("203.0.113.9", Array.Empty<String>()));
        Assert.True(AccessPolicy.IsIpAllowed(null, Array.Empty<String>()));
    }

    [Theory(DisplayName = "IP白名单_精确地址")]
    [InlineData("10.1.2.3", "10.1.2.3", true)]
    [InlineData("10.1.2.4", "10.1.2.3", false)]
    [InlineData("::1", "::1", true)]
    public void Ip_Exact(String ip, String rule, Boolean expected)
        => Assert.Equal(expected, AccessPolicy.IsIpAllowed(ip, new[] { rule }));

    [Theory(DisplayName = "IP白名单_CIDR 边界")]
    [InlineData("10.1.2.3", "10.0.0.0/8", true)]
    [InlineData("11.1.2.3", "10.0.0.0/8", false)]
    [InlineData("192.168.1.255", "192.168.1.0/24", true)]
    [InlineData("192.168.2.1", "192.168.1.0/24", false)]
    [InlineData("172.16.5.9", "172.16.0.0/12", true)]
    [InlineData("172.32.5.9", "172.16.0.0/12", false)]
    [InlineData("192.168.1.130", "192.168.1.128/25", true)]
    [InlineData("192.168.1.127", "192.168.1.128/25", false)]
    [InlineData("2001:db8::1", "2001:db8::/32", true)]
    [InlineData("2001:db9::1", "2001:db8::/32", false)]
    public void Ip_Cidr(String ip, String rule, Boolean expected)
        => Assert.Equal(expected, AccessPolicy.IsIpAllowed(ip, new[] { rule }));

    [Fact(DisplayName = "IP白名单_通配与非法输入")]
    public void Ip_WildcardAndBadInput()
    {
        Assert.True(AccessPolicy.IsIpAllowed("8.8.8.8", new[] { "*" }));

        // 无法解析的来源地址一律拒绝：配置了白名单就不该放行「不知道是谁」
        Assert.False(AccessPolicy.IsIpAllowed("not-an-ip", new[] { "10.0.0.0/8" }));
        Assert.False(AccessPolicy.IsIpAllowed(null, new[] { "10.0.0.0/8" }));

        // 规则本身写错不能变成放行
        Assert.False(AccessPolicy.IsIpAllowed("10.0.0.1", new[] { "10.0.0.0/99" }));
        Assert.False(AccessPolicy.IsIpAllowed("10.0.0.1", new[] { "garbage" }));
    }

    [Fact(DisplayName = "IP白名单_IPv4映射地址按 IPv4 处理")]
    public void Ip_MappedIpv4()
        => Assert.True(AccessPolicy.IsIpAllowed("::ffff:10.1.2.3", new[] { "10.0.0.0/8" }));

    [Fact(DisplayName = "IP白名单_多规则与分隔符解析")]
    public void Ip_ParseList()
    {
        var rules = AccessPolicy.ParseList(" 10.0.0.0/8 , 192.168.0.0/16;; 172.16.0.0/12 \n");

        Assert.Equal(3, rules.Length);
        Assert.Equal("10.0.0.0/8", rules[0]);
        Assert.Equal("172.16.0.0/12", rules[2]);
        Assert.Empty(AccessPolicy.ParseList("   "));
        Assert.Empty(AccessPolicy.ParseList(null));
    }

    [Fact(DisplayName = "HMAC_签名可复算且大小写无关")]
    public void Hmac_Compute_Stable()
    {
        var signature = AccessPolicy.ComputeSignature("secret", "1700000000", "{\"a\":1}");

        Assert.Equal(64, signature.Length);
        Assert.Equal(signature, AccessPolicy.ComputeSignature("secret", "1700000000", "{\"a\":1}"));
        Assert.NotEqual(signature, AccessPolicy.ComputeSignature("secret2", "1700000000", "{\"a\":1}"));
        Assert.NotEqual(signature, AccessPolicy.ComputeSignature("secret", "1700000001", "{\"a\":1}"));
        Assert.NotEqual(signature, AccessPolicy.ComputeSignature("secret", "1700000000", "{\"a\":2}"));
    }

    [Fact(DisplayName = "HMAC_时间窗内通过_窗外与篡改拒绝")]
    public void Hmac_Verify_Window()
    {
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var timestamp = new DateTimeOffset(now).ToUnixTimeSeconds().ToString();
        var body = "{\"event\":\"ORDER_COMPLETED\"}";
        var signature = AccessPolicy.ComputeSignature("s3cret", timestamp, body);

        Assert.True(AccessPolicy.VerifySignature("s3cret", timestamp, body, signature, 300, now));

        // 签名被改一个字符
        var tampered = (signature[0] == 'a' ? "b" : "a") + signature[1..];
        Assert.False(AccessPolicy.VerifySignature("s3cret", timestamp, body, tampered, 300, now));

        // 报文被改
        Assert.False(AccessPolicy.VerifySignature("s3cret", timestamp, body + " ", signature, 300, now));

        // 时间窗外（10 分钟前，窗口 5 分钟）
        var stale = new DateTimeOffset(now.AddMinutes(-10)).ToUnixTimeSeconds().ToString();
        var staleSignature = AccessPolicy.ComputeSignature("s3cret", stale, body);
        Assert.False(AccessPolicy.VerifySignature("s3cret", stale, body, staleSignature, 300, now));

        // 缺头
        Assert.False(AccessPolicy.VerifySignature("s3cret", null, body, signature, 300, now));
        Assert.False(AccessPolicy.VerifySignature("s3cret", timestamp, body, null, 300, now));
        Assert.False(AccessPolicy.VerifySignature(null, timestamp, body, signature, 300, now));

        // 时间戳不是数字
        Assert.False(AccessPolicy.VerifySignature("s3cret", "abc", body, signature, 300, now));
    }

    [Fact(DisplayName = "定长比较_空值与长度不同都返回 false")]
    public void SecureEquals_Basics()
    {
        Assert.True(AccessPolicy.SecureEquals("abc", "abc"));
        Assert.False(AccessPolicy.SecureEquals("abc", "abd"));
        Assert.False(AccessPolicy.SecureEquals("abc", "ab"));
        Assert.False(AccessPolicy.SecureEquals(null, "abc"));
        Assert.False(AccessPolicy.SecureEquals("abc", null));
    }

    [Theory(DisplayName = "私网判定_环回私网链路本地都算私网")]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void Private_Address(String ip, Boolean expected)
        => Assert.Equal(expected, AccessPolicy.IsPrivateHost(ip));

    [Fact(DisplayName = "私网判定_带端口与 localhost")]
    public void Private_HostWithPort()
    {
        Assert.True(AccessPolicy.IsPrivateHost("127.0.0.1:6379"));
        Assert.True(AccessPolicy.IsPrivateHost("[::1]:6379"));
        Assert.True(AccessPolicy.IsPrivateHost("localhost:6379"));
        Assert.False(AccessPolicy.IsPrivateHost("8.8.8.8:6379"));
        Assert.True(AccessPolicy.IsPrivateHost(""));
    }
}
