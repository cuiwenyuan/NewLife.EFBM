using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using NewLife;
using NewLife.Data;
using XCode;
using XCode.Cache;
using XCode.Configuration;
using XCode.DataAccessLayer;

namespace EFBM.Data.Entities;

/// <summary>逐件完成明细</summary>
[Serializable]
[DataObject]
[Description("逐件完成明细")]
[BindIndex("IU_ProductionRecord_EventKey", true, "EventKey")]
[BindIndex("IX_ProductionRecord_OrderId_EventTime", false, "OrderId,EventTime")]
[BindIndex("IX_ProductionRecord_DeviceId_EventTime", false, "DeviceId,EventTime")]
[BindTable("ProductionRecord", Description = "逐件完成明细", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class ProductionRecord
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号。时间缩放，便于按月分表</summary>
    [DisplayName("编号")]
    [Description("编号。时间缩放，便于按月分表")]
    [DataObjectField(true, false, false, 0)]
    [BindColumn("Id", "编号。时间缩放，便于按月分表", "", DataScale = "time")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _EventKey;
    /// <summary>幂等键。分片内唯一</summary>
    [DisplayName("幂等键")]
    [Description("幂等键。分片内唯一")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("EventKey", "幂等键。分片内唯一", "", Master = true)]
    public String EventKey { get => _EventKey; set { if (OnPropertyChanging("EventKey", value)) { _EventKey = value; OnPropertyChanged("EventKey"); } } }

    private String _MessageId;
    /// <summary>消息追踪 ID</summary>
    [DisplayName("消息追踪ID")]
    [Description("消息追踪 ID")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("MessageId", "消息追踪 ID", "")]
    public String MessageId { get => _MessageId; set { if (OnPropertyChanging("MessageId", value)) { _MessageId = value; OnPropertyChanged("MessageId"); } } }

    private String _OrderId;
    /// <summary>工单号</summary>
    [DisplayName("工单号")]
    [Description("工单号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("OrderId", "工单号", "")]
    public String OrderId { get => _OrderId; set { if (OnPropertyChanging("OrderId", value)) { _OrderId = value; OnPropertyChanged("OrderId"); } } }

    private String _BoxId;
    /// <summary>盒型或盒号原值，不推断语义</summary>
    [DisplayName("盒型或盒号原值")]
    [Description("盒型或盒号原值，不推断语义")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("BoxId", "盒型或盒号原值，不推断语义", "")]
    public String BoxId { get => _BoxId; set { if (OnPropertyChanging("BoxId", value)) { _BoxId = value; OnPropertyChanged("BoxId"); } } }

    private String _DeviceId;
    /// <summary>设备编号</summary>
    [DisplayName("设备编号")]
    [Description("设备编号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private Boolean _IsPiece;
    /// <summary>是否逐件完成事件</summary>
    [DisplayName("是否逐件完成事件")]
    [Description("是否逐件完成事件")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("IsPiece", "是否逐件完成事件", "")]
    public Boolean IsPiece { get => _IsPiece; set { if (OnPropertyChanging("IsPiece", value)) { _IsPiece = value; OnPropertyChanged("IsPiece"); } } }

    private Int32 _PlanQty;
    /// <summary>计划数量</summary>
    [DisplayName("计划数量")]
    [Description("计划数量")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("PlanQty", "计划数量", "")]
    public Int32 PlanQty { get => _PlanQty; set { if (OnPropertyChanging("PlanQty", value)) { _PlanQty = value; OnPropertyChanged("PlanQty"); } } }

    private Int32 _CompletedQty;
    /// <summary>累计完成数。按取大收敛</summary>
    [DisplayName("累计完成数")]
    [Description("累计完成数。按取大收敛")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("CompletedQty", "累计完成数。按取大收敛", "")]
    public Int32 CompletedQty { get => _CompletedQty; set { if (OnPropertyChanging("CompletedQty", value)) { _CompletedQty = value; OnPropertyChanged("CompletedQty"); } } }

    private DateTime _EventTime;
    /// <summary>设备侧事件时间 UTC</summary>
    [DisplayName("设备侧事件时间UTC")]
    [Description("设备侧事件时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("EventTime", "设备侧事件时间 UTC", "")]
    public DateTime EventTime { get => _EventTime; set { if (OnPropertyChanging("EventTime", value)) { _EventTime = value; OnPropertyChanged("EventTime"); } } }

    private String _ExtJson;
    /// <summary>扩展字段 JSON</summary>
    [DisplayName("扩展字段JSON")]
    [Description("扩展字段 JSON")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("ExtJson", "扩展字段 JSON", "")]
    public String ExtJson { get => _ExtJson; set { if (OnPropertyChanging("ExtJson", value)) { _ExtJson = value; OnPropertyChanged("ExtJson"); } } }

    private String _ParseVersion;
    /// <summary>解析版本</summary>
    [DisplayName("解析版本")]
    [Description("解析版本")]
    [DataObjectField(false, false, true, 20)]
    [BindColumn("ParseVersion", "解析版本", "")]
    public String ParseVersion { get => _ParseVersion; set { if (OnPropertyChanging("ParseVersion", value)) { _ParseVersion = value; OnPropertyChanged("ParseVersion"); } } }

    private DateTime _CreateTime;
    /// <summary>创建时间</summary>
    [Category("扩展")]
    [DisplayName("创建时间")]
    [Description("创建时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("CreateTime", "创建时间", "")]
    public DateTime CreateTime { get => _CreateTime; set { if (OnPropertyChanging("CreateTime", value)) { _CreateTime = value; OnPropertyChanged("CreateTime"); } } }
    #endregion

    #region 获取/设置 字段值
    /// <summary>获取/设置 字段值</summary>
    /// <param name="name">字段名</param>
    /// <returns></returns>
    public override Object this[String name]
    {
        get => name switch
        {
            "Id" => _Id,
            "EventKey" => _EventKey,
            "MessageId" => _MessageId,
            "OrderId" => _OrderId,
            "BoxId" => _BoxId,
            "DeviceId" => _DeviceId,
            "IsPiece" => _IsPiece,
            "PlanQty" => _PlanQty,
            "CompletedQty" => _CompletedQty,
            "EventTime" => _EventTime,
            "ExtJson" => _ExtJson,
            "ParseVersion" => _ParseVersion,
            "CreateTime" => _CreateTime,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToLong(); break;
                case "EventKey": _EventKey = Convert.ToString(value); break;
                case "MessageId": _MessageId = Convert.ToString(value); break;
                case "OrderId": _OrderId = Convert.ToString(value); break;
                case "BoxId": _BoxId = Convert.ToString(value); break;
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "IsPiece": _IsPiece = value.ToBoolean(); break;
                case "PlanQty": _PlanQty = value.ToInt(); break;
                case "CompletedQty": _CompletedQty = value.ToInt(); break;
                case "EventTime": _EventTime = value.ToDateTime(); break;
                case "ExtJson": _ExtJson = Convert.ToString(value); break;
                case "ParseVersion": _ParseVersion = Convert.ToString(value); break;
                case "CreateTime": _CreateTime = value.ToDateTime(); break;
                default: base[name] = value; break;
            }
        }
    }
    #endregion

    #region 关联映射
    #endregion

    #region 扩展查询
    /// <summary>根据编号查找</summary>
    /// <param name="id">编号</param>
    /// <returns>实体对象</returns>
    public static ProductionRecord FindById(Int64 id)
    {
        if (id < 0) return null;

        return Find(_.Id == id);
    }

    /// <summary>根据幂等键查找</summary>
    /// <param name="eventKey">幂等键</param>
    /// <returns>实体对象</returns>
    public static ProductionRecord FindByEventKey(String eventKey)
    {
        if (eventKey.IsNullOrEmpty()) return null;

        return Find(_.EventKey == eventKey);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="orderId">工单号</param>
    /// <param name="deviceId">设备编号</param>
    /// <param name="eventTime">设备侧事件时间 UTC</param>
    /// <param name="isPiece">是否逐件完成事件</param>
    /// <param name="start">编号开始</param>
    /// <param name="end">编号结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<ProductionRecord> Search(String orderId, String deviceId, DateTime eventTime, Boolean? isPiece, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!orderId.IsNullOrEmpty()) exp &= _.OrderId == orderId;
        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        if (isPiece != null) exp &= _.IsPiece == isPiece;
        exp &= _.Id.Between(start, end, Meta.Factory.Snow);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 数据清理
    /// <summary>清理指定时间段内的数据</summary>
    /// <param name="start">开始时间。未指定时清理小于指定时间的所有数据</param>
    /// <param name="end">结束时间</param>
    /// <param name="maximumRows">最大删除行数。清理历史数据时，避免一次性删除过多导致数据库IO跟不上，0表示所有</param>
    /// <returns>清理行数</returns>
    public static Int32 DeleteWith(DateTime start, DateTime end, Int32 maximumRows = 0)
    {
        return Delete(_.Id.Between(start, end, Meta.Factory.Snow), maximumRows);
    }
    #endregion

    #region 字段名
    /// <summary>取得逐件完成明细字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号。时间缩放，便于按月分表</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>幂等键。分片内唯一</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>消息追踪 ID</summary>
        public static readonly Field MessageId = FindByName("MessageId");

        /// <summary>工单号</summary>
        public static readonly Field OrderId = FindByName("OrderId");

        /// <summary>盒型或盒号原值，不推断语义</summary>
        public static readonly Field BoxId = FindByName("BoxId");

        /// <summary>设备编号</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>是否逐件完成事件</summary>
        public static readonly Field IsPiece = FindByName("IsPiece");

        /// <summary>计划数量</summary>
        public static readonly Field PlanQty = FindByName("PlanQty");

        /// <summary>累计完成数。按取大收敛</summary>
        public static readonly Field CompletedQty = FindByName("CompletedQty");

        /// <summary>设备侧事件时间 UTC</summary>
        public static readonly Field EventTime = FindByName("EventTime");

        /// <summary>扩展字段 JSON</summary>
        public static readonly Field ExtJson = FindByName("ExtJson");

        /// <summary>解析版本</summary>
        public static readonly Field ParseVersion = FindByName("ParseVersion");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得逐件完成明细字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号。时间缩放，便于按月分表</summary>
        public const String Id = "Id";

        /// <summary>幂等键。分片内唯一</summary>
        public const String EventKey = "EventKey";

        /// <summary>消息追踪 ID</summary>
        public const String MessageId = "MessageId";

        /// <summary>工单号</summary>
        public const String OrderId = "OrderId";

        /// <summary>盒型或盒号原值，不推断语义</summary>
        public const String BoxId = "BoxId";

        /// <summary>设备编号</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>是否逐件完成事件</summary>
        public const String IsPiece = "IsPiece";

        /// <summary>计划数量</summary>
        public const String PlanQty = "PlanQty";

        /// <summary>累计完成数。按取大收敛</summary>
        public const String CompletedQty = "CompletedQty";

        /// <summary>设备侧事件时间 UTC</summary>
        public const String EventTime = "EventTime";

        /// <summary>扩展字段 JSON</summary>
        public const String ExtJson = "ExtJson";

        /// <summary>解析版本</summary>
        public const String ParseVersion = "ParseVersion";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";
    }
    #endregion
}
