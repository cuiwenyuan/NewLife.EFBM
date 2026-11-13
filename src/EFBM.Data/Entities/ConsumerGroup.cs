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

/// <summary>消费组登记</summary>
[Serializable]
[DataObject]
[Description("消费组登记")]
[BindIndex("IU_ConsumerGroup_Name", true, "Name")]
[BindTable("ConsumerGroup", Description = "消费组登记", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class ConsumerGroup
{
    #region 属性
    private Int32 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int32 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _Name;
    /// <summary>消费组名。必须为 efbm-系统名</summary>
    [DisplayName("消费组名")]
    [Description("消费组名。必须为 efbm-系统名")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Name", "消费组名。必须为 efbm-系统名", "", Master = true)]
    public String Name { get => _Name; set { if (OnPropertyChanging("Name", value)) { _Name = value; OnPropertyChanged("Name"); } } }

    private String _SystemName;
    /// <summary>归属系统名称</summary>
    [DisplayName("归属系统名称")]
    [Description("归属系统名称")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("SystemName", "归属系统名称", "")]
    public String SystemName { get => _SystemName; set { if (OnPropertyChanging("SystemName", value)) { _SystemName = value; OnPropertyChanged("SystemName"); } } }

    private String _Owner;
    /// <summary>责任人 / 联系方式</summary>
    [DisplayName("责任人_联系方式")]
    [Description("责任人 / 联系方式")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Owner", "责任人 / 联系方式", "")]
    public String Owner { get => _Owner; set { if (OnPropertyChanging("Owner", value)) { _Owner = value; OnPropertyChanged("Owner"); } } }

    private Boolean _Enabled;
    /// <summary>启用</summary>
    [DisplayName("启用")]
    [Description("启用")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Enabled", "启用", "")]
    public Boolean Enabled { get => _Enabled; set { if (OnPropertyChanging("Enabled", value)) { _Enabled = value; OnPropertyChanged("Enabled"); } } }

    private DateTime _LastSeen;
    /// <summary>最后出现时间 UTC。监控扫描到该组时回写</summary>
    [DisplayName("最后出现时间UTC")]
    [Description("最后出现时间 UTC。监控扫描到该组时回写")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("LastSeen", "最后出现时间 UTC。监控扫描到该组时回写", "")]
    public DateTime LastSeen { get => _LastSeen; set { if (OnPropertyChanging("LastSeen", value)) { _LastSeen = value; OnPropertyChanged("LastSeen"); } } }

    private String _Description;
    /// <summary>用途说明</summary>
    [DisplayName("用途说明")]
    [Description("用途说明")]
    [DataObjectField(false, false, true, 500)]
    [BindColumn("Description", "用途说明", "")]
    public String Description { get => _Description; set { if (OnPropertyChanging("Description", value)) { _Description = value; OnPropertyChanged("Description"); } } }

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
            "Name" => _Name,
            "SystemName" => _SystemName,
            "Owner" => _Owner,
            "Enabled" => _Enabled,
            "LastSeen" => _LastSeen,
            "Description" => _Description,
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
                case "Id": _Id = value.ToInt(); break;
                case "Name": _Name = Convert.ToString(value); break;
                case "SystemName": _SystemName = Convert.ToString(value); break;
                case "Owner": _Owner = Convert.ToString(value); break;
                case "Enabled": _Enabled = value.ToBoolean(); break;
                case "LastSeen": _LastSeen = value.ToDateTime(); break;
                case "Description": _Description = Convert.ToString(value); break;
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
    public static ConsumerGroup FindById(Int32 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据消费组名查找</summary>
    /// <param name="name">消费组名</param>
    /// <returns>实体对象</returns>
    public static ConsumerGroup FindByName(String name)
    {
        if (name.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < MaxCacheCount) return Meta.Cache.Find(e => e.Name.EqualIgnoreCase(name));

        // 单对象缓存
        return Meta.SingleCache.GetItemWithSlaveKey(name) as ConsumerGroup;

        //return Find(_.Name == name);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="enabled">启用</param>
    /// <param name="start">更新时间开始</param>
    /// <param name="end">更新时间结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<ConsumerGroup> Search(Boolean? enabled, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (enabled != null) exp &= _.Enabled == enabled;
        exp &= _.UpdateTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得消费组登记字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>消费组名。必须为 efbm-系统名</summary>
        public static readonly Field Name = FindByName("Name");

        /// <summary>归属系统名称</summary>
        public static readonly Field SystemName = FindByName("SystemName");

        /// <summary>责任人 / 联系方式</summary>
        public static readonly Field Owner = FindByName("Owner");

        /// <summary>启用</summary>
        public static readonly Field Enabled = FindByName("Enabled");

        /// <summary>最后出现时间 UTC。监控扫描到该组时回写</summary>
        public static readonly Field LastSeen = FindByName("LastSeen");

        /// <summary>用途说明</summary>
        public static readonly Field Description = FindByName("Description");

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

    /// <summary>取得消费组登记字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>消费组名。必须为 efbm-系统名</summary>
        public const String Name = "Name";

        /// <summary>归属系统名称</summary>
        public const String SystemName = "SystemName";

        /// <summary>责任人 / 联系方式</summary>
        public const String Owner = "Owner";

        /// <summary>启用</summary>
        public const String Enabled = "Enabled";

        /// <summary>最后出现时间 UTC。监控扫描到该组时回写</summary>
        public const String LastSeen = "LastSeen";

        /// <summary>用途说明</summary>
        public const String Description = "Description";

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
