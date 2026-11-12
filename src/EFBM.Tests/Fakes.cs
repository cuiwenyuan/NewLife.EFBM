using EFBM.Core;

namespace EFBM.Tests;

/// <summary>接收链路测试替身：队列、凭据、本地兜底三个抽象的最小实现。</summary>
/// <remarks>
/// 这些接口是架构解耦的产物（Core 不依赖 Web / DB），不是为测试临时包装的虚方法；
/// 用替身覆盖同步链路的分支，Redis / MySQL 运行时行为留给 P6 的真实环境集成测试。
/// </remarks>
internal sealed class FakeEventQueue : IEventQueue
{
    public List<StreamMessage> Added { get; } = [];
    public Boolean ThrowOnAdd { get; set; }
    public String LastAckedId { get; private set; }

    public Task<String> AddAsync(StreamMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd) throw new InvalidOperationException("redis down");
        Added.Add(message);
        return Task.FromResult($"{Added.Count}-0");
    }

    public Task<IReadOnlyList<QueueMessage>> TakeAsync(String group, String consumer, Int32 count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QueueMessage>>(Array.Empty<QueueMessage>());

    public Task AckAsync(String group, String streamId, CancellationToken cancellationToken = default)
    {
        LastAckedId = streamId;
        return Task.CompletedTask;
    }
}

/// <summary>凭据存储替身，记录查询次数用于验证匿名模式零查库。</summary>
internal sealed class FakeCredentialStore : IDeviceCredentialStore
{
    public Dictionary<String, DeviceCredential> Items { get; } = new(StringComparer.Ordinal);
    public Boolean ThrowOnFind { get; set; }
    public Int32 CallCount { get; private set; }

    public Task<DeviceCredential> FindByAppKeyAsync(String appKey, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (ThrowOnFind) throw new InvalidOperationException("credential db down");
        Items.TryGetValue(appKey ?? String.Empty, out var credential);
        return Task.FromResult(credential);
    }

    /// <summary>构造一条测试用凭据。</summary>
    /// <param name="deviceId">绑定设备。</param>
    /// <param name="appKey">AppKey。</param>
    /// <param name="appSecret">AppSecret。</param>
    /// <param name="enabled">是否启用。</param>
    /// <returns>凭据视图。</returns>
    public static DeviceCredential Valid(String deviceId = "FBOX-A01", String appKey = "key-1", String appSecret = "secret-1", Boolean enabled = true) =>
        new() { AppKey = appKey, AppSecret = appSecret, DeviceId = deviceId, Enabled = enabled };

    /// <summary>注册一条凭据并返回自身，便于链式构造。</summary>
    /// <param name="credential">凭据视图。</param>
    /// <returns>当前存储实例。</returns>
    public FakeCredentialStore With(DeviceCredential credential)
    {
        Items[credential.AppKey] = credential;
        return this;
    }
}

/// <summary>请求日志替身：记录条目并可控地模拟写盘失败。</summary>
internal sealed class FakeRequestLog : IRequestLog
{
    public List<RequestLogEntry> Entries { get; } = [];
    public Boolean ThrowOnWrite { get; set; }
    public Boolean Enabled { get; set; } = true;

    public Task<Boolean> WriteAsync(RequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return Task.FromResult(false);
        if (ThrowOnWrite) throw new IOException("disk full");

        Entries.Add(entry);
        return Task.FromResult(true);
    }
}

/// <summary>本地兜底替身，验证正常路径零写入与降级路径写入一次。</summary>
internal sealed class FakeFallbackStore : ILocalFallbackStore
{
    public List<StreamMessage> Saved { get; } = [];
    public Boolean ThrowOnSave { get; set; }
    public List<FallbackEntry> Pending { get; } = [];
    public List<Int64> Requeued { get; } = [];
    public List<Int64> Retried { get; } = [];

    public Task SaveAsync(StreamMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave) throw new InvalidOperationException("fallback db down");
        Saved.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FallbackEntry>> TakePendingAsync(Int32 count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FallbackEntry>>(Pending.Take(count).ToList());

    public Task MarkRequeuedAsync(Int64 id, CancellationToken cancellationToken = default)
    {
        Requeued.Add(id);
        return Task.CompletedTask;
    }

    public Task MarkRetryAsync(Int64 id, String error, CancellationToken cancellationToken = default)
    {
        Retried.Add(id);
        return Task.CompletedTask;
    }
}
