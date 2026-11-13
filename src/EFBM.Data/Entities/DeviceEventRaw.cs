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

/// <summary>原始上报报文</summary>
[Serializable]
[DataObject]
[Description("原始上报报文")]
[BindIndex("IU_DeviceEventRaw_EventKey", true, "EventKey")]
[BindIndex("IX_DeviceEventRaw_DeviceId_EventTime", false, "DeviceId,EventTime")]
[BindIndex("IX_DeviceEventRaw_Event_ReceivedAt", false, "Event,ReceivedAt")]
[BindIndex("IX_DeviceEventRaw_StreamId", false, "StreamId")]
[BindIndex("IX_DeviceEventRaw_ProcessStatus_HandleStatus_ReceivedAt", false, "ProcessStatus,HandleStatus,ReceivedAt")]
[BindTable("DeviceEventRaw", Description = "原始上报报文", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class DeviceEventRaw
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
    /// <summary>幂等键 SHA1(event|deviceId|timestamp)。分片内唯一</summary>
    [DisplayName("幂等键SHA1(event|deviceId|timestamp)")]
    [Description("幂等键 SHA1(event|deviceId|timestamp)。分片内唯一")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("EventKey", "幂等键 SHA1(event|deviceId|timestamp)。分片内唯一", "", Master = true)]
    public String EventKey { get => _EventKey; set { if (OnPropertyChanging("EventKey", value)) { _EventKey = value; OnPropertyChanged("EventKey"); } } }

    private String _MessageId;
    /// <summary>消息追踪 ID UUID。不参与去重</summary>
    [DisplayName("消息追踪IDUUID")]
    [Description("消息追踪 ID UUID。不参与去重")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("MessageId", "消息追踪 ID UUID。不参与去重", "")]
    public String MessageId { get => _MessageId; set { if (OnPropertyChanging("MessageId", value)) { _MessageId = value; OnPropertyChanged("MessageId"); } } }

    private String _StreamId;
    /// <summary>Redis Stream 消息 ID。消费时补记，用于定位与重放</summary>
    [DisplayName("RedisStream消息ID")]
    [Description("Redis Stream 消息 ID。消费时补记，用于定位与重放")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("StreamId", "Redis Stream 消息 ID。消费时补记，用于定位与重放", "")]
    public String StreamId { get => _StreamId; set { if (OnPropertyChanging("StreamId", value)) { _StreamId = value; OnPropertyChanged("StreamId"); } } }

    private String _Event;
    /// <summary>事件类型</summary>
    [DisplayName("事件类型")]
    [Description("事件类型")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Event", "事件类型", "")]
    public String Event { get => _Event; set { if (OnPropertyChanging("Event", value)) { _Event = value; OnPropertyChanged("Event"); } } }

    private String _DeviceId;
    /// <summary>设备编号</summary>
    [DisplayName("设备编号")]
    [Description("设备编号")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private String _EventTimeRaw;
    /// <summary>设备侧时间原始字符串。参与 EventKey</summary>
    [DisplayName("设备侧时间原始字符串")]
    [Description("设备侧时间原始字符串。参与 EventKey")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("EventTimeRaw", "设备侧时间原始字符串。参与 EventKey", "")]
    public String EventTimeRaw { get => _EventTimeRaw; set { if (OnPropertyChanging("EventTimeRaw", value)) { _EventTimeRaw = value; OnPropertyChanged("EventTimeRaw"); } } }

    private DateTime _EventTime;
    /// <summary>设备侧时间 UTC</summary>
    [DisplayName("设备侧时间UTC")]
    [Description("设备侧时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("EventTime", "设备侧时间 UTC", "")]
    public DateTime EventTime { get => _EventTime; set { if (OnPropertyChanging("EventTime", value)) { _EventTime = value; OnPropertyChanged("EventTime"); } } }

    private String _RawJson;
    /// <summary>未经改写的原始报文全文</summary>
    [DisplayName("未经改写的原始报文全文")]
    [Description("未经改写的原始报文全文")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("RawJson", "未经改写的原始报文全文", "")]
    public String RawJson { get => _RawJson; set { if (OnPropertyChanging("RawJson", value)) { _RawJson = value; OnPropertyChanged("RawJson"); } } }

    private String _DataJson;
    /// <summary>data 节点原文</summary>
    [DisplayName("data节点原文")]
    [Description("data 节点原文")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("DataJson", "data 节点原文", "")]
    public String DataJson { get => _DataJson; set { if (OnPropertyChanging("DataJson", value)) { _DataJson = value; OnPropertyChanged("DataJson"); } } }

    private String _ExtJson;
    /// <summary>未识别字段 JSON</summary>
    [DisplayName("未识别字段JSON")]
    [Description("未识别字段 JSON")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("ExtJson", "未识别字段 JSON", "")]
    public String ExtJson { get => _ExtJson; set { if (OnPropertyChanging("ExtJson", value)) { _ExtJson = value; OnPropertyChanged("ExtJson"); } } }

    private String _BoxType;
    /// <summary>箱型。2026-11-13 字段固化演练：从 data.boxType 固化为正式列，历史数据经回溯回填补齐</summary>
    [DisplayName("箱型")]
    [Description("箱型。2026-11-13 字段固化演练：从 data.boxType 固化为正式列，历史数据经回溯回填补齐")]
    [DataObjectField(false, false, true, 20)]
    [BindColumn("BoxType", "箱型。2026-11-13 字段固化演练：从 data.boxType 固化为正式列，历史数据经回溯回填补齐", "")]
    public String BoxType { get => _BoxType; set { if (OnPropertyChanging("BoxType", value)) { _BoxType = value; OnPropertyChanged("BoxType"); } } }

    private String _UnknownFields;
    /// <summary>未识别字段名清单，逗号分隔</summary>
    [DisplayName("未识别字段名清单")]
    [Description("未识别字段名清单，逗号分隔")]
    [DataObjectField(false, false, true, 1000)]
    [BindColumn("UnknownFields", "未识别字段名清单，逗号分隔", "")]
    public String UnknownFields { get => _UnknownFields; set { if (OnPropertyChanging("UnknownFields", value)) { _UnknownFields = value; OnPropertyChanged("UnknownFields"); } } }

    private String _ParseVersion;
    /// <summary>解析版本。当前 1.0</summary>
    [DisplayName("解析版本")]
    [Description("解析版本。当前 1.0")]
    [DataObjectField(false, false, true, 20)]
    [BindColumn("ParseVersion", "解析版本。当前 1.0", "")]
    public String ParseVersion { get => _ParseVersion; set { if (OnPropertyChanging("ParseVersion", value)) { _ParseVersion = value; OnPropertyChanged("ParseVersion"); } } }

    private String _ParseWarning;
    /// <summary>解析告警编码，见字段字典 §5</summary>
    [DisplayName("解析告警编码")]
    [Description("解析告警编码，见字段字典 §5")]
    [DataObjectField(false, false, true, 200)]
    [BindColumn("ParseWarning", "解析告警编码，见字段字典 §5", "")]
    public String ParseWarning { get => _ParseWarning; set { if (OnPropertyChanging("ParseWarning", value)) { _ParseWarning = value; OnPropertyChanged("ParseWarning"); } } }

    private String _SourceIp;
    /// <summary>来源 IP</summary>
    [DisplayName("来源IP")]
    [Description("来源 IP")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("SourceIp", "来源 IP", "")]
    public String SourceIp { get => _SourceIp; set { if (OnPropertyChanging("SourceIp", value)) { _SourceIp = value; OnPropertyChanged("SourceIp"); } } }

    private DateTime _ReceivedAt;
    /// <summary>服务端接收时间 UTC</summary>
    [DisplayName("服务端接收时间UTC")]
    [Description("服务端接收时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("ReceivedAt", "服务端接收时间 UTC", "")]
    public DateTime ReceivedAt { get => _ReceivedAt; set { if (OnPropertyChanging("ReceivedAt", value)) { _ReceivedAt = value; OnPropertyChanged("ReceivedAt"); } } }

    private DateTime _PersistedAt;
    /// <summary>落库时间 UTC</summary>
    [DisplayName("落库时间UTC")]
    [Description("落库时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("PersistedAt", "落库时间 UTC", "")]
    public DateTime PersistedAt { get => _PersistedAt; set { if (OnPropertyChanging("PersistedAt", value)) { _PersistedAt = value; OnPropertyChanged("PersistedAt"); } } }

    private Int32 _ProcessStatus;
    /// <summary>处理状态。0待处理 1已落库 2死信 3已重放</summary>
    [DisplayName("处理状态")]
    [Description("处理状态。0待处理 1已落库 2死信 3已重放")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("ProcessStatus", "处理状态。0待处理 1已落库 2死信 3已重放", "")]
    public Int32 ProcessStatus { get => _ProcessStatus; set { if (OnPropertyChanging("ProcessStatus", value)) { _ProcessStatus = value; OnPropertyChanged("ProcessStatus"); } } }

    private Int32 _RepeatCount;
    /// <summary>同一 EventKey 重复投递次数</summary>
    [DisplayName("同一EventKey重复投递次数")]
    [Description("同一 EventKey 重复投递次数")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("RepeatCount", "同一 EventKey 重复投递次数", "")]
    public Int32 RepeatCount { get => _RepeatCount; set { if (OnPropertyChanging("RepeatCount", value)) { _RepeatCount = value; OnPropertyChanged("RepeatCount"); } } }

    private String _ReplayOf;
    /// <summary>重放来源 MessageId</summary>
    [DisplayName("重放来源MessageId")]
    [Description("重放来源 MessageId")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("ReplayOf", "重放来源 MessageId", "")]
    public String ReplayOf { get => _ReplayOf; set { if (OnPropertyChanging("ReplayOf", value)) { _ReplayOf = value; OnPropertyChanged("ReplayOf"); } } }

    private Int32 _HandleStatus;
    /// <summary>人工处理状态。0未处理 1已确认 2已忽略</summary>
    [DisplayName("人工处理状态")]
    [Description("人工处理状态。0未处理 1已确认 2已忽略")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("HandleStatus", "人工处理状态。0未处理 1已确认 2已忽略", "")]
    public Int32 HandleStatus { get => _HandleStatus; set { if (OnPropertyChanging("HandleStatus", value)) { _HandleStatus = value; OnPropertyChanged("HandleStatus"); } } }

    private String _HandleRemark;
    /// <summary>人工处理备注</summary>
    [DisplayName("人工处理备注")]
    [Description("人工处理备注")]
    [DataObjectField(false, false, true, 500)]
    [BindColumn("HandleRemark", "人工处理备注", "")]
    public String HandleRemark { get => _HandleRemark; set { if (OnPropertyChanging("HandleRemark", value)) { _HandleRemark = value; OnPropertyChanged("HandleRemark"); } } }

    private String _HandleBy;
    /// <summary>人工处理人</summary>
    [DisplayName("人工处理人")]
    [Description("人工处理人")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("HandleBy", "人工处理人", "")]
    public String HandleBy { get => _HandleBy; set { if (OnPropertyChanging("HandleBy", value)) { _HandleBy = value; OnPropertyChanged("HandleBy"); } } }

    private DateTime _HandleAt;
    /// <summary>人工处理时间</summary>
    [DisplayName("人工处理时间")]
    [Description("人工处理时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("HandleAt", "人工处理时间", "")]
    public DateTime HandleAt { get => _HandleAt; set { if (OnPropertyChanging("HandleAt", value)) { _HandleAt = value; OnPropertyChanged("HandleAt"); } } }

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
            "StreamId" => _StreamId,
            "Event" => _Event,
            "DeviceId" => _DeviceId,
            "EventTimeRaw" => _EventTimeRaw,
            "EventTime" => _EventTime,
            "RawJson" => _RawJson,
            "DataJson" => _DataJson,
            "ExtJson" => _ExtJson,
            "BoxType" => _BoxType,
            "UnknownFields" => _UnknownFields,
            "ParseVersion" => _ParseVersion,
            "ParseWarning" => _ParseWarning,
            "SourceIp" => _SourceIp,
            "ReceivedAt" => _ReceivedAt,
            "PersistedAt" => _PersistedAt,
            "ProcessStatus" => _ProcessStatus,
            "RepeatCount" => _RepeatCount,
            "ReplayOf" => _ReplayOf,
            "HandleStatus" => _HandleStatus,
            "HandleRemark" => _HandleRemark,
            "HandleBy" => _HandleBy,
            "HandleAt" => _HandleAt,
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
                case "StreamId": _StreamId = Convert.ToString(value); break;
                case "Event": _Event = Convert.ToString(value); break;
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "EventTimeRaw": _EventTimeRaw = Convert.ToString(value); break;
                case "EventTime": _EventTime = value.ToDateTime(); break;
                case "RawJson": _RawJson = Convert.ToString(value); break;
                case "DataJson": _DataJson = Convert.ToString(value); break;
                case "ExtJson": _ExtJson = Convert.ToString(value); break;
                case "BoxType": _BoxType = Convert.ToString(value); break;
                case "UnknownFields": _UnknownFields = Convert.ToString(value); break;
                case "ParseVersion": _ParseVersion = Convert.ToString(value); break;
                case "ParseWarning": _ParseWarning = Convert.ToString(value); break;
                case "SourceIp": _SourceIp = Convert.ToString(value); break;
                case "ReceivedAt": _ReceivedAt = value.ToDateTime(); break;
                case "PersistedAt": _PersistedAt = value.ToDateTime(); break;
                case "ProcessStatus": _ProcessStatus = value.ToInt(); break;
                case "RepeatCount": _RepeatCount = value.ToInt(); break;
                case "ReplayOf": _ReplayOf = Convert.ToString(value); break;
                case "HandleStatus": _HandleStatus = value.ToInt(); break;
                case "HandleRemark": _HandleRemark = Convert.ToString(value); break;
                case "HandleBy": _HandleBy = Convert.ToString(value); break;
                case "HandleAt": _HandleAt = value.ToDateTime(); break;
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
    public static DeviceEventRaw FindById(Int64 id)
    {
        if (id < 0) return null;

        return Find(_.Id == id);
    }

    /// <summary>根据幂等键SHA1(event|deviceId|timestamp)查找</summary>
    /// <param name="eventKey">幂等键SHA1(event|deviceId|timestamp)</param>
    /// <returns>实体对象</returns>
    public static DeviceEventRaw FindByEventKey(String eventKey)
    {
        if (eventKey.IsNullOrEmpty()) return null;

        return Find(_.EventKey == eventKey);
    }

    /// <summary>根据RedisStream消息ID查找</summary>
    /// <param name="streamId">RedisStream消息ID</param>
    /// <returns>实体列表</returns>
    public static IList<DeviceEventRaw> FindAllByStreamId(String streamId)
    {
        if (streamId.IsNullOrEmpty()) return [];

        return FindAll(_.StreamId == streamId);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="streamId">Redis Stream 消息 ID。消费时补记，用于定位与重放</param>
    /// <param name="@event">事件类型</param>
    /// <param name="deviceId">设备编号</param>
    /// <param name="eventTime">设备侧时间 UTC</param>
    /// <param name="receivedAt">服务端接收时间 UTC</param>
    /// <param name="processStatus">处理状态。0待处理 1已落库 2死信 3已重放</param>
    /// <param name="handleStatus">人工处理状态。0未处理 1已确认 2已忽略</param>
    /// <param name="start">编号开始</param>
    /// <param name="end">编号结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<DeviceEventRaw> Search(String streamId, String @event, String deviceId, DateTime eventTime, DateTime receivedAt, Int32 processStatus, Int32 handleStatus, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!streamId.IsNullOrEmpty()) exp &= _.StreamId == streamId;
        if (!@event.IsNullOrEmpty()) exp &= _.Event == @event;
        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        if (processStatus >= 0) exp &= _.ProcessStatus == processStatus;
        if (handleStatus >= 0) exp &= _.HandleStatus == handleStatus;
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
    /// <summary>取得原始上报报文字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号。时间缩放，便于按月分表</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>幂等键 SHA1(event|deviceId|timestamp)。分片内唯一</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>消息追踪 ID UUID。不参与去重</summary>
        public static readonly Field MessageId = FindByName("MessageId");

        /// <summary>Redis Stream 消息 ID。消费时补记，用于定位与重放</summary>
        public static readonly Field StreamId = FindByName("StreamId");

        /// <summary>事件类型</summary>
        public static readonly Field Event = FindByName("Event");

        /// <summary>设备编号</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>设备侧时间原始字符串。参与 EventKey</summary>
        public static readonly Field EventTimeRaw = FindByName("EventTimeRaw");

        /// <summary>设备侧时间 UTC</summary>
        public static readonly Field EventTime = FindByName("EventTime");

        /// <summary>未经改写的原始报文全文</summary>
        public static readonly Field RawJson = FindByName("RawJson");

        /// <summary>data 节点原文</summary>
        public static readonly Field DataJson = FindByName("DataJson");

        /// <summary>未识别字段 JSON</summary>
        public static readonly Field ExtJson = FindByName("ExtJson");

        /// <summary>箱型。2026-11-13 字段固化演练：从 data.boxType 固化为正式列，历史数据经回溯回填补齐</summary>
        public static readonly Field BoxType = FindByName("BoxType");

        /// <summary>未识别字段名清单，逗号分隔</summary>
        public static readonly Field UnknownFields = FindByName("UnknownFields");

        /// <summary>解析版本。当前 1.0</summary>
        public static readonly Field ParseVersion = FindByName("ParseVersion");

        /// <summary>解析告警编码，见字段字典 §5</summary>
        public static readonly Field ParseWarning = FindByName("ParseWarning");

        /// <summary>来源 IP</summary>
        public static readonly Field SourceIp = FindByName("SourceIp");

        /// <summary>服务端接收时间 UTC</summary>
        public static readonly Field ReceivedAt = FindByName("ReceivedAt");

        /// <summary>落库时间 UTC</summary>
        public static readonly Field PersistedAt = FindByName("PersistedAt");

        /// <summary>处理状态。0待处理 1已落库 2死信 3已重放</summary>
        public static readonly Field ProcessStatus = FindByName("ProcessStatus");

        /// <summary>同一 EventKey 重复投递次数</summary>
        public static readonly Field RepeatCount = FindByName("RepeatCount");

        /// <summary>重放来源 MessageId</summary>
        public static readonly Field ReplayOf = FindByName("ReplayOf");

        /// <summary>人工处理状态。0未处理 1已确认 2已忽略</summary>
        public static readonly Field HandleStatus = FindByName("HandleStatus");

        /// <summary>人工处理备注</summary>
        public static readonly Field HandleRemark = FindByName("HandleRemark");

        /// <summary>人工处理人</summary>
        public static readonly Field HandleBy = FindByName("HandleBy");

        /// <summary>人工处理时间</summary>
        public static readonly Field HandleAt = FindByName("HandleAt");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得原始上报报文字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号。时间缩放，便于按月分表</summary>
        public const String Id = "Id";

        /// <summary>幂等键 SHA1(event|deviceId|timestamp)。分片内唯一</summary>
        public const String EventKey = "EventKey";

        /// <summary>消息追踪 ID UUID。不参与去重</summary>
        public const String MessageId = "MessageId";

        /// <summary>Redis Stream 消息 ID。消费时补记，用于定位与重放</summary>
        public const String StreamId = "StreamId";

        /// <summary>事件类型</summary>
        public const String Event = "Event";

        /// <summary>设备编号</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>设备侧时间原始字符串。参与 EventKey</summary>
        public const String EventTimeRaw = "EventTimeRaw";

        /// <summary>设备侧时间 UTC</summary>
        public const String EventTime = "EventTime";

        /// <summary>未经改写的原始报文全文</summary>
        public const String RawJson = "RawJson";

        /// <summary>data 节点原文</summary>
        public const String DataJson = "DataJson";

        /// <summary>未识别字段 JSON</summary>
        public const String ExtJson = "ExtJson";

        /// <summary>箱型。2026-11-13 字段固化演练：从 data.boxType 固化为正式列，历史数据经回溯回填补齐</summary>
        public const String BoxType = "BoxType";

        /// <summary>未识别字段名清单，逗号分隔</summary>
        public const String UnknownFields = "UnknownFields";

        /// <summary>解析版本。当前 1.0</summary>
        public const String ParseVersion = "ParseVersion";

        /// <summary>解析告警编码，见字段字典 §5</summary>
        public const String ParseWarning = "ParseWarning";

        /// <summary>来源 IP</summary>
        public const String SourceIp = "SourceIp";

        /// <summary>服务端接收时间 UTC</summary>
        public const String ReceivedAt = "ReceivedAt";

        /// <summary>落库时间 UTC</summary>
        public const String PersistedAt = "PersistedAt";

        /// <summary>处理状态。0待处理 1已落库 2死信 3已重放</summary>
        public const String ProcessStatus = "ProcessStatus";

        /// <summary>同一 EventKey 重复投递次数</summary>
        public const String RepeatCount = "RepeatCount";

        /// <summary>重放来源 MessageId</summary>
        public const String ReplayOf = "ReplayOf";

        /// <summary>人工处理状态。0未处理 1已确认 2已忽略</summary>
        public const String HandleStatus = "HandleStatus";

        /// <summary>人工处理备注</summary>
        public const String HandleRemark = "HandleRemark";

        /// <summary>人工处理人</summary>
        public const String HandleBy = "HandleBy";

        /// <summary>人工处理时间</summary>
        public const String HandleAt = "HandleAt";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";
    }
    #endregion
}
