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

/// <summary>本地兜底队列</summary>
[Serializable]
[DataObject]
[Description("本地兜底队列")]
[BindIndex("IX_LocalQueueFallback_MessageId", false, "MessageId")]
[BindIndex("IX_LocalQueueFallback_RequeuedAt", false, "RequeuedAt")]
[BindTable("LocalQueueFallback", Description = "本地兜底队列", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class LocalQueueFallback
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

    private DateTime _RequeuedAt;
    /// <summary>回灌入流时间</summary>
    [DisplayName("回灌入流时间")]
    [Description("回灌入流时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("RequeuedAt", "回灌入流时间", "")]
    public DateTime RequeuedAt { get => _RequeuedAt; set { if (OnPropertyChanging("RequeuedAt", value)) { _RequeuedAt = value; OnPropertyChanged("RequeuedAt"); } } }

    private Int32 _RetryCount;
    /// <summary>回灌尝试次数</summary>
    [DisplayName("回灌尝试次数")]
    [Description("回灌尝试次数")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("RetryCount", "回灌尝试次数", "")]
    public Int32 RetryCount { get => _RetryCount; set { if (OnPropertyChanging("RetryCount", value)) { _RetryCount = value; OnPropertyChanged("RetryCount"); } } }

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
            "EventKey" => _EventKey,
            "RawJson" => _RawJson,
            "RequeuedAt" => _RequeuedAt,
            "RetryCount" => _RetryCount,
            "CreateTime" => _CreateTime,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToLong(); break;
                case "MessageId": _MessageId = Convert.ToString(value); break;
                case "EventKey": _EventKey = Convert.ToString(value); break;
                case "RawJson": _RawJson = Convert.ToString(value); break;
                case "RequeuedAt": _RequeuedAt = value.ToDateTime(); break;
                case "RetryCount": _RetryCount = value.ToInt(); break;
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
    public static LocalQueueFallback FindById(Int64 id)
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
    public static IList<LocalQueueFallback> FindAllByMessageId(String messageId)
    {
        if (messageId.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.FindAll(e => e.MessageId.EqualIgnoreCase(messageId));

        return FindAll(_.MessageId == messageId);
    }
    #endregion

    #region 字段名
    /// <summary>取得本地兜底队列字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>消息追踪 ID</summary>
        public static readonly Field MessageId = FindByName("MessageId");

        /// <summary>幂等键</summary>
        public static readonly Field EventKey = FindByName("EventKey");

        /// <summary>原始消息体</summary>
        public static readonly Field RawJson = FindByName("RawJson");

        /// <summary>回灌入流时间</summary>
        public static readonly Field RequeuedAt = FindByName("RequeuedAt");

        /// <summary>回灌尝试次数</summary>
        public static readonly Field RetryCount = FindByName("RetryCount");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得本地兜底队列字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>消息追踪 ID</summary>
        public const String MessageId = "MessageId";

        /// <summary>幂等键</summary>
        public const String EventKey = "EventKey";

        /// <summary>原始消息体</summary>
        public const String RawJson = "RawJson";

        /// <summary>回灌入流时间</summary>
        public const String RequeuedAt = "RequeuedAt";

        /// <summary>回灌尝试次数</summary>
        public const String RetryCount = "RetryCount";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";
    }
    #endregion
}
