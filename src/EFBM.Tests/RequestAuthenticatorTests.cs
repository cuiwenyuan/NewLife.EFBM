using System.Text;
using EFBM.Core;

namespace EFBM.Tests;

/// <summary>三模式鉴权与按设备凭据校验（需求说明 FR-2、验收用例 C3）。</summary>
public class RequestAuthenticatorTests
{
    /// <summary>构造一个已注册有效凭据的存储。</summary>
    /// <returns>凭据存储替身。</returns>
    private static FakeCredentialStore Store() => new FakeCredentialStore().With(FakeCredentialStore.Valid());

    /// <summary>构造 AppKeyAppSecret 模式的请求。</summary>
    /// <param name="key">AppKey。</param>
    /// <param name="secret">AppSecret。</param>
    /// <param name="deviceId">报文设备编号。</param>
    /// <param name="internet">是否外网。</param>
    /// <returns>鉴权输入。</returns>
    private static AuthRequest HeaderRequest(String key = "key-1", String secret = "secret-1", String deviceId = "FBOX-A01", Boolean internet = false) => new()
    {
        AuthMode = AuthModes.AppKeyAppSecret,
        AppKey = key,
        AppSecret = secret,
        DeviceId = deviceId,
        IsInternet = internet,
    };

    [Fact(DisplayName = "鉴权_匿名内网_通过且完全不查库")]
    public async Task Anonymous_Intranet_PassesWithoutDb()
    {
        var store = Store();
        var authenticator = new RequestAuthenticator(store);

        var result = await authenticator.AuthenticateAsync(new AuthRequest { AuthMode = AuthModes.Anonymous, DeviceId = "FBOX-A01" });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, store.CallCount);
    }

    [Fact(DisplayName = "鉴权_匿名外网_拒绝并返回403")]
    public async Task Anonymous_Internet_IsForbidden()
    {
        var authenticator = new RequestAuthenticator(Store());

        var result = await authenticator.AuthenticateAsync(new AuthRequest { AuthMode = AuthModes.Anonymous, IsInternet = true, DeviceId = "FBOX-A01" });

        Assert.Equal(403, result.StatusCode);
    }

    [Fact(DisplayName = "鉴权_AppKeyAppSecret正确_通过")]
    public async Task AppKeyAppSecret_Valid_Passes()
    {
        var authenticator = new RequestAuthenticator(Store());

        Assert.True((await authenticator.AuthenticateAsync(HeaderRequest())).IsSuccess);
    }

    [Theory(DisplayName = "鉴权_凭据缺失或错误_统一返回401且不泄露密钥")]
    [InlineData(null, "secret-1")]
    [InlineData("key-1", null)]
    [InlineData("key-1", "")]
    [InlineData("unknown", "secret-1")]
    [InlineData("key-1", "wrong-secret")]
    [InlineData("key-1", "SECRET-1")]
    public async Task InvalidCredential_Returns401(String key, String secret)
    {
        var authenticator = new RequestAuthenticator(Store());

        var result = await authenticator.AuthenticateAsync(HeaderRequest(key, secret));

        Assert.Equal(401, result.StatusCode);
        Assert.DoesNotContain("secret", result.Message.ToLowerInvariant());
    }

    [Fact(DisplayName = "鉴权_凭据已停用_返回403")]
    public async Task DisabledCredential_Returns403()
    {
        var store = new FakeCredentialStore().With(FakeCredentialStore.Valid(enabled: false));
        var authenticator = new RequestAuthenticator(store);

        Assert.Equal(403, (await authenticator.AuthenticateAsync(HeaderRequest())).StatusCode);
    }

    [Fact(DisplayName = "鉴权_凭据与报文deviceId不匹配_返回403防串用")]
    public async Task DeviceMismatch_Returns403()
    {
        var authenticator = new RequestAuthenticator(Store());

        Assert.Equal(403, (await authenticator.AuthenticateAsync(HeaderRequest(deviceId: "FBOX-B02"))).StatusCode);
    }

    [Fact(DisplayName = "鉴权_Basic正确_用户名密码复用AppKey与AppSecret")]
    public async Task Basic_Valid_Passes()
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("key-1:secret-1"));
        var authenticator = new RequestAuthenticator(Store());
        var request = new AuthRequest { AuthMode = AuthModes.Basic, Authorization = $"Basic {token}", DeviceId = "FBOX-A01" };

        Assert.True((await authenticator.AuthenticateAsync(request)).IsSuccess);
    }

    [Theory(DisplayName = "鉴权_Basic格式非法_返回401")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic ")]
    [InlineData("Basic $$$")]
    [InlineData("Basic a2V5LTE=")]           // base64 为 "key-1"，无冒号
    public async Task Basic_Malformed_Returns401(String header)
    {
        var authenticator = new RequestAuthenticator(Store());
        var request = new AuthRequest { AuthMode = AuthModes.Basic, Authorization = header, DeviceId = "FBOX-A01" };

        Assert.Equal(401, (await authenticator.AuthenticateAsync(request)).StatusCode);
    }

    [Fact(DisplayName = "鉴权_凭据库不可用_返回401并留告警线索而非500")]
    public async Task StoreFailure_Returns401()
    {
        var store = Store();
        store.ThrowOnFind = true;
        var authenticator = new RequestAuthenticator(store);

        var result = await authenticator.AuthenticateAsync(HeaderRequest());

        Assert.Equal(401, result.StatusCode);
        Assert.Contains("不可用", result.Message);
    }

    [Fact(DisplayName = "鉴权_未知认证模式_兜底返回401")]
    public async Task UnknownMode_Returns401()
    {
        var authenticator = new RequestAuthenticator(Store());

        Assert.Equal(401, (await authenticator.AuthenticateAsync(new AuthRequest { AuthMode = "Whatever", DeviceId = "FBOX-A01" })).StatusCode);
    }

    [Fact(DisplayName = "AuthModes_归一化与白名单_大小写不敏感且空值回落默认")]
    public void AuthModes_Normalize()
    {
        Assert.True(AuthModes.IsKnown("appkeyappsecret"));
        Assert.False(AuthModes.IsKnown("nope"));
        Assert.Equal(AuthModes.AppKeyAppSecret, AuthModes.Normalize(null));
        Assert.Equal(AuthModes.Basic, AuthModes.Normalize("basic"));
        Assert.Equal(3, AuthModes.All.Length);
    }
}
