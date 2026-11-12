using EFBM.Core;

namespace EFBM.Tests;

/// <summary>内外网部署基线与「禁止静默丢弃」的启动自检测试（验收用例 C14）。</summary>
public class EfbmOptionsTests
{
    [Fact(DisplayName = "配置_内网默认配置_启动自检通过")]
    public void Intranet_DefaultConfig_Passes()
    {
        var options = new EfbmOptions();

        Assert.False(options.IsInternet());
        options.ValidateOrThrow();
    }

    [Fact(DisplayName = "配置_外网缺HTTPS_启动自检失败")]
    public void Internet_WithoutHttps_Fails()
    {
        var options = new EfbmOptions { Deployment = "Internet", RequireHttps = false };

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    [Fact(DisplayName = "配置_外网启用匿名_启动自检失败")]
    public void Internet_WithAnonymousAuth_Fails()
    {
        var options = new EfbmOptions { Deployment = "Internet", RequireHttps = true, AuthMode = "Anonymous" };

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    [Fact(DisplayName = "配置_外网HTTPS加凭据_启动自检通过")]
    public void Internet_WithHttpsAndCredentials_Passes()
    {
        var options = new EfbmOptions { Deployment = "Internet", RequireHttps = true, AuthMode = "AppKeyAppSecret" };

        options.ValidateOrThrow();
    }

    [Fact(DisplayName = "配置_任何环境启用自动裁剪_启动自检失败")]
    public void AutoTrimEnabled_FailsInAnyEnvironment()
    {
        // 框架按长度 / 时间裁剪不判断消费组是否已消费，会静默删除未确认消息
        var options = new EfbmOptions { AutoTrimEnabled = true };

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    [Fact(DisplayName = "配置_默认值_与冻结契约一致")]
    public void Defaults_MatchContract()
    {
        var options = new EfbmOptions();

        Assert.Equal("efbm:events", options.StreamKey);
        Assert.Equal("efbm-persist", options.PersistGroup);
        Assert.Equal("X-App-Key", options.AppKeyHeader);
        Assert.Equal("X-App-Secret", options.AppSecretHeader);
        Assert.True(options.LocalFallbackEnabled);
        Assert.True(options.SafeTrimEnabled);
        Assert.False(options.AutoTrimEnabled);
        Assert.Equal(60, options.CredentialCacheSeconds);
        Assert.Equal(30, options.FallbackRequeueIntervalSeconds);
        Assert.Equal(100, options.FallbackRequeueBatchSize);
        Assert.True(options.TrustForwardedHeaders);
        Assert.Equal(4 * 1024 * 1024, options.MaxBodyBytes);
    }

    [Fact(DisplayName = "配置_未知认证模式_启动自检失败")]
    public void UnknownAuthMode_FailsStartup()
    {
        // 认证模式拼错会让所有上报被 401 且设备不重发，必须在启动时拦住
        var options = new EfbmOptions { AuthMode = "Token" };

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    [Theory(DisplayName = "配置_已知认证模式_大小写不敏感通过")]
    [InlineData("basic")]
    [InlineData("anonymous")]
    [InlineData("APPKEYAPPSECRET")]
    public void KnownAuthMode_PassesIgnoringCase(String mode)
    {
        var options = new EfbmOptions { AuthMode = mode };

        options.ValidateOrThrow();
    }

    [Fact(DisplayName = "配置_接收参数非法_启动自检失败")]
    public void InvalidReceiveParameters_FailStartup()
    {
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { MaxBodyBytes = 0 }.ValidateOrThrow());
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { FallbackRequeueIntervalSeconds = 0 }.ValidateOrThrow());
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { FallbackRequeueBatchSize = 0 }.ValidateOrThrow());
        Assert.Throws<InvalidOperationException>(() => new EfbmOptions { CredentialCacheSeconds = -1 }.ValidateOrThrow());
    }
}
