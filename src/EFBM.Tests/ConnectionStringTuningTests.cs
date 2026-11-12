using EFBM.Core;

namespace EFBM.Tests;

/// <summary>连接串归一化测试：补齐 <c>Pooling=True</c>，只补「没写」的。</summary>
/// <remarks>
/// 为什么值得单独测：SQLite 默认不开连接池，实测单次开连接 8.37ms、复用连接 0.03ms。
/// 一条事件要执行 5~6 条语句，漏了这个参数落库吞吐就从 600+ 条/秒掉到 20 条/秒，
/// 而配置分散在 appsettings / 环境变量 / 现场备份里，任何一个漏写都会静默退化。
/// </remarks>
public class ConnectionStringTuningTests
{
    [Fact(DisplayName = "连接串_SQLite_自动补齐连接池")]
    public void Sqlite_AppendsPooling()
    {
        var normalized = ConnectionStringTuning.Normalize("Data Source=EFBM.db;Provider=SQLite");

        Assert.Equal("Data Source=EFBM.db;Provider=SQLite;Pooling=True", normalized);
    }

    [Fact(DisplayName = "连接串_SQLite_末尾已有分号不重复")]
    public void Sqlite_TrailingSemicolon()
    {
        var normalized = ConnectionStringTuning.Normalize("Data Source=EFBM.db;Provider=SQLite;");

        Assert.Equal("Data Source=EFBM.db;Provider=SQLite;Pooling=True", normalized);
    }

    [Theory(DisplayName = "连接串_已显式配置连接池_不覆盖")]
    [InlineData("Data Source=EFBM.db;Provider=SQLite;Pooling=False", "Pooling=False")]
    [InlineData("Data Source=EFBM.db;Provider=SQLite;pooling=true", "pooling=true")]
    public void Sqlite_ExplicitPoolingKept(String original, String expectedPart)
    {
        var normalized = ConnectionStringTuning.Normalize(original);

        Assert.Equal(original, normalized);
        Assert.Contains(expectedPart, normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Pooling=True;", normalized, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "连接串_MySQL_原样返回")]
    public void MySql_Untouched()
    {
        const String cs = "Server=127.0.0.1;Port=3306;Database=EFBM;Uid=efbm;Pwd=x;Provider=MySql";

        Assert.Equal(cs, ConnectionStringTuning.Normalize(cs));
    }

    [Theory(DisplayName = "连接串_空值_原样返回")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Untouched(String cs)
    {
        Assert.Equal(cs, ConnectionStringTuning.Normalize(cs));
    }

    [Fact(DisplayName = "连接串_键名大小写不敏感")]
    public void HasKey_IgnoreCase()
    {
        Assert.True(ConnectionStringTuning.HasKey("Data Source=a.db;PROVIDER=SQLite;POOLING=True", "Pooling"));
        Assert.False(ConnectionStringTuning.HasKey("Data Source=a.db;Provider=SQLite", "Pooling"));
        Assert.False(ConnectionStringTuning.HasKey("Data Source=a.db;PoolingExtra=True", "Pooling"));
    }

    [Fact(DisplayName = "连接串_识别SQLite驱动")]
    public void IsSqlite_ByProvider()
    {
        Assert.True(ConnectionStringTuning.IsSqlite("Data Source=a.db;provider=sqlite"));
        Assert.False(ConnectionStringTuning.IsSqlite("Server=127.0.0.1;Provider=MySql"));
    }
}
