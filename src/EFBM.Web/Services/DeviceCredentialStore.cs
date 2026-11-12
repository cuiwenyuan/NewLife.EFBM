using System.Collections.Concurrent;
using EFBM.Core;
using EFBM.Data.Entities;

namespace EFBM.Web.Services;

// 📋 生效: instructions=[newlife-global,xcode,development] | skills=[xcode-data-modeling] | agent=default

/// <summary>按设备发放的凭据读取实现：XCode 实体 + 进程内短 TTL 缓存。</summary>
/// <remarks>
/// 同步接收链路不得因逐件上报而反复查库：命中缓存时完全无数据库 IO；
/// 未命中才走 <c>AppCredential.FindByAppKey</c>（XCode 实体缓存 + 本层 TTL 双重保护）。
/// <b>只读</b>：不更新 <c>LastUsedAt</c>，否则每条上报都会写库，破坏「零 DB 写」约束；
/// 最后使用时间由 P4 后台或 P5 运维统计异步补记。
///
/// 后台发放 / 重置 / 停用凭据时必须调用 <see cref="Invalidate"/>，否则最多滞后一个 TTL。
/// 凭据库不可用时异常向上抛给 <see cref="RequestAuthenticator"/>，由宿主记高优告警。
/// </remarks>
public sealed class DeviceCredentialStore : IDeviceCredentialStore
{
    private readonly EfbmOptions _options;
    private readonly ConcurrentDictionary<String, CacheItem> _cache = new(StringComparer.Ordinal);

    /// <summary>构造凭据存储。</summary>
    /// <param name="options">EFBM 配置节，读取缓存秒数。</param>
    public DeviceCredentialStore(EfbmOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>按 AppKey 查询凭据（Basic 模式下即用户名）。</summary>
    /// <param name="appKey">请求携带的 AppKey。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据视图；不存在返回 <c>null</c>。</returns>
    public Task<DeviceCredential> FindByAppKeyAsync(String appKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (String.IsNullOrWhiteSpace(appKey)) return Task.FromResult<DeviceCredential>(null);

        var seconds = Math.Max(0, _options.CredentialCacheSeconds);
        if (seconds > 0 && _cache.TryGetValue(appKey, out var item) && item.ExpiresAt > DateTime.UtcNow)
            return Task.FromResult(item.Credential);

        var entity = AppCredential.FindByAppKey(appKey);
        var credential = entity == null ? null : Project(entity);

        if (seconds > 0)
        {
            _cache[appKey] = new CacheItem
            {
                Credential = credential,
                ExpiresAt = DateTime.UtcNow.AddSeconds(seconds),
            };
        }

        return Task.FromResult(credential);
    }

    /// <summary>使某个 AppKey 的缓存立即失效；后台发放 / 重置 / 停用凭据时调用。</summary>
    /// <param name="appKey">要失效的 AppKey。</param>
    public void Invalidate(String appKey)
    {
        if (!String.IsNullOrEmpty(appKey)) _cache.TryRemove(appKey, out _);
    }

    /// <summary>清空全部凭据缓存。</summary>
    public void Clear() => _cache.Clear();

    /// <summary>把 XCode 实体投影为接入层只读视图，避免把实体类型泄漏到 Core。</summary>
    /// <param name="entity">凭据实体。</param>
    /// <returns>凭据视图。</returns>
    private static DeviceCredential Project(AppCredential entity) => new()
    {
        AppKey = entity.AppKey,
        AppSecret = entity.AppSecret,
        DeviceId = entity.DeviceId,
        Enabled = entity.Enabled,
    };

    /// <summary>缓存项；<c>null</c> 结果也缓存，避免无效 AppKey 反复穿透到数据库。</summary>
    private sealed class CacheItem
    {
        /// <summary>凭据视图，可为 <c>null</c>。</summary>
        public DeviceCredential Credential { get; set; }

        /// <summary>过期时间 UTC。</summary>
        public DateTime ExpiresAt { get; set; }
    }
}
