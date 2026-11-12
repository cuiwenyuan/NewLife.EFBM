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

/// <summary>设备凭据</summary>
[Serializable]
[DataObject]
[Description("设备凭据")]
[BindIndex("IU_AppCredential_AppKey", true, "AppKey")]
[BindIndex("IU_AppCredential_DeviceId", true, "DeviceId")]
[BindTable("AppCredential", Description = "设备凭据", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class AppCredential
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _AppKey;
    /// <summary>应用键。对应 X-App-Key</summary>
    [DisplayName("应用键")]
    [Description("应用键。对应 X-App-Key")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("AppKey", "应用键。对应 X-App-Key", "", Master = true)]
    public String AppKey { get => _AppKey; set { if (OnPropertyChanging("AppKey", value)) { _AppKey = value; OnPropertyChanged("AppKey"); } } }

    private String _AppSecret;
    /// <summary>应用密钥。对应 X-App-Secret，日志与后台脱敏</summary>
    [DisplayName("应用密钥")]
    [Description("应用密钥。对应 X-App-Secret，日志与后台脱敏")]
    [DataObjectField(false, false, true, 200)]
    [BindColumn("AppSecret", "应用密钥。对应 X-App-Secret，日志与后台脱敏", "")]
    public String AppSecret { get => _AppSecret; set { if (OnPropertyChanging("AppSecret", value)) { _AppSecret = value; OnPropertyChanged("AppSecret"); } } }

    private String _DeviceId;
    /// <summary>绑定设备编号。与设备一对一</summary>
    [DisplayName("绑定设备编号")]
    [Description("绑定设备编号。与设备一对一")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "绑定设备编号。与设备一对一", "")]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private Boolean _Enabled;
    /// <summary>启用</summary>
    [DisplayName("启用")]
    [Description("启用")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Enabled", "启用", "")]
    public Boolean Enabled { get => _Enabled; set { if (OnPropertyChanging("Enabled", value)) { _Enabled = value; OnPropertyChanged("Enabled"); } } }

    private DateTime _LastUsedAt;
    /// <summary>最后使用时间</summary>
    [DisplayName("最后使用时间")]
    [Description("最后使用时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("LastUsedAt", "最后使用时间", "")]
    public DateTime LastUsedAt { get => _LastUsedAt; set { if (OnPropertyChanging("LastUsedAt", value)) { _LastUsedAt = value; OnPropertyChanged("LastUsedAt"); } } }

    private String _Remark;
    /// <summary>备注</summary>
    [DisplayName("备注")]
    [Description("备注")]
    [DataObjectField(false, false, true, 500)]
    [BindColumn("Remark", "备注", "")]
    public String Remark { get => _Remark; set { if (OnPropertyChanging("Remark", value)) { _Remark = value; OnPropertyChanged("Remark"); } } }

    private DateTime _CreateTime;
    /// <summary>创建时间</summary>
    [Category("扩展")]
    [DisplayName("创建时间")]
    [Description("创建时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("CreateTime", "创建时间", "")]
    public DateTime CreateTime { get => _CreateTime; set { if (OnPropertyChanging("CreateTime", value)) { _CreateTime = value; OnPropertyChanged("CreateTime"); } } }

    private String _CreateIP;
    /// <summary>创建地址</summary>
    [Category("扩展")]
    [DisplayName("创建地址")]
    [Description("创建地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("CreateIP", "创建地址", "")]
    public String CreateIP { get => _CreateIP; set { if (OnPropertyChanging("CreateIP", value)) { _CreateIP = value; OnPropertyChanged("CreateIP"); } } }

    private DateTime _UpdateTime;
    /// <summary>更新时间</summary>
    [Category("扩展")]
    [DisplayName("更新时间")]
    [Description("更新时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("UpdateTime", "更新时间", "")]
    public DateTime UpdateTime { get => _UpdateTime; set { if (OnPropertyChanging("UpdateTime", value)) { _UpdateTime = value; OnPropertyChanged("UpdateTime"); } } }

    private String _UpdateIP;
    /// <summary>更新地址</summary>
    [Category("扩展")]
    [DisplayName("更新地址")]
    [Description("更新地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("UpdateIP", "更新地址", "")]
    public String UpdateIP { get => _UpdateIP; set { if (OnPropertyChanging("UpdateIP", value)) { _UpdateIP = value; OnPropertyChanged("UpdateIP"); } } }
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
            "AppKey" => _AppKey,
            "AppSecret" => _AppSecret,
            "DeviceId" => _DeviceId,
            "Enabled" => _Enabled,
            "LastUsedAt" => _LastUsedAt,
            "Remark" => _Remark,
            "CreateTime" => _CreateTime,
            "CreateIP" => _CreateIP,
            "UpdateTime" => _UpdateTime,
            "UpdateIP" => _UpdateIP,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToLong(); break;
                case "AppKey": _AppKey = Convert.ToString(value); break;
                case "AppSecret": _AppSecret = Convert.ToString(value); break;
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "Enabled": _Enabled = value.ToBoolean(); break;
                case "LastUsedAt": _LastUsedAt = value.ToDateTime(); break;
                case "Remark": _Remark = Convert.ToString(value); break;
                case "CreateTime": _CreateTime = value.ToDateTime(); break;
                case "CreateIP": _CreateIP = Convert.ToString(value); break;
                case "UpdateTime": _UpdateTime = value.ToDateTime(); break;
                case "UpdateIP": _UpdateIP = Convert.ToString(value); break;
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
    public static AppCredential FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据应用键查找</summary>
    /// <param name="appKey">应用键</param>
    /// <returns>实体对象</returns>
    public static AppCredential FindByAppKey(String appKey)
    {
        if (appKey.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.AppKey.EqualIgnoreCase(appKey));

        // 单对象缓存
        return Meta.SingleCache.GetItemWithSlaveKey(appKey) as AppCredential;

        //return Find(_.AppKey == appKey);
    }

    /// <summary>根据绑定设备编号查找</summary>
    /// <param name="deviceId">绑定设备编号</param>
    /// <returns>实体对象</returns>
    public static AppCredential FindByDeviceId(String deviceId)
    {
        if (deviceId.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.DeviceId.EqualIgnoreCase(deviceId));

        return Find(_.DeviceId == deviceId);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="deviceId">绑定设备编号。与设备一对一</param>
    /// <param name="enabled">启用</param>
    /// <param name="start">更新时间开始</param>
    /// <param name="end">更新时间结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<AppCredential> Search(String deviceId, Boolean? enabled, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!deviceId.IsNullOrEmpty()) exp &= _.DeviceId == deviceId;
        if (enabled != null) exp &= _.Enabled == enabled;
        exp &= _.UpdateTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得设备凭据字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>应用键。对应 X-App-Key</summary>
        public static readonly Field AppKey = FindByName("AppKey");

        /// <summary>应用密钥。对应 X-App-Secret，日志与后台脱敏</summary>
        public static readonly Field AppSecret = FindByName("AppSecret");

        /// <summary>绑定设备编号。与设备一对一</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>启用</summary>
        public static readonly Field Enabled = FindByName("Enabled");

        /// <summary>最后使用时间</summary>
        public static readonly Field LastUsedAt = FindByName("LastUsedAt");

        /// <summary>备注</summary>
        public static readonly Field Remark = FindByName("Remark");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        /// <summary>创建地址</summary>
        public static readonly Field CreateIP = FindByName("CreateIP");

        /// <summary>更新时间</summary>
        public static readonly Field UpdateTime = FindByName("UpdateTime");

        /// <summary>更新地址</summary>
        public static readonly Field UpdateIP = FindByName("UpdateIP");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得设备凭据字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>应用键。对应 X-App-Key</summary>
        public const String AppKey = "AppKey";

        /// <summary>应用密钥。对应 X-App-Secret，日志与后台脱敏</summary>
        public const String AppSecret = "AppSecret";

        /// <summary>绑定设备编号。与设备一对一</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>启用</summary>
        public const String Enabled = "Enabled";

        /// <summary>最后使用时间</summary>
        public const String LastUsedAt = "LastUsedAt";

        /// <summary>备注</summary>
        public const String Remark = "Remark";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";

        /// <summary>创建地址</summary>
        public const String CreateIP = "CreateIP";

        /// <summary>更新时间</summary>
        public const String UpdateTime = "UpdateTime";

        /// <summary>更新地址</summary>
        public const String UpdateIP = "UpdateIP";
    }
    #endregion
}
