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

/// <summary>故障记录</summary>
[Serializable]
[DataObject]
[Description("故障记录")]
[BindIndex("IX_FaultRecord_DeviceId_OccurredAt", false, "DeviceId,OccurredAt")]
[BindIndex("IX_FaultRecord_Status", false, "Status")]
[BindIndex("IX_FaultRecord_EventKey", false, "EventKey")]
[BindTable("FaultRecord", Description = "故障记录", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class FaultRecord
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _EventKey;
    /// <summary>发生事件幂等键</summary>
    [DisplayName("发生事件幂等键")]
    [Description("发生事件幂等键")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("EventKey", "发生事件幂等键", "")]
    public String EventKey { get => _EventKey; set { if (OnPropertyChanging("EventKey", value)) { _EventKey = value; OnPropertyChanged("EventKey"); } } }

    private String _DeviceId;
    /// <summary>设备编号</summary>
    [DisplayName("设备编号")]
    [Description("设备编号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private String _Message;
    /// <summary>故障信息</summary>
    [DisplayName("故障信息")]
    [Description("故障信息")]
    [DataObjectField(false, false, true, 2000)]
    [BindColumn("Message", "故障信息", "")]
    public String Message { get => _Message; set { if (OnPropertyChanging("Message", value)) { _Message = value; OnPropertyChanged("Message"); } } }

    private DateTime _OccurredAt;
    /// <summary>发生时间 UTC</summary>
    [DisplayName("发生时间UTC")]
    [Description("发生时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("OccurredAt", "发生时间 UTC", "")]
    public DateTime OccurredAt { get => _OccurredAt; set { if (OnPropertyChanging("OccurredAt", value)) { _OccurredAt = value; OnPropertyChanged("OccurredAt"); } } }

    private DateTime _ClearedAt;
    /// <summary>清除时间 UTC</summary>
    [DisplayName("清除时间UTC")]
    [Description("清除时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("ClearedAt", "清除时间 UTC", "")]
    public DateTime ClearedAt { get => _ClearedAt; set { if (OnPropertyChanging("ClearedAt", value)) { _ClearedAt = value; OnPropertyChanged("ClearedAt"); } } }

    private String _ClearEventKey;
    /// <summary>清除事件幂等键</summary>
    [DisplayName("清除事件幂等键")]
    [Description("清除事件幂等键")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("ClearEventKey", "清除事件幂等键", "")]
    public String ClearEventKey { get => _ClearEventKey; set { if (OnPropertyChanging("ClearEventKey", value)) { _ClearEventKey = value; OnPropertyChanged("ClearEventKey"); } } }

    private Int32 _Status;
    /// <summary>0未清除 1已清除 2手动关闭</summary>
    [DisplayName("0未清除1已清除2手动关闭")]
    [Description("0未清除 1已清除 2手动关闭")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Status", "0未清除 1已清除 2手动关闭", "")]
    public Int32 Status { get => _Status; set { if (OnPropertyChanging("Status", value)) { _Status = value; OnPropertyChanged("Status"); } } }

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
            "EventKey" => _EventKey,
            "DeviceId" => _DeviceId,
            "Message" => _Message,
            "OccurredAt" => _OccurredAt,
            "ClearedAt" => _ClearedAt,
            "ClearEventKey" => _ClearEventKey,
            "Status" => _Status,
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
                case "EventKey": _EventKey = Convert.ToString(value); break;
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "Message": _Message = Convert.ToString(value); break;
                case "OccurredAt": _OccurredAt = value.ToDateTime(); break;
                case "ClearedAt": _ClearedAt = value.ToDateTime(); break;
                case "ClearEventKey": _ClearEventKey = Convert.ToString(value); break;
                case "Status": _Status = value.ToInt(); break;
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
    public static FaultRecord FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据0未清除1已清除2手动关闭查找</summary>
    /// <param name="status">0未清除1已清除2手动关闭</param>
    /// <returns>实体列表</returns>
    public static IList<FaultRecord> FindAllByStatus(Int32 status)
    {
        if (status < 0) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.Status == status);

        return FindAll(_.Status == status);
    }

    /// <summary>根据发生事件幂等键查找</summary>
    /// <param name="eventKey">发生事件幂等键</param>
    /// <returns>实体列表</returns>
    public static IList<FaultRecord> FindAllByEventKey(String eventKey)
    {
        if (eventKey.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.EventKey.EqualIgnoreCase(eventKey));

        return FindAll(_.EventKey == eventKey);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="eventKey">发生事件幂等键</param>
    /// <param name="deviceId">设备编号</param>
    /// <param name="status">0未清除 1已清除 2手动关闭</param>
    /// <param name="start">发生时间UTC开始</param>
    /// <param name="end">发生时间UTC结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<FaultRecord> Search(String eventKey, String deviceId, Int32 status, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!eventKey.IsNullOrEmpty()) exp &= _.EventKey == eventKey;
        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        if (status >= 0) exp &= _.Status == status;
        exp &= _.OccurredAt.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得故障记录字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>发生事件幂等键</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>设备编号</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>故障信息</summary>
        public static readonly Field Message = FindByName("Message");

        /// <summary>发生时间 UTC</summary>
        public static readonly Field OccurredAt = FindByName("OccurredAt");

        /// <summary>清除时间 UTC</summary>
        public static readonly Field ClearedAt = FindByName("ClearedAt");

        /// <summary>清除事件幂等键</summary>
        public static readonly Field ClearEventKey = FindByName("ClearEventKey");

        /// <summary>0未清除 1已清除 2手动关闭</summary>
        public static readonly Field Status = FindByName("Status");

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

    /// <summary>取得故障记录字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>发生事件幂等键</summary>
        public const String EventKey = "EventKey";

        /// <summary>设备编号</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>故障信息</summary>
        public const String Message = "Message";

        /// <summary>发生时间 UTC</summary>
        public const String OccurredAt = "OccurredAt";

        /// <summary>清除时间 UTC</summary>
        public const String ClearedAt = "ClearedAt";

        /// <summary>清除事件幂等键</summary>
        public const String ClearEventKey = "ClearEventKey";

        /// <summary>0未清除 1已清除 2手动关闭</summary>
        public const String Status = "Status";

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
