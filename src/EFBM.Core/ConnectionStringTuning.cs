namespace EFBM.Core;

/// <summary>连接串归一化：补齐驱动必需的、漏了会带来数量级性能差异的参数。</summary>
/// <remarks>
/// 为什么需要这一层：<c>System.Data.SQLite</c> <b>默认不开连接池</b>，每条语句都要重新打开数据库文件。
/// 实测（本机 SQLite 3，Windows）单次「开连接 + 查询」8.37ms，而「复用连接查询」只要 0.03ms，
/// 相差 60 倍；落库消费者一条事件要执行 5~6 条语句，于是吞吐被压到 <b>20 条/秒</b>，
/// 上报能力（500+ 条/秒）与落库能力差一个数量级，Stream 积压只增不减。
///
/// 把 <c>Pooling=True</c> 写进部署配置当然也可以，但配置散落在 appsettings、容器环境变量、
/// 现场手改的备份文件里，任何一个地方漏写都会静默退化成 20 条/秒。所以在这里按驱动类型自动补齐：
/// 只补「没写」的，用户显式写了 <c>Pooling=False</c> 时不覆盖。
///
/// MySQL 连接串无需处理（MySql.Data 默认已启用连接池），原样返回。
/// </remarks>
public static class ConnectionStringTuning
{
    /// <summary>需要补齐的连接池参数。</summary>
    public const String PoolingKey = "Pooling";

    /// <summary>按驱动类型补齐连接串参数。</summary>
    /// <param name="connectionString">原始连接串，允许为空。</param>
    /// <returns>补齐后的连接串；非 SQLite 或未改动时返回原值。</returns>
    public static String Normalize(String connectionString)
    {
        if (String.IsNullOrWhiteSpace(connectionString)) return connectionString;
        if (!IsSqlite(connectionString)) return connectionString;
        if (HasKey(connectionString, PoolingKey)) return connectionString;

        // SQLite 连接串以分号分隔，末尾可能已有分号，避免出现 ";;"
        var trimmed = connectionString.TrimEnd();
        var separator = trimmed.EndsWith(";", StringComparison.Ordinal) ? String.Empty : ";";
        return $"{trimmed}{separator}{PoolingKey}=True";
    }

    /// <summary>判断连接串是否 SQLite。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>SQLite 返回 <c>true</c>。</returns>
    public static Boolean IsSqlite(String connectionString) =>
        !String.IsNullOrWhiteSpace(connectionString)
        && connectionString.Contains("Provider=SQLite", StringComparison.OrdinalIgnoreCase);

    /// <summary>判断连接串里是否已显式写了某个键（大小写不敏感）。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <param name="key">参数名。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public static Boolean HasKey(String connectionString, String key)
    {
        if (String.IsNullOrWhiteSpace(connectionString) || String.IsNullOrWhiteSpace(key)) return false;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            if (String.Equals(part[..index].Trim(), key, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
