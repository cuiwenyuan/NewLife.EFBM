using EFBM.Core;
using EFBM.Data.Entities;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

// 📋 生效: instructions=[newlife-global,xcode,testing-strategy] | skills=[xcode-data-modeling,testing-strategy] | agent=default

/// <summary>SQLite 提供者可用性验证：Beta 用单机 SQLite 存放设备凭据与兜底记录。</summary>
/// <remarks>
/// 这些用例会真实创建 SQLite 文件并让 XCode 自动建表，因此能证明
/// ① <c>XCode.SQLite</c> 原生库在本机可加载；② <c>Migration=On</c> 能自动建表；
/// ③ 生成的实体在本机可读写。<b>不验证</b> MySQL 方言，双库矩阵仍按开发计划 §7 在目标环境补跑。
/// </remarks>
public sealed class SqliteSmokeTests : IDisposable
{
    private readonly String _dbFile;

    /// <summary>为本次测试准备独立的 SQLite 文件，避免污染开发库。</summary>
    public SqliteSmokeTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"efbm-smoke-{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbFile};Provider=SQLite";

        // 实体固定使用 ConnName=EFBM；测试库用独立文件覆盖同名连接
        DAL.AddConnStr("EFBM", connStr, null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;
    }

    /// <summary>首次访问实体时自动建表，随后可插入并按 AppKey 查回。</summary>
    [Fact(DisplayName = "SQLite_自动建表并可读写凭据")]
    public void Credential_CanBeCreatedAndFound()
    {
        var entity = new AppCredential
        {
            AppKey = "beta-key",
            AppSecret = "beta-secret",
            DeviceId = "FBOX-A01",
            Enabled = true,
            Remark = "beta smoke",
        };
        Assert.Equal(1, entity.Insert());

        var found = AppCredential.FindByAppKey("beta-key");
        Assert.NotNull(found);
        Assert.Equal("beta-secret", found.AppSecret);
        Assert.Equal("FBOX-A01", found.DeviceId);
        Assert.True(found.Enabled);

        // 同名字段查询走生成的 FindByDeviceId，验证唯一索引与查询表达式可用
        Assert.Equal(found.Id, AppCredential.FindByDeviceId("FBOX-A01")?.Id);
    }

    /// <summary>兜底表同样能自动建表并写入，Redis 故障降级路径才有落点。</summary>
    [Fact(DisplayName = "SQLite_兜底表可写入并查询未回灌记录")]
    public void LocalQueueFallback_CanBeWritten()
    {
        var entity = new LocalQueueFallback
        {
            MessageId = Guid.NewGuid().ToString("N"),
            EventKey = new String('a', 40),
            RawJson = "{\"eventKey\":\"a\"}",
            RequeuedAt = EfbmDefaults.UnrequeuedAt,
            RetryCount = 0,
            CreateTime = DateTime.UtcNow,
        };
        Assert.Equal(1, entity.Insert());

        var count = LocalQueueFallback.FindCount(
            LocalQueueFallback._.RequeuedAt == EfbmDefaults.UnrequeuedAt, null, null, 0, 0);
        Assert.True(count >= 1);
    }

    /// <summary>关闭连接池并尽力删除临时库文件。</summary>
    public void Dispose()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }
        catch (IOException)
        {
            // 连接池未及时释放时保留临时文件，不影响测试结论
        }
    }
}
