using System.Runtime.InteropServices;
using EFBM.Core;
using NewLife.Log;

namespace EFBM.Web.Services;

/// <summary>启动自检日志：打印实际生效的 provider、Redis 地址、Stream key、消费组与部署环境。</summary>
/// <remarks>
/// 设备失败不重发，内外网配置混用是高风险场景，因此启动时把关键配置全部输出到日志供人工核对。
/// 本阶段不主动连接 Redis / 数据库：骨架阶段不应因外部依赖未就绪而无法启动；
/// Redis 版本探测、消费组存活与 lag 检查在 P5 的 <c>/health</c> 与监控任务中补齐。
/// </remarks>
public static class StartupDiagnostics
{
    /// <summary>输出启动配置摘要。</summary>
    /// <param name="configuration">应用配置。</param>
    /// <param name="environment">宿主环境。</param>
    /// <param name="options">EFBM 配置节。</param>
    public static void Log(IConfiguration configuration, IHostEnvironment environment, EfbmOptions options)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (environment == null) throw new ArgumentNullException(nameof(environment));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var businessConn = configuration.GetConnectionString(EfbmDefaults.ConnectionName);
        var membershipConn = configuration.GetConnectionString(EfbmDefaults.MembershipConnectionName);

        XTrace.WriteLine("=== EFBM 启动自检 ===");
        XTrace.WriteLine("环境={0} / 部署={1} / 强制HTTPS={2} / 认证={3}", environment.EnvironmentName, options.Deployment, options.RequireHttps, options.AuthMode);
        XTrace.WriteLine("框架={0} / 运行时={1}", RuntimeInformation.FrameworkDescription, Environment.Version);
        XTrace.WriteLine("业务库 {0}: provider={1} / 连接串已配置={2}", EfbmDefaults.ConnectionName, ParseProvider(businessConn), !String.IsNullOrEmpty(businessConn));
        XTrace.WriteLine("权限库 {0}: provider={1} / 连接串已配置={2}", EfbmDefaults.MembershipConnectionName, ParseProvider(membershipConn), !String.IsNullOrEmpty(membershipConn));
        XTrace.WriteLine("Redis={0} / db={1} / 启用={2} / 版本探测={3}",
            options.RedisServer, options.RedisDb, options.RedisEnabled, options.ProbeRedisOnStartup ? "开启" : "关闭（P5 启用）");
        XTrace.WriteLine("Stream={0} / 落库组={1} / 外部组前缀={2}", options.StreamKey, options.PersistGroup, EfbmDefaults.ExternalGroupPrefix);
        XTrace.WriteLine("鉴权Header={0} + {1} / 解析版本={2} / 凭据缓存={3}s / 信任反代={4}",
            options.AppKeyHeader, options.AppSecretHeader, EfbmDefaults.ParseVersion, options.CredentialCacheSeconds, options.TrustForwardedHeaders);
        XTrace.WriteLine("批量={0}条/{1}ms / 本地兜底={2} / 自动裁剪={3} / 安全裁剪={4}",
            options.BatchSize, options.BatchIntervalMs, options.LocalFallbackEnabled, options.AutoTrimEnabled, options.SafeTrimEnabled);
        XTrace.WriteLine("请求体上限={0}B / 兜底回灌={1}s×{2}条", options.MaxBodyBytes, options.FallbackRequeueIntervalSeconds, options.FallbackRequeueBatchSize);
        XTrace.WriteLine("请求日志={0} / 目录={1} / 保留{2}天 / 记录原文={3} / fsync={4}",
            options.RequestLog.Enabled, options.RequestLog.Directory, options.RequestLog.RetentionDays,
            options.RequestLog.IncludeRaw, options.RequestLog.FlushToDisk);
        XTrace.WriteLine("Migration=On（Config/XCode.config）；目标框架 net10.0；端点 POST {0}（P2 已接入）", EfbmDefaults.EventsRoute);

        XTrace.WriteLine("安全基线：HMAC={0}（时间窗{1}s） / IP白名单={2} / 查询密钥={3} / 外网暴露面白名单={4}",
            options.HmacEnabled ? "开启" : "关闭",
            options.HmacWindowSeconds,
            AccessPolicy.ParseList(options.AllowedIps).Length == 0 ? "不限制" : $"{AccessPolicy.ParseList(options.AllowedIps).Length} 条",
            String.IsNullOrWhiteSpace(options.QueryApiKey) ? "未配置（仅后台登录用户可用）" : "已配置",
            options.IsInternet() ? (options.ExposureAllowlistEnabled ? "开启" : "关闭") : "不适用（内网）");

        XTrace.WriteLine("在线用户统计（魔方 UserOnline）：{0}",
            options.UserOnlineMode switch
            {
                0 => "不记录",
                1 => "仅记录已登录用户（设备匿名流量不写库）",
                _ => "记录所有访客（每条上报都会写一次 Membership 库，SQLite 下不建议）",
            });

        if (!options.RedisEnabled)
            XTrace.WriteLine("注意：共享上报流已关闭（EFBM:RedisEnabled=false），数据只进本地兜底与文本日志，queued 恒为 false");

        if (options.Beta?.Enabled == true)
            XTrace.WriteLine("Beta 引导已启用：deviceId={0} / appKey={1} / 幂等补齐设备与凭据（外网禁止）", options.Beta.DeviceId, options.Beta.AppKey);

        if (options.AutoTrimEnabled)
            XTrace.WriteLine("警告：自动裁剪已启用，可能静默删除未确认消息，必须关闭！");
        if (String.IsNullOrEmpty(businessConn))
            XTrace.WriteLine("警告：未配置业务库连接串 {0}", EfbmDefaults.ConnectionName);
    }

    /// <summary>从 XCode 连接串中读取 <c>Provider=</c> 值。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>provider 名称；未配置返回 <c>(none)</c>。</returns>
    private static String ParseProvider(String connectionString)
    {
        if (String.IsNullOrEmpty(connectionString)) return "(none)";

        foreach (var part in connectionString.Split(';'))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            if (!String.Equals(part[..idx].Trim(), "Provider", StringComparison.OrdinalIgnoreCase)) continue;
            return part[(idx + 1)..].Trim();
        }

        return "(未显式指定)";
    }
}
