namespace EFBM.Core;

/// <summary>EFBM 全局默认值与契约常量。</summary>
/// <remarks>
/// 所有可配置项在此登记默认值，实际值由 <c>appsettings.json</c> 的 <c>EFBM</c> 配置节覆盖。
/// 这里的常量不得散落到业务代码中重复硬编码；改动契约需同步《需求说明.md》§3 与《Doc/上报字段字典.md》。
/// </remarks>
public static class EfbmDefaults
{
    /// <summary>业务库连接名。连接串 provider 决定 SQLite / MySQL，切换零代码。</summary>
    public const String ConnectionName = "EFBM";

    /// <summary>Cube 权限库连接名，与业务库同实例分库。</summary>
    public const String MembershipConnectionName = "Membership";

    /// <summary>唯一上报端点路径，不带版本号。</summary>
    public const String EventsRoute = "api/events";

    /// <summary>共享上报流的 Stream key。</summary>
    public const String StreamKey = "efbm:events";

    /// <summary>本系统落库消费组，启动时必须最先创建。</summary>
    public const String PersistGroup = "efbm-persist";

    /// <summary>外部系统消费组名前缀，完整名称为 <c>efbm-&lt;系统名&gt;</c>。</summary>
    public const String ExternalGroupPrefix = "efbm-";

    /// <summary>AppKey 默认请求头名，可按部署配置修改。</summary>
    public const String AppKeyHeader = "X-App-Key";

    /// <summary>AppSecret 默认请求头名，可按部署配置修改。</summary>
    public const String AppSecretHeader = "X-App-Secret";

    /// <summary>当前字段字典 / 解析器版本，见《Doc/上报字段字典.md》。</summary>
    public const String ParseVersion = "1.0";

    /// <summary>消费者默认攒批条数。</summary>
    public const Int32 BatchSize = 200;

    /// <summary>消费者默认攒批间隔，毫秒。</summary>
    public const Int32 BatchIntervalMs = 200;

    /// <summary>Redis 最低版本要求（XTRIM MINID / XAUTOCLAIM 依赖）。</summary>
    public const String MinRedisVersion = "6.2";

    /// <summary>凭据进程内缓存秒数；稳态逐件上报不查库，后台重置凭据时需主动失效缓存。</summary>
    public const Int32 CredentialCacheSeconds = 60;

    /// <summary>本地兜底回灌扫描间隔（秒）。</summary>
    public const Int32 FallbackRequeueIntervalSeconds = 30;

    /// <summary>单次回灌最多处理的兜底记录数。</summary>
    public const Int32 FallbackRequeueBatchSize = 100;

    /// <summary>单次上报请求体上限（字节）；仅防异常大包，正常报文远小于该值。</summary>
    public const Int32 MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>请求日志默认目录（相对宿主内容根目录）。</summary>
    public const String RequestLogDirectory = "logs/api";

    /// <summary>请求日志默认文件名前缀。</summary>
    public const String RequestLogPrefix = "api";

    /// <summary>请求日志单文件上限（MB）。</summary>
    public const Int32 RequestLogMaxFileMB = 64;

    /// <summary>请求日志默认保留天数；0 表示不清理。</summary>
    public const Int32 RequestLogRetentionDays = 90;

    /// <summary>「尚未回灌」哨兵时间。</summary>
    /// <remarks>
    /// 不能用 <c>DateTime.MinValue</c>：MySQL 的 DATETIME 最小值为 1000-01-01，写入零值会失败，
    /// 而 NULL 又无法用普通比较条件查询。回灌成功后改写为 <c>DateTime.UtcNow</c>。
    /// </remarks>
    public static readonly DateTime UnrequeuedAt = new(1970, 1, 1);
}
