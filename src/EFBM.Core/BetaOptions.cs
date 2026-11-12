namespace EFBM.Core;

/// <summary>Beta 阶段的配置驱动引导：自动准备一台设备与一套凭据，免去后台发放页面（P4）。</summary>
/// <remarks>
/// 后台「设备与凭据管理」属于 P4，在它上线前 Beta 无法联调：没有凭据，所有上报都会 401，
/// 而设备失败不重发。本配置节只解决<b>联调起步</b>问题，不是正式发放通道：
/// ① 默认关闭，仅内网允许开启，外网部署启动自检直接拒绝；
/// ② AppSecret 明文写在配置文件，正式环境必须改用后台发放 + 密钥管理；
/// ③ 只补齐缺失记录，不覆盖已有凭据，除非显式 <see cref="ResetSecret"/>；
/// ④ 引导只写设备与凭据两张表，不参与接收链路，稳态不产生任何数据库 IO。
/// </remarks>
public sealed class BetaOptions
{
    /// <summary>是否启用配置驱动引导。</summary>
    public Boolean Enabled { get; set; }

    /// <summary>引导设备编号，与报文 <c>deviceId</c> 一致。</summary>
    public String DeviceId { get; set; } = "FBOX-A01";

    /// <summary>引导设备名称，仅后台展示用。</summary>
    public String DeviceName { get; set; } = "Beta 联调设备";

    /// <summary>引导凭据的 AppKey（<c>X-App-Key</c>）。</summary>
    public String AppKey { get; set; } = "beta-fbox-a01";

    /// <summary>引导凭据的 AppSecret（<c>X-App-Secret</c>）；Beta 可明文，正式环境禁止。</summary>
    public String AppSecret { get; set; }

    /// <summary>启动时是否用配置值覆盖已有凭据的 Secret；默认关闭，避免误改已发放凭据。</summary>
    public Boolean ResetSecret { get; set; }

    /// <summary>校验引导配置；仅在 <see cref="Enabled"/> 为 <c>true</c> 时生效。</summary>
    /// <param name="isInternet">是否为外网部署。</param>
    /// <exception cref="InvalidOperationException">配置不合法或外网启用时抛出。</exception>
    public void ValidateOrThrow(Boolean isInternet)
    {
        if (!Enabled) return;

        if (isInternet)
            throw new InvalidOperationException("Beta 引导凭据会把 AppSecret 明文写入配置文件，外网部署禁止启用（EFBM:Beta:Enabled=false）");

        if (String.IsNullOrWhiteSpace(AppKey))
            throw new InvalidOperationException("Beta 引导凭据缺少 AppKey（EFBM:Beta:AppKey）");

        // 过短的 Secret 等于没有认证；8 位只是下限，正式凭据应由后台随机生成
        if (String.IsNullOrWhiteSpace(AppSecret) || AppSecret.Length < 8)
            throw new InvalidOperationException("Beta 引导凭据的 AppSecret 至少 8 位（EFBM:Beta:AppSecret）");

        if (String.IsNullOrWhiteSpace(DeviceId))
            throw new InvalidOperationException("Beta 引导缺少设备编号（EFBM:Beta:DeviceId）");
    }
}
