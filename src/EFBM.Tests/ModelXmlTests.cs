using System.Xml.Linq;

namespace EFBM.Tests;

/// <summary>XCode 模型文件的结构测试：保证 10 张实体、连接名、唯一索引与扩展字段不被误删。</summary>
public class ModelXmlTests
{
    private static readonly XNamespace Ns = "https://newlifex.com/Model202509.xsd";
    private static readonly XDocument Doc = XDocument.Load(RepoPaths.Find("src/EFBM.Data/Model.xml"));

    private static XElement Table(String name) =>
        Doc.Descendants(Ns + "Table").First(e => (String)e.Attribute("Name") == name);

    private static String[] Columns(String table) =>
        Table(table).Descendants(Ns + "Column").Select(e => (String)e.Attribute("Name")).ToArray();

    private static String[] UniqueIndexes(String table) =>
        Table(table).Descendants(Ns + "Index")
            .Where(e => String.Equals((String)e.Attribute("Unique"), "True", StringComparison.OrdinalIgnoreCase))
            .Select(e => (String)e.Attribute("Columns")).ToArray();

    [Fact(DisplayName = "模型_恰含十张表")]
    public void Model_HasExactlyTenTables()
    {
        var names = Doc.Descendants(Ns + "Table").Select(e => (String)e.Attribute("Name")).ToArray();

        Assert.Equal(10, names.Length);
        Assert.Equal(new[]
        {
            "Device", "AppCredential", "DeviceEventRaw", "ProductionRecord", "WorkOrder",
            "DeviceStatusLog", "FaultRecord", "DeadLetter", "LocalQueueFallback", "StatusDict",
        }, names);
    }

    [Fact(DisplayName = "模型_选项使用EFBM连接与实体命名空间")]
    public void Option_UsesEfbmConnectionAndEntityNamespace()
    {
        var option = Doc.Root.Element(Ns + "Option");

        Assert.Equal("EFBM", (String)option.Element(Ns + "ConnName"));
        Assert.Equal("EFBM.Data.Entities", (String)option.Element(Ns + "Namespace"));
        Assert.Equal(@".\Entities\", (String)option.Element(Ns + "Output"));
    }

    [Theory(DisplayName = "模型_必需唯一索引_全部声明")]
    [InlineData("Device", "DeviceId")]
    [InlineData("AppCredential", "AppKey")]
    [InlineData("DeviceEventRaw", "EventKey")]
    [InlineData("ProductionRecord", "EventKey")]
    [InlineData("WorkOrder", "OrderId")]
    [InlineData("StatusDict", "StatusCode")]
    public void RequiredUniqueIndexes_AreDeclared(String table, String column)
    {
        Assert.Contains(column, UniqueIndexes(table));
    }

    [Fact(DisplayName = "模型_凭据与设备一对一")]
    public void AppCredential_BindsOneDevice()
    {
        Assert.Contains("DeviceId", UniqueIndexes("AppCredential"));
    }

    [Fact(DisplayName = "模型_原始报文与逐件明细_按时间分片")]
    public void RawAndPieceTables_AreTimeSharded()
    {
        foreach (var table in new[] { "DeviceEventRaw", "ProductionRecord" })
        {
            var id = Table(table).Descendants(Ns + "Column").First(e => (String)e.Attribute("Name") == "Id");
            Assert.Equal("time", (String)id.Attribute("DataScale"));
        }
    }

    [Fact(DisplayName = "模型_原始报文_保留全文与人工处理字段")]
    public void RawTable_KeepsFullTextAndManualHandlingFields()
    {
        var columns = Columns("DeviceEventRaw");

        foreach (var name in new[] { "RawJson", "DataJson", "ExtJson", "UnknownFields", "ParseVersion", "ParseWarning", "StreamId", "RepeatCount", "HandleStatus", "HandleRemark", "HandleBy", "HandleAt" })
            Assert.Contains(name, columns);
    }

    [Fact(DisplayName = "模型_聚合表_带ExtJson与ParseVersion")]
    public void AggregateTables_CarryExtJsonAndParseVersion()
    {
        foreach (var table in new[] { "ProductionRecord", "WorkOrder", "DeviceStatusLog", "FaultRecord" })
        {
            var columns = Columns(table);
            Assert.Contains("ExtJson", columns);
            Assert.Contains("ParseVersion", columns);
        }
    }
}
