namespace EFBM.Core;

/// <summary><c>appsettings.json</c> 中 <c>EFBM</c> 配置节的强类型视图。</summary>
/// <remarks>
/// 内外网使用两套配置、不改代码。外网基线（强制 HTTPS、禁用业务匿名认证）在启动时自检，
/// 配置不符直接启动失败（需求说明 §7、开发计划 §4）。
/// </remarks>
public class EfbmOptions
{
    /// <summary>配置节名称。</summary>
    public const String SectionName = "EFBM";

    /// <summary>部署环境：<c>Intranet</c>（内网）或 <c>Internet</c>（外网）。</summary>
    public String Deployment { get; set; } = "Intranet";

    /// <summary>是否强制 HTTPS；外网部署必须为 <c>true</c>。</summary>
    public Boolean RequireHttps { get; set; }

    /// <summary>认证模式：<c>Anonymous</c> / <c>Basic</c> / <c>AppKeyAppSecret</c>。</summary>
    public String AuthMode { get; set; } = "AppKeyAppSecret";

    /// <summary>AppKey 请求头名，可配置修改。</summary>
    public String AppKeyHeader { get; set; } = EfbmDefaults.AppKeyHeader;

    /// <summary>AppSecret 请求头名，可配置修改。</summary>
    public String AppSecretHeader { get; set; } = EfbmDefaults.AppSecretHeader;

    /// <summary>共享上报流 key。</summary>
    public String StreamKey { get; set; } = EfbmDefaults.StreamKey;

    /// <summary>本系统落库消费组名。</summary>
    public String PersistGroup { get; set; } = EfbmDefaults.PersistGroup;

    /// <summary>Redis 地址（主从 / 哨兵由部署环境提供）。</summary>
    public String RedisServer { get; set; } = "127.0.0.1:6379";

    /// <summary>是否接入共享上报流。Beta 阶段可关闭，只用文本日志与本地兜底收数据。</summary>
    /// <remarks>
    /// 关闭后 <see cref="IEventQueue"/> 不再尝试连接 Redis（避免每条请求都等超时），
    /// 接收链路直接走本地兜底 / 文本日志，响应里 <c>queued=false</c>。
    /// </remarks>
    public Boolean RedisEnabled { get; set; } = true;

    /// <summary>Redis 密码；生产环境用环境变量或密钥管理覆盖，勿明文入库。</summary>
    public String RedisPassword { get; set; }

    /// <summary>Redis 库编号。</summary>
    public Int32 RedisDb { get; set; }

    /// <summary>消费者攒批条数。</summary>
    public Int32 BatchSize { get; set; } = EfbmDefaults.BatchSize;

    /// <summary>消费者攒批间隔，毫秒。</summary>
    public Int32 BatchIntervalMs { get; set; } = EfbmDefaults.BatchIntervalMs;

    /// <summary>本地兜底队列开关；Redis 不可用时写本地暂存并仍返回 200。</summary>
    public Boolean LocalFallbackEnabled { get; set; } = true;

    /// <summary>本地兜底回灌扫描间隔（秒）。</summary>
    public Int32 FallbackRequeueIntervalSeconds { get; set; } = EfbmDefaults.FallbackRequeueIntervalSeconds;

    /// <summary>单次回灌最多处理的兜底记录数。</summary>
    public Int32 FallbackRequeueBatchSize { get; set; } = EfbmDefaults.FallbackRequeueBatchSize;

    /// <summary>设备凭据进程内缓存秒数；0 表示不缓存（每次查库）。</summary>
    public Int32 CredentialCacheSeconds { get; set; } = EfbmDefaults.CredentialCacheSeconds;

    /// <summary>消费者单条消息的进程内重试上限；超过后先写死信表再确认（FR-12，绝不自动丢弃）。</summary>
    public Int32 ConsumerMaxRetry { get; set; } = EfbmDefaults.ConsumerMaxRetry;

    /// <summary>首次上报自动登记设备（FR-13）；关闭后未登记设备的事件仍正常落库，只是不建 Device 记录。</summary>
    public Boolean DeviceAutoRegister { get; set; } = true;

    /// <summary>是否信任反向代理的 <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>。</summary>
    public Boolean TrustForwardedHeaders { get; set; } = true;

    /// <summary>单次上报请求体上限（字节）。</summary>
    public Int32 MaxBodyBytes { get; set; } = EfbmDefaults.MaxBodyBytes;

    /// <summary>是否启用框架自动裁剪；<b>必须保持 false</b>，否则会静默删除未确认消息。</summary>
    public Boolean AutoTrimEnabled { get; set; }

    /// <summary>按所有消费组最小已确认 ID 的安全裁剪开关。</summary>
    public Boolean SafeTrimEnabled { get; set; } = true;

    /// <summary>是否在启动时探测 Redis 版本（默认关闭，避免骨架阶段依赖 Redis 在线）。</summary>
    public Boolean ProbeRedisOnStartup { get; set; }

    /// <summary>Beta 引导配置：后台发放页面（P4）上线前的临时凭据来源，默认关闭。</summary>
    public BetaOptions Beta { get; set; } = new();

    /// <summary>请求日志配置：Beta 阶段每条上报的文本留痕，是队列不可用时的最后一道兜底。</summary>
    public RequestLogOptions RequestLog { get; set; } = new();

    /// <summary>是否为外网部署。</summary>
    /// <returns>外网返回 <c>true</c>。</returns>
    public Boolean IsInternet() => String.Equals(Deployment, "Internet", StringComparison.OrdinalIgnoreCase);

    /// <summary>启动自检：外网必须 HTTPS 且不得使用业务匿名认证。</summary>
    /// <exception cref="InvalidOperationException">外网配置不符合安全基线时抛出。</exception>
    public void ValidateOrThrow()
    {
        // 任何环境都不允许自动裁剪：框架按长度 / 时间裁剪不判断消费组是否已消费
        if (AutoTrimEnabled)
            throw new InvalidOperationException("自动裁剪会静默删除未确认消息，禁止启用（EFBM:AutoTrimEnabled=false）");

        // 认证模式拼错会让所有上报被 401 且设备不重发，属于上线级事故，启动时必须拦住
        if (!AuthModes.IsKnown(AuthMode))
            throw new InvalidOperationException($"认证模式非法（EFBM:AuthMode={AuthMode}），仅支持 {String.Join(" / ", AuthModes.All)}");

        if (MaxBodyBytes <= 0)
            throw new InvalidOperationException("请求体上限必须为正数（EFBM:MaxBodyBytes>0）");
        if (FallbackRequeueIntervalSeconds <= 0 || FallbackRequeueBatchSize <= 0)
            throw new InvalidOperationException("兜底回灌的间隔与批量必须为正数");
        if (CredentialCacheSeconds < 0)
            throw new InvalidOperationException("凭据缓存秒数不得为负数");
        if (ConsumerMaxRetry < 1)
            throw new InvalidOperationException("消费重试上限至少为 1（EFBM:ConsumerMaxRetry>=1）");
        if (BatchSize <= 0 || BatchIntervalMs <= 0)
            throw new InvalidOperationException("消费者攒批条数与间隔必须为正数");

        Beta ??= new BetaOptions();
        Beta.ValidateOrThrow(IsInternet());

        RequestLog ??= new RequestLogOptions();
        RequestLog.ValidateOrThrow();

        if (!IsInternet()) return;

        if (!RequireHttps)
            throw new InvalidOperationException("外网部署必须启用 HTTPS（EFBM:RequireHttps=true）");

        if (String.Equals(AuthMode, "Anonymous", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("外网部署禁止 Anonymous 认证模式");
    }
}
