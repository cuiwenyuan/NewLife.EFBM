using EFBM.Core;
using EFBM.Data.Entities;
using NewLife.Log;

namespace EFBM.Web.Services;

// 📋 生效: instructions=[newlife-global,xcode,development] | skills=[xcode-data-modeling] | agent=default

/// <summary>Beta 引导：按配置补齐引导设备与凭据，让联调无需等待 P4 后台发放页面。</summary>
/// <remarks>
/// 只在 <c>EFBM:Beta:Enabled=true</c> 时由 <c>Program</c> 在启动阶段调用一次，
/// 因此不进入接收链路、稳态零开销。语义是<b>幂等补齐</b>而不是覆盖：
/// 设备已存在则复用（被停用则重新启用）；凭据已存在则校验绑定关系，
/// 只有显式 <c>ResetSecret=true</c> 才改写 Secret。
/// 任何不一致都直接抛异常让启动失败——带着错误凭据上线，比启动失败更难排查。
/// </remarks>
public static class BetaSeedService
{
    /// <summary>确保引导设备与凭据存在。</summary>
    /// <param name="options">EFBM 配置节。</param>
    /// <exception cref="InvalidOperationException">已有凭据绑定了别的设备时抛出。</exception>
    public static void SeedOrThrow(EfbmOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var beta = options.Beta;
        if (beta == null || !beta.Enabled) return;

        var device = Device.FindByDeviceId(beta.DeviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = beta.DeviceId,
                Name = beta.DeviceName,
                Enabled = true,
                Remark = "Beta 引导设备",
            };
            device.Insert();
            XTrace.WriteLine("Beta 引导：已创建设备 {0}（{1}）", beta.DeviceId, beta.DeviceName);
        }
        else if (!device.Enabled)
        {
            device.Enabled = true;
            device.Update();
            XTrace.WriteLine("Beta 引导：设备 {0} 原为停用，已重新启用", beta.DeviceId);
        }

        var credential = AppCredential.FindByAppKey(beta.AppKey);
        if (credential == null)
        {
            credential = new AppCredential
            {
                AppKey = beta.AppKey,
                AppSecret = beta.AppSecret,
                DeviceId = beta.DeviceId,
                Enabled = true,
                Remark = "Beta 引导凭据（配置驱动，P4 后应改为后台发放）",
            };
            credential.Insert();
            XTrace.WriteLine("Beta 引导：已创建凭据 appKey={0} deviceId={1} secret={2}", beta.AppKey, beta.DeviceId, Mask(beta.AppSecret));
        }
        else if (!String.Equals(credential.DeviceId, beta.DeviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Beta 引导凭据 {beta.AppKey} 已绑定设备 {credential.DeviceId}，与配置的 {beta.DeviceId} 不一致；请改用其他 AppKey 或清理旧记录");
        }
        else
        {
            var dirty = false;
            if (!credential.Enabled) { credential.Enabled = true; dirty = true; }
            if (beta.ResetSecret && !String.Equals(credential.AppSecret, beta.AppSecret, StringComparison.Ordinal))
            {
                credential.AppSecret = beta.AppSecret;
                dirty = true;
            }
            if (dirty)
            {
                credential.Update();
                XTrace.WriteLine("Beta 引导：已更新凭据 appKey={0}（重置Secret={1}）", beta.AppKey, beta.ResetSecret);
            }
        }

        XTrace.WriteLine("Beta 引导完成：deviceId={0} appKey={1}，可直接用于 POST {2}", beta.DeviceId, beta.AppKey, EfbmDefaults.EventsRoute);
    }

    /// <summary>日志脱敏：只保留首尾各两位，避免完整 Secret 落到日志。</summary>
    /// <param name="secret">原始 Secret。</param>
    /// <returns>脱敏后的字符串。</returns>
    private static String Mask(String secret)
    {
        if (String.IsNullOrEmpty(secret)) return "(empty)";
        if (secret.Length <= 4) return new String('*', secret.Length);
        return $"{secret[..2]}{new String('*', secret.Length - 4)}{secret[^2..]}";
    }
}
