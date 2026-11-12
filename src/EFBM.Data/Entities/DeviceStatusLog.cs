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

/// <summary>设备状态流水</summary>
[Serializable]
[DataObject]
[Description("设备状态流水")]
[BindIndex("IX_DeviceStatusLog_EventKey", false, "EventKey")]
[BindIndex("IX_DeviceStatusLog_DeviceId_EventTime", false, "DeviceId,EventTime")]
[BindTable("DeviceStatusLog", Description = "设备状态流水", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class DeviceStatusLog
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
    /// <summary>幂等键</summary>
    [DisplayName("幂等键")]
    [Description("幂等键")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("EventKey", "幂等键", "")]
    public String EventKey { get => _EventKey; set { if (OnPropertyChanging("EventKey", value)) { _EventKey = value; OnPropertyChanged("EventKey"); } } }

    private String _MessageId;
    /// <summary>消息追踪 ID</summary>
    [DisplayName("消息追踪ID")]
    [Description("消息追踪 ID")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("MessageId", "消息追踪 ID", "")]
    public String MessageId { get => _MessageId; set { if (OnPropertyChanging("MessageId", value)) { _MessageId = value; OnPropertyChanged("MessageId"); } } }

    private String _DeviceId;
    /// <summary>设备编号</summary>
    [DisplayName("设备编号")]
    [Description("设备编号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private String _StatusRaw;
    /// <summary>状态原值</summary>
    [DisplayName("状态原值")]
    [Description("状态原值")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("StatusRaw", "状态原值", "")]
    public String StatusRaw { get => _StatusRaw; set { if (OnPropertyChanging("StatusRaw", value)) { _StatusRaw = value; OnPropertyChanged("StatusRaw"); } } }

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
            "DeviceId" => _DeviceId,
            "StatusRaw" => _StatusRaw,
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
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "StatusRaw": _StatusRaw = Convert.ToString(value); break;
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
    public static DeviceStatusLog FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据幂等键查找</summary>
    /// <param name="eventKey">幂等键</param>
    /// <returns>实体列表</returns>
    public static IList<DeviceStatusLog> FindAllByEventKey(String eventKey)
    {
        if (eventKey.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.EventKey.EqualIgnoreCase(eventKey));

        return FindAll(_.EventKey == eventKey);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="eventKey">幂等键</param>
    /// <param name="deviceId">设备编号</param>
    /// <param name="start">设备侧事件时间UTC开始</param>
    /// <param name="end">设备侧事件时间UTC结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<DeviceStatusLog> Search(String eventKey, String deviceId, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!eventKey.IsNullOrEmpty()) exp &= _.EventKey == eventKey;
        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        exp &= _.EventTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得设备状态流水字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>幂等键</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>消息追踪 ID</summary>
        public static readonly Field MessageId = FindByName("MessageId");

        /// <summary>设备编号</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>状态原值</summary>
        public static readonly Field StatusRaw = FindByName("StatusRaw");

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

    /// <summary>取得设备状态流水字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>幂等键</summary>
        public const String EventKey = "EventKey";

        /// <summary>消息追踪 ID</summary>
        public const String MessageId = "MessageId";

        /// <summary>设备编号</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>状态原值</summary>
        public const String StatusRaw = "StatusRaw";

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
