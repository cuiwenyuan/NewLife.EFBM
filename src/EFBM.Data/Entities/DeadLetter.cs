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

/// <summary>死信消息</summary>
[Serializable]
[DataObject]
[Description("死信消息")]
[BindIndex("IX_DeadLetter_MessageId", false, "MessageId")]
[BindIndex("IX_DeadLetter_ReplayStatus_CreateTime", false, "ReplayStatus,CreateTime")]
[BindTable("DeadLetter", Description = "死信消息", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class DeadLetter
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _MessageId;
    /// <summary>消息追踪 ID</summary>
    [DisplayName("消息追踪ID")]
    [Description("消息追踪 ID")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("MessageId", "消息追踪 ID", "", Master = true)]
    public String MessageId { get => _MessageId; set { if (OnPropertyChanging("MessageId", value)) { _MessageId = value; OnPropertyChanged("MessageId"); } } }

    private String _StreamId;
    /// <summary>Redis Stream 消息 ID</summary>
    [DisplayName("RedisStream消息ID")]
    [Description("Redis Stream 消息 ID")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("StreamId", "Redis Stream 消息 ID", "")]
    public String StreamId { get => _StreamId; set { if (OnPropertyChanging("StreamId", value)) { _StreamId = value; OnPropertyChanged("StreamId"); } } }

    private String _EventKey;
    /// <summary>幂等键</summary>
    [DisplayName("幂等键")]
    [Description("幂等键")]
    [DataObjectField(false, false, true, 64)]
    [BindColumn("EventKey", "幂等键", "")]
    public String EventKey { get => _EventKey; set { if (OnPropertyChanging("EventKey", value)) { _EventKey = value; OnPropertyChanged("EventKey"); } } }

    private String _RawJson;
    /// <summary>原始消息体</summary>
    [DisplayName("原始消息体")]
    [Description("原始消息体")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("RawJson", "原始消息体", "")]
    public String RawJson { get => _RawJson; set { if (OnPropertyChanging("RawJson", value)) { _RawJson = value; OnPropertyChanged("RawJson"); } } }

    private Int32 _RetryCount;
    /// <summary>累计重试次数</summary>
    [DisplayName("累计重试次数")]
    [Description("累计重试次数")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("RetryCount", "累计重试次数", "")]
    public Int32 RetryCount { get => _RetryCount; set { if (OnPropertyChanging("RetryCount", value)) { _RetryCount = value; OnPropertyChanged("RetryCount"); } } }

    private String _LastError;
    /// <summary>最后一次错误</summary>
    [DisplayName("最后一次错误")]
    [Description("最后一次错误")]
    [DataObjectField(false, false, true, 2000)]
    [BindColumn("LastError", "最后一次错误", "")]
    public String LastError { get => _LastError; set { if (OnPropertyChanging("LastError", value)) { _LastError = value; OnPropertyChanged("LastError"); } } }

    private Int32 _ReplayStatus;
    /// <summary>0待处理 1已重放 2已忽略</summary>
    [DisplayName("0待处理1已重放2已忽略")]
    [Description("0待处理 1已重放 2已忽略")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("ReplayStatus", "0待处理 1已重放 2已忽略", "")]
    public Int32 ReplayStatus { get => _ReplayStatus; set { if (OnPropertyChanging("ReplayStatus", value)) { _ReplayStatus = value; OnPropertyChanged("ReplayStatus"); } } }

    private DateTime _ReplayAt;
    /// <summary>重放时间</summary>
    [DisplayName("重放时间")]
    [Description("重放时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("ReplayAt", "重放时间", "")]
    public DateTime ReplayAt { get => _ReplayAt; set { if (OnPropertyChanging("ReplayAt", value)) { _ReplayAt = value; OnPropertyChanged("ReplayAt"); } } }

    private String _ReplayBy;
    /// <summary>重放操作人</summary>
    [DisplayName("重放操作人")]
    [Description("重放操作人")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("ReplayBy", "重放操作人", "")]
    public String ReplayBy { get => _ReplayBy; set { if (OnPropertyChanging("ReplayBy", value)) { _ReplayBy = value; OnPropertyChanged("ReplayBy"); } } }

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
            "MessageId" => _MessageId,
            "StreamId" => _StreamId,
            "EventKey" => _EventKey,
            "RawJson" => _RawJson,
            "RetryCount" => _RetryCount,
            "LastError" => _LastError,
            "ReplayStatus" => _ReplayStatus,
            "ReplayAt" => _ReplayAt,
            "ReplayBy" => _ReplayBy,
            "CreateTime" => _CreateTime,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToLong(); break;
                case "MessageId": _MessageId = Convert.ToString(value); break;
                case "StreamId": _StreamId = Convert.ToString(value); break;
                case "EventKey": _EventKey = Convert.ToString(value); break;
                case "RawJson": _RawJson = Convert.ToString(value); break;
                case "RetryCount": _RetryCount = value.ToInt(); break;
                case "LastError": _LastError = Convert.ToString(value); break;
                case "ReplayStatus": _ReplayStatus = value.ToInt(); break;
                case "ReplayAt": _ReplayAt = value.ToDateTime(); break;
                case "ReplayBy": _ReplayBy = Convert.ToString(value); break;
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
    public static DeadLetter FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据消息追踪ID查找</summary>
    /// <param name="messageId">消息追踪ID</param>
    /// <returns>实体列表</returns>
    public static IList<DeadLetter> FindAllByMessageId(String messageId)
    {
        if (messageId.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.MessageId.EqualIgnoreCase(messageId));

        return FindAll(_.MessageId == messageId);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="replayStatus">0待处理 1已重放 2已忽略</param>
    /// <param name="start">创建时间开始</param>
    /// <param name="end">创建时间结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<DeadLetter> Search(Int32 replayStatus, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (replayStatus >= 0) exp &= _.ReplayStatus == replayStatus;
        exp &= _.CreateTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得死信消息字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>消息追踪 ID</summary>
        public static readonly Field MessageId = FindByName("MessageId");

        /// <summary>Redis Stream 消息 ID</summary>
        public static readonly Field StreamId = FindByName("StreamId");

        /// <summary>幂等键</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>原始消息体</summary>
        public static readonly Field RawJson = FindByName("RawJson");

        /// <summary>累计重试次数</summary>
        public static readonly Field RetryCount = FindByName("RetryCount");

        /// <summary>最后一次错误</summary>
        public static readonly Field LastError = FindByName("LastError");

        /// <summary>0待处理 1已重放 2已忽略</summary>
        public static readonly Field ReplayStatus = FindByName("ReplayStatus");

        /// <summary>重放时间</summary>
        public static readonly Field ReplayAt = FindByName("ReplayAt");

        /// <summary>重放操作人</summary>
        public static readonly Field ReplayBy = FindByName("ReplayBy");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得死信消息字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>消息追踪 ID</summary>
        public const String MessageId = "MessageId";

        /// <summary>Redis Stream 消息 ID</summary>
        public const String StreamId = "StreamId";

        /// <summary>幂等键</summary>
        public const String EventKey = "EventKey";

        /// <summary>原始消息体</summary>
        public const String RawJson = "RawJson";

        /// <summary>累计重试次数</summary>
        public const String RetryCount = "RetryCount";

        /// <summary>最后一次错误</summary>
        public const String LastError = "LastError";

        /// <summary>0待处理 1已重放 2已忽略</summary>
        public const String ReplayStatus = "ReplayStatus";

        /// <summary>重放时间</summary>
        public const String ReplayAt = "ReplayAt";

        /// <summary>重放操作人</summary>
        public const String ReplayBy = "ReplayBy";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";
    }
    #endregion
}
