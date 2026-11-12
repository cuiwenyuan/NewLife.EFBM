using EFBM.Data.Entities;

namespace EFBM.Tests;

/// <summary>验证 xcode 生成的实体确实包含契约要求的列，且可被业务代码直接引用。</summary>
public class GeneratedEntityTests
{
    [Fact(DisplayName = "生成实体_暴露契约列")]
    public void GeneratedEntities_ExposeContractColumns()
    {
        var raw = new DeviceEventRaw
        {
            EventKey = "abc",
            MessageId = "m1",
            StreamId = "1-0",
            RawJson = "{\"event\":\"ORDER_COMPLETED\"}",
            DataJson = "{}",
            ExtJson = "{\"boxType\":\"A\"}",
            UnknownFields = "boxType",
            ParseVersion = "1.0",
            ParseWarning = "W102",
            RepeatCount = 2,
            HandleStatus = 1,
        };

        Assert.Equal("abc", raw.EventKey);
        Assert.Equal("boxType", raw.UnknownFields);
        Assert.Equal(2, raw.RepeatCount);

        var piece = new ProductionRecord
        {
            EventKey = "abc",
            OrderId = "WO-20260723-001",
            BoxId = "BOX-0001",
            IsPiece = true,
            PlanQty = 1000,
            CompletedQty = 1,
        };
        Assert.True(piece.IsPiece);
        Assert.Equal(1, piece.CompletedQty);

        var credential = new AppCredential { AppKey = "key", AppSecret = "secret", DeviceId = "FBOX-A01" };
        Assert.Equal("FBOX-A01", credential.DeviceId);

        var order = new WorkOrder { OrderId = "WO-20260723-001", PlanQty = 1000, CompletedQty = 1000 };
        Assert.Equal(1000, order.CompletedQty);
    }

    [Fact(DisplayName = "生成实体_绑定EFBM连接名")]
    public void GeneratedEntities_BindToEfbmConnection()
    {
        Assert.Equal("DeviceEventRaw", DeviceEventRaw.Meta.TableName);
        Assert.Equal("ProductionRecord", ProductionRecord.Meta.TableName);
        Assert.Equal("EFBM", DeviceEventRaw.Meta.ConnName);
    }
}
