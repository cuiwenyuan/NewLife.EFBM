using EFBM.Web.Services;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>审计存储的集成测试：真实 SQLite 上写 Cube <c>Log</c> 表并按条件查回来（AUTH-3b）。</summary>
/// <remarks>
/// 这里刻意不走替身：审计最容易错的是「字段映射」——Cube 的 <c>Log</c> 表里 <c>Ex1</c> 是整型、
/// 摘要只能放 <c>Ex4</c>，这种坑只有真表能验证。与其它 SQLite 测试同属 <c>sqlite</c> collection 串行。
/// </remarks>
[Collection("sqlite")]
public sealed class AuditStoreTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-audit");

    private readonly CubeAuditStore _store = new();

    /// <summary>独立 SQLite 文件（Membership 连接）。</summary>
    public AuditStoreTests()
    {
        DAL.AddConnStr("Membership", $"Data Source={_dbFile};Provider=SQLite", null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("Membership").CheckTables();
    }

    [Fact(DisplayName = "审计存储_写入后能按动作与操作人查回")]
    public void WriteThenQuery()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        Write(marker, "清理原始报文", success: true, summary: "{\"deleted\":3}", targetId: 11);
        Write(marker, "重放原始报文", success: true, summary: "{\"messageId\":\"m1\"}", targetId: 12);
        Write(marker, "清理原始报文", success: false, summary: "凭证无效", targetId: null);

        var all = _store.Query(new AuditQuery { Operator = marker });
        Assert.Equal(3, all.Total);
        Assert.Equal(3, all.Items.Count);

        // 按动作过滤
        var cleanups = _store.Query(new AuditQuery { Operator = marker, Action = "清理原始报文" });
        Assert.Equal(2, cleanups.Total);
        Assert.All(cleanups.Items, r => Assert.Equal("清理原始报文", r.Action));

        // 按结果过滤
        var failed = _store.Query(new AuditQuery { Operator = marker, Success = false });
        Assert.Equal(1, failed.Total);
        Assert.Equal("凭证无效", failed.Items[0].Summary);
        Assert.Null(failed.Items[0].TargetId);
    }

    [Fact(DisplayName = "审计存储_分页与总数正确_按时间倒序")]
    public void QueryPaging()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 5; i++) Write(marker, "标记处理状态", success: true, summary: $"# {i}", targetId: i + 1);

        var page = _store.Query(new AuditQuery { Operator = marker, Page = 1, PageSize = 2 });

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.PageSize);
    }

    [Fact(DisplayName = "审计存储_超长摘要被截断_不会撑爆日志表")]
    public void Write_TruncatesLongText()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        Write(marker, "导出原始报文", success: true, summary: new String('x', 5000), targetId: null);

        var page = _store.Query(new AuditQuery { Operator = marker });

        var record = Assert.Single(page.Items);
        Assert.True(record.Summary.Length <= 900);
    }

    [Fact(DisplayName = "审计存储_结果对象可压成一行摘要")]
    public void Summarize_Works()
    {
        Assert.Contains("deleted", CubeAuditStore.Summarize(new { deleted = 2 }));
        Assert.Null(CubeAuditStore.Summarize(null));
    }

    /// <summary>写一条审计；操作人用随机标记，避免与其它用例的数据混在一起。</summary>
    /// <param name="operatorName">操作人（同时作为隔离标记）。</param>
    /// <param name="action">动作。</param>
    /// <param name="user">操作人。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="summary">结果摘要。</param>
    /// <param name="targetId">目标主键。</param>
    private void Write(String operatorName, String action, Boolean success, String summary, Int64? targetId)
        => _store.Write(new AuditRecord
        {
            Action = action,
            TargetId = targetId,
            Success = success,
            Operator = operatorName,
            Ip = "127.0.0.1",
            TraceId = "trace-audit",
            Summary = summary,
            Remark = "{\"from\":\"2026-11-01\",\"to\":\"2026-11-12\"}",
            Time = DateTime.Now,
        });
}
