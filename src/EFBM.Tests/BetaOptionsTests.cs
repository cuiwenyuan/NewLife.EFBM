using EFBM.Core;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,development,testing-strategy] | skills=[testing-strategy] | agent=default

/// <summary>Beta 引导配置的启动自检：它会把 AppSecret 明文放进配置文件，必须严格限制使用范围。</summary>
public class BetaOptionsTests
{
    [Fact(DisplayName = "Beta引导_默认关闭_校验直接通过")]
    public void Disabled_DoesNotValidateSecret()
    {
        var beta = new BetaOptions();
        Assert.False(beta.Enabled);

        beta.ValidateOrThrow(false);
        beta.ValidateOrThrow(true);
    }

    [Fact(DisplayName = "Beta引导_外网启用_启动失败")]
    public void Internet_Enabled_Throws()
    {
        var beta = new BetaOptions { Enabled = true, AppKey = "beta-key", AppSecret = "beta-secret-2026" };

        var ex = Assert.Throws<InvalidOperationException>(() => beta.ValidateOrThrow(true));
        Assert.Contains("外网", ex.Message);
    }

    [Theory(DisplayName = "Beta引导_密钥缺失或过短_启动失败")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void WeakSecret_Throws(String secret)
    {
        var beta = new BetaOptions { Enabled = true, AppKey = "beta-key", AppSecret = secret };

        Assert.Throws<InvalidOperationException>(() => beta.ValidateOrThrow(false));
    }

    [Fact(DisplayName = "Beta引导_内网且密钥合格_校验通过")]
    public void Intranet_Valid_Passes()
    {
        var beta = new BetaOptions { Enabled = true, AppKey = "beta-key", AppSecret = "beta-secret-2026" };

        beta.ValidateOrThrow(false);
    }

    [Fact(DisplayName = "配置_外网启用Beta引导_启动自检失败")]
    public void EfbmOptions_InternetWithBeta_Throws()
    {
        var options = new EfbmOptions
        {
            Deployment = "Internet",
            RequireHttps = true,
            AuthMode = "AppKeyAppSecret",
            Beta = new BetaOptions { Enabled = true, AppKey = "beta-key", AppSecret = "beta-secret-2026" },
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateOrThrow());
    }

    [Fact(DisplayName = "配置_内网启用Beta引导_启动自检通过")]
    public void EfbmOptions_IntranetWithBeta_Passes()
    {
        var options = new EfbmOptions
        {
            Deployment = "Intranet",
            Beta = new BetaOptions { Enabled = true, AppKey = "beta-key", AppSecret = "beta-secret-2026" },
        };

        options.ValidateOrThrow();
    }
}
