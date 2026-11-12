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

/// <summary>工单</summary>
[Serializable]
[DataObject]
[Description("工单")]
[BindIndex("IU_WorkOrder_OrderId", true, "OrderId")]
[BindIndex("IX_WorkOrder_DeviceId_StartedAt", false, "DeviceId,StartedAt")]
[BindIndex("IX_WorkOrder_Status", false, "Status")]
[BindTable("WorkOrder", Description = "工单", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class WorkOrder
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _OrderId;
    /// <summary>工单号</summary>
    [DisplayName("工单号")]
    [Description("工单号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("OrderId", "工单号", "", Master = true)]
    public String OrderId { get => _OrderId; set { if (OnPropertyChanging("OrderId", value)) { _OrderId = value; OnPropertyChanged("OrderId"); } } }

    private String _BoxId;
    /// <summary>盒型或盒号原值</summary>
    [DisplayName("盒型或盒号原值")]
    [Description("盒型或盒号原值")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("BoxId", "盒型或盒号原值", "")]
    public String BoxId { get => _BoxId; set { if (OnPropertyChanging("BoxId", value)) { _BoxId = value; OnPropertyChanged("BoxId"); } } }

    private String _DeviceId;
    /// <summary>设备编号</summary>
    [DisplayName("设备编号")]
    [Description("设备编号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private Int32 _PlanQty;
    /// <summary>计划数量</summary>
    [DisplayName("计划数量")]
    [Description("计划数量")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("PlanQty", "计划数量", "")]
    public Int32 PlanQty { get => _PlanQty; set { if (OnPropertyChanging("PlanQty", value)) { _PlanQty = value; OnPropertyChanged("PlanQty"); } } }

    private Int32 _CompletedQty;
    /// <summary>累计完成数。禁止旧值覆盖新值</summary>
    [DisplayName("累计完成数")]
    [Description("累计完成数。禁止旧值覆盖新值")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("CompletedQty", "累计完成数。禁止旧值覆盖新值", "")]
    public Int32 CompletedQty { get => _CompletedQty; set { if (OnPropertyChanging("CompletedQty", value)) { _CompletedQty = value; OnPropertyChanged("CompletedQty"); } } }

    private String _Status;
    /// <summary>派生状态。进行中/已完成</summary>
    [DisplayName("派生状态")]
    [Description("派生状态。进行中/已完成")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Status", "派生状态。进行中/已完成", "")]
    public String Status { get => _Status; set { if (OnPropertyChanging("Status", value)) { _Status = value; OnPropertyChanged("Status"); } } }

    private DateTime _StartedAt;
    /// <summary>开工时间 UTC</summary>
    [DisplayName("开工时间UTC")]
    [Description("开工时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("StartedAt", "开工时间 UTC", "")]
    public DateTime StartedAt { get => _StartedAt; set { if (OnPropertyChanging("StartedAt", value)) { _StartedAt = value; OnPropertyChanged("StartedAt"); } } }

    private DateTime _LastReportAt;
    /// <summary>最后上报时间 UTC</summary>
    [DisplayName("最后上报时间UTC")]
    [Description("最后上报时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("LastReportAt", "最后上报时间 UTC", "")]
    public DateTime LastReportAt { get => _LastReportAt; set { if (OnPropertyChanging("LastReportAt", value)) { _LastReportAt = value; OnPropertyChanged("LastReportAt"); } } }

    private DateTime _CompletedAt;
    /// <summary>派生完成时间 UTC</summary>
    [DisplayName("派生完成时间UTC")]
    [Description("派生完成时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("CompletedAt", "派生完成时间 UTC", "")]
    public DateTime CompletedAt { get => _CompletedAt; set { if (OnPropertyChanging("CompletedAt", value)) { _CompletedAt = value; OnPropertyChanged("CompletedAt"); } } }

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

    private DateTime _UpdateTime;
    /// <summary>更新时间</summary>
    [Category("扩展")]
    [DisplayName("更新时间")]
    [Description("更新时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("UpdateTime", "更新时间", "")]
    public DateTime UpdateTime { get => _UpdateTime; set { if (OnPropertyChanging("UpdateTime", value)) { _UpdateTime = value; OnPropertyChanged("UpdateTime"); } } }
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
            "OrderId" => _OrderId,
            "BoxId" => _BoxId,
            "DeviceId" => _DeviceId,
            "PlanQty" => _PlanQty,
            "CompletedQty" => _CompletedQty,
            "Status" => _Status,
            "StartedAt" => _StartedAt,
            "LastReportAt" => _LastReportAt,
            "CompletedAt" => _CompletedAt,
            "ExtJson" => _ExtJson,
            "ParseVersion" => _ParseVersion,
            "CreateTime" => _CreateTime,
            "UpdateTime" => _UpdateTime,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToLong(); break;
                case "OrderId": _OrderId = Convert.ToString(value); break;
                case "BoxId": _BoxId = Convert.ToString(value); break;
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "PlanQty": _PlanQty = value.ToInt(); break;
                case "CompletedQty": _CompletedQty = value.ToInt(); break;
                case "Status": _Status = Convert.ToString(value); break;
                case "StartedAt": _StartedAt = value.ToDateTime(); break;
                case "LastReportAt": _LastReportAt = value.ToDateTime(); break;
                case "CompletedAt": _CompletedAt = value.ToDateTime(); break;
                case "ExtJson": _ExtJson = Convert.ToString(value); break;
                case "ParseVersion": _ParseVersion = Convert.ToString(value); break;
                case "CreateTime": _CreateTime = value.ToDateTime(); break;
                case "UpdateTime": _UpdateTime = value.ToDateTime(); break;
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
    public static WorkOrder FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据工单号查找</summary>
    /// <param name="orderId">工单号</param>
    /// <returns>实体对象</returns>
    public static WorkOrder FindByOrderId(String orderId)
    {
        if (orderId.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.OrderId.EqualIgnoreCase(orderId));

        // 单对象缓存
        return Meta.SingleCache.GetItemWithSlaveKey(orderId) as WorkOrder;

        //return Find(_.OrderId == orderId);
    }

    /// <summary>根据派生状态查找</summary>
    /// <param name="status">派生状态</param>
    /// <returns>实体列表</returns>
    public static IList<WorkOrder> FindAllByStatus(String status)
    {
        if (status.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.Status.EqualIgnoreCase(status));

        return FindAll(_.Status == status);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="deviceId">设备编号</param>
    /// <param name="status">派生状态。进行中/已完成</param>
    /// <param name="start">开工时间UTC开始</param>
    /// <param name="end">开工时间UTC结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<WorkOrder> Search(String deviceId, String status, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        if (!status.IsNullOrEmpty()) exp &= _.Status == status;
        exp &= _.StartedAt.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得工单字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>工单号</summary>
        public static readonly Field OrderId = FindByName("OrderId");

        /// <summary>盒型或盒号原值</summary>
        public static readonly Field BoxId = FindByName("BoxId");

        /// <summary>设备编号</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>计划数量</summary>
        public static readonly Field PlanQty = FindByName("PlanQty");

        /// <summary>累计完成数。禁止旧值覆盖新值</summary>
        public static readonly Field CompletedQty = FindByName("CompletedQty");

        /// <summary>派生状态。进行中/已完成</summary>
        public static readonly Field Status = FindByName("Status");

        /// <summary>开工时间 UTC</summary>
        public static readonly Field StartedAt = FindByName("StartedAt");

        /// <summary>最后上报时间 UTC</summary>
        public static readonly Field LastReportAt = FindByName("LastReportAt");

        /// <summary>派生完成时间 UTC</summary>
        public static readonly Field CompletedAt = FindByName("CompletedAt");

        /// <summary>扩展字段 JSON</summary>
        public static readonly Field ExtJson = FindByName("ExtJson");

        /// <summary>解析版本</summary>
        public static readonly Field ParseVersion = FindByName("ParseVersion");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        /// <summary>更新时间</summary>
        public static readonly Field UpdateTime = FindByName("UpdateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得工单字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>工单号</summary>
        public const String OrderId = "OrderId";

        /// <summary>盒型或盒号原值</summary>
        public const String BoxId = "BoxId";

        /// <summary>设备编号</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>计划数量</summary>
        public const String PlanQty = "PlanQty";

        /// <summary>累计完成数。禁止旧值覆盖新值</summary>
        public const String CompletedQty = "CompletedQty";

        /// <summary>派生状态。进行中/已完成</summary>
        public const String Status = "Status";

        /// <summary>开工时间 UTC</summary>
        public const String StartedAt = "StartedAt";

        /// <summary>最后上报时间 UTC</summary>
        public const String LastReportAt = "LastReportAt";

        /// <summary>派生完成时间 UTC</summary>
        public const String CompletedAt = "CompletedAt";

        /// <summary>扩展字段 JSON</summary>
        public const String ExtJson = "ExtJson";

        /// <summary>解析版本</summary>
        public const String ParseVersion = "ParseVersion";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";

        /// <summary>更新时间</summary>
        public const String UpdateTime = "UpdateTime";
    }
    #endregion
}
