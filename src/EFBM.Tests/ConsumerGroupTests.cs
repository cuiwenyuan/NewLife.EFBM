using EFBM.Core;
using EFBM.Data.Entities;
using XCode;
using XCode.DataAccessLayer;

namespace EFBM.Tests;

/// <summary>消费组命名规则（纯函数）与登记实体的校验 / 回写 / 禁删（STR-2a、WEB-9b）。</summary>
[Collection("sqlite")]
public sealed class ConsumerGroupTests
{
    private static readonly String _dbFile = TempSqlite.CreatePath("efbm-consumer-group");

    /// <summary>独立 SQLite 文件；显式建表绕开 XCode 的进程级表结构缓存。</summary>
    public ConsumerGroupTests()
    {
        DAL.AddConnStr("EFBM", ConnectionStringTuning.Normalize($"Data Source={_dbFile};Provider=SQLite"), null, "SQLite");
        XCodeSetting.Current.Migration = Migration.On;
        XCodeSetting.Current.ShowSQL = false;

        DAL.Create("EFBM").CheckTables();
    }

    [Theory(DisplayName = "组名规则_合法格式")]
    [InlineData("efbm-mes")]
    [InlineData("efbm-wms")]
    [InlineData("efbm-mes-2")]
    [InlineData("efbm-a_b-c")]
    [InlineData("efbm-a2")]
    public void Name_Valid(String name)
    {
        Assert.Null(ConsumerGroupRules.Validate(name));
        Assert.True(ConsumerGroupRules.IsValidName(name));
    }

    [Theory(DisplayName = "组名规则_非法格式给出人话")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mes")]
    [InlineData("efbm-")]
    [InlineData("efbm-MES")]
    [InlineData("efbm--mes")]
    [InlineData("efbm-mes space")]
    [InlineData("efbm-中文")]
    public void Name_Invalid(String name)
    {
        Assert.NotNull(ConsumerGroupRules.Validate(name));
        Assert.False(ConsumerGroupRules.IsValidName(name));
    }

    [Fact(DisplayName = "组名规则_超长拒绝")]
    public void Name_TooLong()
    {
        var name = "efbm-" + new String('a', ConsumerGroupRules.MaxNameLength);
        Assert.NotNull(ConsumerGroupRules.Validate(name));
    }

    [Fact(DisplayName = "组名规则_系统名推导与内部组识别")]
    public void Name_SystemName()
    {
        Assert.Equal("mes", ConsumerGroupRules.GetSystemName("efbm-mes"));
        Assert.Equal(String.Empty, ConsumerGroupRules.GetSystemName("mes"));

        Assert.True(ConsumerGroupRules.IsInternal(EfbmDefaults.PersistGroup));
        Assert.False(ConsumerGroupRules.IsInternal("efbm-mes"));
    }

    [Fact(DisplayName = "登记_归属系统名自动推导")]
    public void Entity_FillsSystemName()
    {
        var name = "efbm-auto-" + Guid.NewGuid().ToString("N")[..6];

        var entity = new ConsumerGroup { Name = name, SystemName = "手填错的值", Enabled = true };
        entity.Insert();

        Assert.Equal(name["efbm-".Length..], entity.SystemName);
        Assert.StartsWith("auto-", entity.SystemName);
    }

    [Fact(DisplayName = "登记_非法组名拒绝入库")]
    public void Entity_RejectsBadName()
    {
        var entity = new ConsumerGroup { Name = "MES", Enabled = true };

        var ex = Assert.Throws<InvalidOperationException>(() => entity.Insert());
        Assert.Contains("efbm-", ex.Message);
    }

    [Fact(DisplayName = "登记_内部落库组不需要登记")]
    public void Entity_RejectsInternal()
    {
        var entity = new ConsumerGroup { Name = EfbmDefaults.PersistGroup, Enabled = true };

        var ex = Assert.Throws<InvalidOperationException>(() => entity.Insert());
        Assert.Contains("内部组", ex.Message);
    }

    [Fact(DisplayName = "登记_组名唯一")]
    public void Entity_NameUnique()
    {
        var name = "efbm-dup-" + Guid.NewGuid().ToString("N")[..6];
        new ConsumerGroup { Name = name, Enabled = true }.Insert();

        var second = new ConsumerGroup { Name = name, Enabled = true };
        Assert.ThrowsAny<Exception>(() => second.Insert());
    }

    [Fact(DisplayName = "登记_不允许删除")]
    public void Entity_DeleteBlocked()
    {
        var name = "efbm-del-" + Guid.NewGuid().ToString("N")[..6];
        var entity = new ConsumerGroup { Name = name, Enabled = true };
        entity.Insert();

        var ex = Assert.Throws<InvalidOperationException>(() => entity.Delete());
        Assert.Contains("不允许删除", ex.Message);

        Assert.NotNull(ConsumerGroup.FindByName(name));
    }

    [Fact(DisplayName = "回写最后出现时间_只更新已登记项且返回未登记组")]
    public void TouchSeen_UpdatesRegisteredOnly()
    {
        var registered = "efbm-seen-" + Guid.NewGuid().ToString("N")[..6];
        var unknown = "efbm-ghost-" + Guid.NewGuid().ToString("N")[..6];

        var entity = new ConsumerGroup { Name = registered, Enabled = true };
        entity.Insert();

        var missing = ConsumerGroup.TouchSeen([registered, unknown, EfbmDefaults.PersistGroup]);

        Assert.Equal([unknown], missing);

        var saved = ConsumerGroup.FindByName(registered);
        Assert.NotNull(saved);
        Assert.True(saved.LastSeen > DateTime.UtcNow.AddMinutes(-1), "已登记组的最后出现时间应被回写");

        // 未登记的组不会被自动建记录：登记必须有责任人或用途说明
        Assert.Null(ConsumerGroup.FindByName(unknown));
    }

    [Fact(DisplayName = "回写最后出现时间_一分钟内不重复写")]
    public void TouchSeen_Throttled()
    {
        var name = "efbm-throttle-" + Guid.NewGuid().ToString("N")[..6];
        var entity = new ConsumerGroup { Name = name, Enabled = true, LastSeen = DateTime.UtcNow.AddMinutes(-5) };
        entity.Insert();

        ConsumerGroup.TouchSeen([name]);
        var first = ConsumerGroup.FindByName(name).LastSeen;

        ConsumerGroup.TouchSeen([name]);
        var second = ConsumerGroup.FindByName(name).LastSeen;

        Assert.Equal(first, second);
    }
}
