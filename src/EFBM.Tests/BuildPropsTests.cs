using System.Xml.Linq;

namespace EFBM.Tests;

/// <summary>工程骨架测试：统一 TFM、4 个工程、XCode 迁移配置，防止模板默认值回潮。</summary>
public class BuildPropsTests
{
    [Fact(DisplayName = "工程骨架_DirectoryBuildProps统一net10.0并关闭可空")]
    public void DirectoryBuildProps_ForcesNet10()
    {
        var text = File.ReadAllText(RepoPaths.Find("Directory.Build.props"));

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", text);
        Assert.Contains("<Nullable>disable</Nullable>", text);
    }

    [Fact(DisplayName = "工程骨架_四个工程均继承统一TFM")]
    public void AllFourProjects_InheritTargetFramework()
    {
        // 官方 xcode 模板默认 netstandard2.0：任何 csproj 再显式声明 TFM 都会覆盖统一值
        var projects = new[]
        {
            "src/EFBM.Core/EFBM.Core.csproj",
            "src/EFBM.Data/EFBM.Data.csproj",
            "src/EFBM.Web/EFBM.Web.csproj",
            "src/EFBM.Tests/EFBM.Tests.csproj",
        };

        foreach (var project in projects)
        {
            var doc = XDocument.Load(RepoPaths.Find(project));
            Assert.DoesNotContain(doc.Descendants("TargetFramework"), e => true);
        }
    }

    [Fact(DisplayName = "工程骨架_解决方案恰含四个工程")]
    public void Solution_ContainsExactlyFourProjects()
    {
        var text = File.ReadAllText(RepoPaths.Find("EFBM.sln"));

        foreach (var name in new[] { "EFBM.Core", "EFBM.Data", "EFBM.Web", "EFBM.Tests" })
            Assert.Contains(name, text);

        Assert.DoesNotContain("EFBM.Queue", text);
        Assert.DoesNotContain("EFBM.Services", text);
        Assert.DoesNotContain("EFBM.Consumer", text);
    }

    [Fact(DisplayName = "工程骨架_XCode配置自动建表加列且关闭SQL日志")]
    public void XCodeConfig_EnablesMigrationAndDisablesSqlLog()
    {
        var doc = XDocument.Load(RepoPaths.Find("src/EFBM.Web/Config/XCode.config"));

        Assert.Equal("On", (String)doc.Root.Element("Migration"));
        Assert.Equal("false", (String)doc.Root.Element("ShowSQL"));
    }
}
