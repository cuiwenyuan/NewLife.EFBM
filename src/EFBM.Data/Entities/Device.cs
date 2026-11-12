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

/// <summary>设备</summary>
[Serializable]
[DataObject]
[Description("设备")]
[BindIndex("IU_Device_DeviceId", true, "DeviceId")]
[BindIndex("IX_Device_LastReportTime", false, "LastReportTime")]
[BindTable("Device", Description = "设备", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class Device
{
    #region 属性
    private Int64 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int64 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _DeviceId;
    /// <summary>设备编号。与报文 deviceId 一致</summary>
    [DisplayName("设备编号")]
    [Description("设备编号。与报文 deviceId 一致")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DeviceId", "设备编号。与报文 deviceId 一致", "", Master = true)]
    public String DeviceId { get => _DeviceId; set { if (OnPropertyChanging("DeviceId", value)) { _DeviceId = value; OnPropertyChanged("DeviceId"); } } }

    private String _Name;
    /// <summary>设备名称</summary>
    [DisplayName("设备名称")]
    [Description("设备名称")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Name", "设备名称", "")]
    public String Name { get => _Name; set { if (OnPropertyChanging("Name", value)) { _Name = value; OnPropertyChanged("Name"); } } }

    private String _StatusRaw;
    /// <summary>当前状态原值。不做枚举校验</summary>
    [DisplayName("当前状态原值")]
    [Description("当前状态原值。不做枚举校验")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("StatusRaw", "当前状态原值。不做枚举校验", "")]
    public String StatusRaw { get => _StatusRaw; set { if (OnPropertyChanging("StatusRaw", value)) { _StatusRaw = value; OnPropertyChanged("StatusRaw"); } } }

    private DateTime _LastReportTime;
    /// <summary>最后上报时间 UTC</summary>
    [DisplayName("最后上报时间UTC")]
    [Description("最后上报时间 UTC")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("LastReportTime", "最后上报时间 UTC", "")]
    public DateTime LastReportTime { get => _LastReportTime; set { if (OnPropertyChanging("LastReportTime", value)) { _LastReportTime = value; OnPropertyChanged("LastReportTime"); } } }

    private Boolean _Enabled;
    /// <summary>启用</summary>
    [DisplayName("启用")]
    [Description("启用")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Enabled", "启用", "")]
    public Boolean Enabled { get => _Enabled; set { if (OnPropertyChanging("Enabled", value)) { _Enabled = value; OnPropertyChanged("Enabled"); } } }

    private String _ExtJson;
    /// <summary>扩展字段 JSON。新增字段固化前的载体</summary>
    [DisplayName("扩展字段JSON")]
    [Description("扩展字段 JSON。新增字段固化前的载体")]
    [DataObjectField(false, false, true, -1)]
    [BindColumn("ExtJson", "扩展字段 JSON。新增字段固化前的载体", "")]
    public String ExtJson { get => _ExtJson; set { if (OnPropertyChanging("ExtJson", value)) { _ExtJson = value; OnPropertyChanged("ExtJson"); } } }

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
            "DeviceId" => _DeviceId,
            "Name" => _Name,
            "StatusRaw" => _StatusRaw,
            "LastReportTime" => _LastReportTime,
            "Enabled" => _Enabled,
            "ExtJson" => _ExtJson,
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
                case "DeviceId": _DeviceId = Convert.ToString(value); break;
                case "Name": _Name = Convert.ToString(value); break;
                case "StatusRaw": _StatusRaw = Convert.ToString(value); break;
                case "LastReportTime": _LastReportTime = value.ToDateTime(); break;
                case "Enabled": _Enabled = value.ToBoolean(); break;
                case "ExtJson": _ExtJson = Convert.ToString(value); break;
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
    public static Device FindById(Int64 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据设备编号查找</summary>
    /// <param name="deviceId">设备编号</param>
    /// <returns>实体对象</returns>
    public static Device FindByDeviceId(String deviceId)
    {
        if (deviceId.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.DeviceId.EqualIgnoreCase(deviceId));

        // 单对象缓存
        return Meta.SingleCache.GetItemWithSlaveKey(deviceId) as Device;

        //return Find(_.DeviceId == deviceId);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="enabled">启用</param>
    /// <param name="start">最后上报时间UTC开始</param>
    /// <param name="end">最后上报时间UTC结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<Device> Search(Boolean? enabled, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (enabled != null) exp &= _.Enabled == enabled;
        exp &= _.LastReportTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得设备字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>设备编号。与报文 deviceId 一致</summary>
        public static readonly Field DeviceId = FindByName("DeviceId");

        /// <summary>设备名称</summary>
        public static readonly Field Name = FindByName("Name");

        /// <summary>当前状态原值。不做枚举校验</summary>
        public static readonly Field StatusRaw = FindByName("StatusRaw");

        /// <summary>最后上报时间 UTC</summary>
        public static readonly Field LastReportTime = FindByName("LastReportTime");

        /// <summary>启用</summary>
        public static readonly Field Enabled = FindByName("Enabled");

        /// <summary>扩展字段 JSON。新增字段固化前的载体</summary>
        public static readonly Field ExtJson = FindByName("ExtJson");

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

    /// <summary>取得设备字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>设备编号。与报文 deviceId 一致</summary>
        public const String DeviceId = "DeviceId";

        /// <summary>设备名称</summary>
        public const String Name = "Name";

        /// <summary>当前状态原值。不做枚举校验</summary>
        public const String StatusRaw = "StatusRaw";

        /// <summary>最后上报时间 UTC</summary>
        public const String LastReportTime = "LastReportTime";

        /// <summary>启用</summary>
        public const String Enabled = "Enabled";

        /// <summary>扩展字段 JSON。新增字段固化前的载体</summary>
        public const String ExtJson = "ExtJson";

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
