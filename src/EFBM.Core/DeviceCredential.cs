namespace EFBM.Core;

/// <summary>按设备发放的凭据视图，供接入层校验使用（不含任何数据层类型）。</summary>
/// <remarks>
/// 这是 <c>AppCredential</c> 实体的只读投影：接收层不依赖 XCode，
/// 由 <c>EFBM.Web/Services/DeviceCredentialStore</c> 查询并做短 TTL 缓存后返回。
/// <see cref="AppSecret"/> 禁止写入任何日志与响应。
/// </remarks>
public sealed class DeviceCredential
{
    /// <summary>应用键，唯一。</summary>
    public String AppKey { get; set; }

    /// <summary>应用密钥；仅用于服务端比对，禁止外泄。</summary>
    public String AppSecret { get; set; }

    /// <summary>绑定的设备编号，必须与报文 <c>deviceId</c> 一致。</summary>
    public String DeviceId { get; set; }

    /// <summary>是否启用；停用后一律 403。</summary>
    public Boolean Enabled { get; set; }
}

/// <summary>凭据读取抽象：让 Core 不依赖数据库，也让同步链路只做「读」不做「写」。</summary>
/// <remarks>
/// 匿名模式不会调用本接口；凭据模式由 Web 层实现并加进程内缓存，
/// 因此稳态下逐件上报不产生数据库 IO（需求说明 §3.5 第 4 条、FR-20）。
/// </remarks>
public interface IDeviceCredentialStore
{
    /// <summary>按 AppKey 查询凭据。</summary>
    /// <param name="appKey">请求携带的 AppKey 或 Basic 用户名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据视图；不存在或查询失败返回 <c>null</c>。</returns>
    Task<DeviceCredential> FindByAppKeyAsync(String appKey, CancellationToken cancellationToken = default);
}
