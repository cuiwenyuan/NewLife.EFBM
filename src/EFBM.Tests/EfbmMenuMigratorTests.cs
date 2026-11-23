using EFBM.Web.Services;

namespace EFBM.Tests;

/// <summary>业务菜单迁移器的结构性约定：新 Area 命名空间、URL 前缀与根名称必须与 EfbmArea 一致。</summary>
/// <remarks>
/// 迁移器操作真实 Menu 表，单测不连库；这里守住最容易漂移的几处字符串——
/// 一旦有人改了 Area 名或命名空间却忘了同步迁移器，升级时菜单就会挂错树、角色权限串失效。
/// </remarks>
public class EfbmMenuMigratorTests
{
    [Fact(DisplayName = "菜单迁移_新命名空间对应 Efbm Area")]
    public void NewNamespace_MatchesArea()
        => Assert.EndsWith(".Areas.Efbm.Controllers", EfbmMenuMigrator.NewNamespace);

    [Fact(DisplayName = "菜单迁移_旧命名空间指向历史 Admin Area")]
    public void OldNamespace_IsHistoricAdmin()
        => Assert.EndsWith(".Areas.Admin.Controllers", EfbmMenuMigrator.OldNamespace);

    [Fact(DisplayName = "菜单迁移_根名称与 URL 前缀一致")]
    public void RootName_AndUrlPrefix()
    {
        Assert.Equal("Efbm", EfbmMenuMigrator.RootName);
        Assert.Equal("/Efbm", EfbmMenuMigrator.NewUrlPrefix);
        Assert.Equal(EfbmMenuMigrator.NewUrlPrefix, "/" + EfbmMenuMigrator.RootName);
    }

    [Fact(DisplayName = "菜单迁移_新旧命名空间不同")]
    public void Namespaces_Differ()
        => Assert.NotEqual(EfbmMenuMigrator.OldNamespace, EfbmMenuMigrator.NewNamespace);
}
