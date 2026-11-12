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

/// <summary>字段登记</summary>
[Serializable]
[DataObject]
[Description("字段登记")]
[BindIndex("IU_FieldDict_Event_FieldName", true, "Event,FieldName")]
[BindTable("FieldDict", Description = "字段登记", ConnName = "EFBM", DbType = DatabaseType.None)]
public partial class FieldDict
{
    #region 属性
    private Int32 _Id;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("Id", "编号", "")]
    public Int32 Id { get => _Id; set { if (OnPropertyChanging("Id", value)) { _Id = value; OnPropertyChanged("Id"); } } }

    private String _Event;
    /// <summary>所属事件，空表示信封字段</summary>
    [DisplayName("所属事件")]
    [Description("所属事件，空表示信封字段")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Event", "所属事件，空表示信封字段", "", Master = true)]
    public String Event { get => _Event; set { if (OnPropertyChanging("Event", value)) { _Event = value; OnPropertyChanged("Event"); } } }

    private String _FieldName;
    /// <summary>字段名</summary>
    [DisplayName("字段名")]
    [Description("字段名")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("FieldName", "字段名", "")]
    public String FieldName { get => _FieldName; set { if (OnPropertyChanging("FieldName", value)) { _FieldName = value; OnPropertyChanged("FieldName"); } } }

    private String _DisplayName;
    /// <summary>中文名</summary>
    [DisplayName("中文名")]
    [Description("中文名")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("DisplayName", "中文名", "")]
    public String DisplayName { get => _DisplayName; set { if (OnPropertyChanging("DisplayName", value)) { _DisplayName = value; OnPropertyChanged("DisplayName"); } } }

    private String _JsonType;
    /// <summary>JSON 类型</summary>
    [DisplayName("JSON类型")]
    [Description("JSON 类型")]
    [DataObjectField(false, false, true, 30)]
    [BindColumn("JsonType", "JSON 类型", "")]
    public String JsonType { get => _JsonType; set { if (OnPropertyChanging("JsonType", value)) { _JsonType = value; OnPropertyChanged("JsonType"); } } }

    private Boolean _Solidified;
    /// <summary>是否已固化为正式列</summary>
    [DisplayName("是否已固化为正式列")]
    [Description("是否已固化为正式列")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Solidified", "是否已固化为正式列", "")]
    public Boolean Solidified { get => _Solidified; set { if (OnPropertyChanging("Solidified", value)) { _Solidified = value; OnPropertyChanged("Solidified"); } } }

    private String _Sample;
    /// <summary>样例值</summary>
    [DisplayName("样例值")]
    [Description("样例值")]
    [DataObjectField(false, false, true, 500)]
    [BindColumn("Sample", "样例值", "")]
    public String Sample { get => _Sample; set { if (OnPropertyChanging("Sample", value)) { _Sample = value; OnPropertyChanged("Sample"); } } }

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
            "Event" => _Event,
            "FieldName" => _FieldName,
            "DisplayName" => _DisplayName,
            "JsonType" => _JsonType,
            "Solidified" => _Solidified,
            "Sample" => _Sample,
            "Remark" => _Remark,
            "CreateTime" => _CreateTime,
            "UpdateTime" => _UpdateTime,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "Id": _Id = value.ToInt(); break;
                case "Event": _Event = Convert.ToString(value); break;
                case "FieldName": _FieldName = Convert.ToString(value); break;
                case "DisplayName": _DisplayName = Convert.ToString(value); break;
                case "JsonType": _JsonType = Convert.ToString(value); break;
                case "Solidified": _Solidified = value.ToBoolean(); break;
                case "Sample": _Sample = Convert.ToString(value); break;
                case "Remark": _Remark = Convert.ToString(value); break;
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
    public static FieldDict FindById(Int32 id)
    {
        if (id < 0) return null;

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.Find(e => e.Id == id);

        // 单对象缓存
        return Meta.SingleCache[id];

        //return Find(_.Id == id);
    }

    /// <summary>根据所属事件、字段名查找</summary>
    /// <param name="@event">所属事件</param>
    /// <param name="fieldName">字段名</param>
    /// <returns>实体对象</returns>
    public static FieldDict FindByEventAndFieldName(String @event, String fieldName)
    {
        if (@event.IsNullOrEmpty()) return null;
        if (fieldName.IsNullOrEmpty()) return null;

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.Find(e => e.Event.EqualIgnoreCase(@event) && e.FieldName.EqualIgnoreCase(fieldName));

        return Find(_.Event == @event & _.FieldName == fieldName);
    }

    /// <summary>根据所属事件查找</summary>
    /// <param name="@event">所属事件</param>
    /// <returns>实体列表</returns>
    public static IList<FieldDict> FindAllByEvent(String @event)
    {
        if (@event.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.FindAll(e => e.Event.EqualIgnoreCase(@event));

        return FindAll(_.Event == @event);
    }
    #endregion

    #region 高级查询
    /// <summary>高级查询</summary>
    /// <param name="fieldName">字段名</param>
    /// <param name="solidified">是否已固化为正式列</param>
    /// <param name="start">更新时间开始</param>
    /// <param name="end">更新时间结束</param>
    /// <param name="key">关键字</param>
    /// <param name="page">分页参数信息。可携带统计和数据权限扩展查询等信息</param>
    /// <returns>实体列表</returns>
    public static IList<FieldDict> Search(String fieldName, Boolean? solidified, DateTime start, DateTime end, String key, PageParameter page)
    {
        var exp = new WhereExpression();

        if (!fieldName.IsNullOrEmpty()) exp &= _.FieldName == fieldName;
        if (solidified != null) exp &= _.Solidified == solidified;
        exp &= _.UpdateTime.Between(start, end);
        if (!key.IsNullOrEmpty()) exp &= SearchWhereByKeys(key);

        return FindAll(exp, page);
    }
    #endregion

    #region 字段名
    /// <summary>取得字段登记字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field Id = FindByName("Id");

        /// <summary>所属事件，空表示信封字段</summary>
        public static readonly Field Event = FindByName("Event");

        /// <summary>字段名</summary>
        public static readonly Field FieldName = FindByName("FieldName");

        /// <summary>中文名</summary>
        public static readonly Field DisplayName = FindByName("DisplayName");

        /// <summary>JSON 类型</summary>
        public static readonly Field JsonType = FindByName("JsonType");

        /// <summary>是否已固化为正式列</summary>
        public static readonly Field Solidified = FindByName("Solidified");

        /// <summary>样例值</summary>
        public static readonly Field Sample = FindByName("Sample");

        /// <summary>备注</summary>
        public static readonly Field Remark = FindByName("Remark");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        /// <summary>更新时间</summary>
        public static readonly Field UpdateTime = FindByName("UpdateTime");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得字段登记字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String Id = "Id";

        /// <summary>所属事件，空表示信封字段</summary>
        public const String Event = "Event";

        /// <summary>字段名</summary>
        public const String FieldName = "FieldName";

        /// <summary>中文名</summary>
        public const String DisplayName = "DisplayName";

        /// <summary>JSON 类型</summary>
        public const String JsonType = "JsonType";

        /// <summary>是否已固化为正式列</summary>
        public const String Solidified = "Solidified";

        /// <summary>样例值</summary>
        public const String Sample = "Sample";

        /// <summary>备注</summary>
        public const String Remark = "Remark";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";

        /// <summary>更新时间</summary>
        public const String UpdateTime = "UpdateTime";
    }
    #endregion
}
