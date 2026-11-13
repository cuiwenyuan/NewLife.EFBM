
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

    /// <summary>接管其他消费者遗留未确认消息的空闲阈值（秒）；<c>0</c> 表示不接管。默认 300。</summary>
    /// <remarks>
    /// 消费者名是「主机名@进程号」，每次重启都换名字：旧消费者留在 PEL 里的未确认消息
    /// 不会再被任何人读到，既不落库也不进死信。启动时与每轮间隔都会扫描并接管空闲超阈值的消息。
    /// 阈值必须远大于单条处理耗时（毫秒级），否则会把别的实例正在处理的消息抢走（重复处理，幂等可兜但无益）。
    /// </remarks>
    public Int32 PersistClaimIdleSeconds { get; set; } = PelClaimRules.DefaultIdleSeconds;

    /// <summary>接管扫描间隔（秒），默认 300；必须为正。</summary>
    public Int32 PersistClaimIntervalSeconds { get; set; } = 300;

    /// <summary>单轮接管最多扫描的 pending 条数，默认 100。</summary>
    public Int32 PersistClaimBatchSize { get; set; } = PelClaimRules.DefaultBatchSize;

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

    /// <summary>安全裁剪扫描间隔（秒）。默认 300 秒（5 分钟）一轮。</summary>
    public Int32 SafeTrimIntervalSeconds { get; set; } = 300;

    /// <summary>安全裁剪保留下限（天）：绝不删除该天数内的数据，即使已被所有消费组确认（需从 Redis 重放时留缓冲）。</summary>
    public Int32 SafeTrimMinRetentionDays { get; set; } = 7;

    /// <summary>后台导出目录。相对路径以应用基目录（DLL 所在目录）为基准，不是进程工作目录。</summary>
    public String ExportDirectory { get; set; } = "logs/export";

    /// <summary>导出凭证有效期（分钟）：超时后清理操作会被拒绝，必须重新导出（FR-15「先导出再删」）。</summary>
    public Int32 ExportTicketMinutes { get; set; } = 30;

    /// <summary>上报来源 IP 白名单（逗号 / 分号分隔，支持 CIDR 与通配 <c>*</c>）；空表示不限制。</summary>
    /// <remarks>外网部署建议配置；命中判断见 <see cref="AccessPolicy.IsIpAllowed"/>。</remarks>
    public String AllowedIps { get; set; }

    /// <summary>是否要求上报请求带 HMAC-SHA256 签名（<c>X-Timestamp</c> + <c>X-Signature</c>）。</summary>
    /// <remarks>默认关闭：现有设备不带签名。启用后签名缺失或时间窗超限一律 401，属于灰度上线能力。</remarks>
    public Boolean HmacEnabled { get; set; }

    /// <summary>HMAC 允许的时钟偏差（秒），默认 300。</summary>
    public Int32 HmacWindowSeconds { get; set; } = AccessPolicy.DefaultHmacWindowSeconds;

    /// <summary>查询统计 API 的访问密钥；配置后要求请求头 <see cref="QueryApiKeyHeader"/> 一致。</summary>
    /// <remarks>
    /// 未配置时退化为「仅后台登录用户可用」，外部系统调用会得到 503 与明确提示——
    /// 宁可拒绝，也不能把全量生产数据裸露在未鉴权接口上。
    /// </remarks>
    public String QueryApiKey { get; set; }

    /// <summary>查询统计 API 的密钥请求头名。</summary>
    public String QueryApiKeyHeader { get; set; } = EfbmDefaults.QueryApiKeyHeader;

    /// <summary>是否启用暴露面白名单：外网只放行上报、查询、健康检查与后台入口。</summary>
    /// <remarks>
    /// 默认 <c>true</c>；仅外网部署生效（内网联调仍需要 Swagger 等页面）。
    /// 被拦下的路径返回 404 而不是 403，避免暴露「这里曾经有过什么」。
    /// </remarks>
    public Boolean ExposureAllowlistEnabled { get; set; } = true;

    /// <summary>外网是否允许 Redis 指向公网地址；默认 <c>false</c>，启动自检会直接拒绝。</summary>
    public Boolean AllowPublicRedis { get; set; }

    /// <summary>是否在启动时探测 Redis 版本（默认关闭，避免骨架阶段依赖 Redis 在线）。</summary>
    public Boolean ProbeRedisOnStartup { get; set; }

    /// <summary>启动时补齐「运维 / 只读」内置角色的后台菜单授权（FR-14）；现场手工调整过角色可关闭。</summary>
    public Boolean SeedAdminRoles { get; set; } = true;

    /// <summary>启动时显式建表 / 加列（按 <c>Config/XCode.config</c> 的 <c>Migration</c> 策略）。默认 <c>true</c>。</summary>
    /// <remarks>
    /// 为什么必须显式做一次：XCode 的反向工程是<b>惰性</b>的——只有第一次访问某个实体时才检查表结构。
    /// 而兜底回灌服务与落库消费者在启动后几十毫秒内就开始跑 SQL，空库首次部署（或新切到 MySQL 的空库）
    /// 会在建表之前撞上「Table 'efbm.localqueuefallback' doesn't exist」，兜底扫描直接失败。
    /// SQLite 老库因为历史表早已存在，这个问题一直没暴露；换成 MySQL 空库后 100% 复现。
    ///
    /// 建表放在连接注册之后、任何后台服务启动之前，顺序就固定了。库不可达时这里会直接抛异常让启动失败，
    /// 这是刻意的：数据库连不上就应该立刻停下来，而不是让服务在「表都不存在」的状态下假装正常。
    /// 需要自行管理表结构的环境（DBA 统一执行脚本）把它设为 <c>false</c>。
    /// </remarks>
    public Boolean AutoMigrate { get; set; } = true;

    /// <summary>魔方「在线用户」统计档位：<c>0</c> 不记录 / <c>1</c> 仅记录已登录用户 / <c>2</c> 记录所有访客。默认 <c>1</c>。</summary>
    /// <remarks>
    /// 为什么不用框架默认的 <c>2</c>：魔方 <c>RunTimeMiddleware</c> 对<b>每一个</b>请求都会写一次 <c>UserOnline</c>，
    /// 而设备上报是机器流量——不带 Cookie 的匿名请求每次都被当成新会话，于是每条上报都要插入一行。
    /// 这会与审计日志 / 业务落库争同一个库的写锁，SQLite 部署下实测直接抛
    /// 「attempt to write a readonly database」把上报请求打成 500（等于丢数据），MySQL 下也是纯无谓开销。
    /// 上报链路<b>不能</b>为无关的后台统计承担失败风险，因此默认只统计已登录用户（后台运维看得见在线会话，
    /// 设备流量一行不写）；需要完整访客画像时在 MySQL 生产实例上改成 <c>2</c>。
    /// </remarks>
    public Int32 UserOnlineMode { get; set; } = 1;

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
        if (PersistClaimIdleSeconds < 0)
            throw new InvalidOperationException("接管空闲阈值不得为负数（EFBM:PersistClaimIdleSeconds>=0，0 表示不接管）");
        if (PersistClaimIntervalSeconds <= 0 || PersistClaimBatchSize <= 0)
            throw new InvalidOperationException("接管扫描间隔与批量必须为正数（EFBM:PersistClaimIntervalSeconds / PersistClaimBatchSize）");
        if (UserOnlineMode is < 0 or > 2)
            throw new InvalidOperationException("在线用户档位只能是 0（不记录）/ 1（仅已登录）/ 2（所有访客）（EFBM:UserOnlineMode）");

        Beta ??= new BetaOptions();
        Beta.ValidateOrThrow(IsInternet());

        RequestLog ??= new RequestLogOptions();
        RequestLog.ValidateOrThrow();

        if (!IsInternet()) return;

        if (!RequireHttps)
            throw new InvalidOperationException("外网部署必须启用 HTTPS（EFBM:RequireHttps=true）");

        if (String.Equals(AuthMode, "Anonymous", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("外网部署禁止 Anonymous 认证模式");

        if (HmacWindowSeconds <= 0)
            throw new InvalidOperationException("HMAC 时间窗必须为正数（EFBM:HmacWindowSeconds>0）");

        // Redis 是内网基础设施：外网部署把它指到公网，等于把全量消息流交给互联网
        if (RedisEnabled && !AllowPublicRedis && !AccessPolicy.IsPrivateHost(RedisServer))
            throw new InvalidOperationException($"外网部署的 Redis 必须位于内网（当前 EFBM:RedisServer={RedisServer}）；如确需公网请显式设置 AllowPublicRedis=true");

        // 查询接口暴露全量生产数据，弱密钥比不设密钥更危险
        if (!String.IsNullOrWhiteSpace(QueryApiKey) && QueryApiKey.Trim().Length < 16)
            throw new InvalidOperationException("外网部署的查询接口密钥至少 16 位（EFBM:QueryApiKey）");
    }
}
