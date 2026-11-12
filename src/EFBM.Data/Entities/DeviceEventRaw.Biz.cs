using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using NewLife;
using NewLife.Data;
using NewLife.Log;
using NewLife.Model;
using NewLife.Reflection;
using NewLife.Threading;
using NewLife.Web;
using XCode;
using XCode.Cache;
using XCode.Configuration;
using XCode.DataAccessLayer;
using XCode.Membership;
using XCode.Shards;

namespace EFBM.Data.Entities;

public partial class DeviceEventRaw : Entity<DeviceEventRaw>
{
    #region 对象操作
    static DeviceEventRaw()
    {
        // 累加字段，生成 Update xx Set Count=Count+1234 Where xxx
        //var df = Meta.Factory.AdditionalFields;
        //df.Add(nameof(ProcessStatus));

        // 拦截器 UserInterceptor、TimeInterceptor、IPInterceptor
        Meta.Interceptors.Add<TimeInterceptor>();

        // 实体缓存
        // var ec = Meta.Cache;
        // ec.Expire = 60;

        // 单对象缓存
        var sc = Meta.SingleCache;
        // sc.Expire = 60;
        sc.FindSlaveKeyMethod = k => Find(_.EventKey == k);
        sc.GetSlaveKeyMethod = e => e.EventKey;
    }

    /// <summary>验证并修补数据，返回验证结果，或者通过抛出异常的方式提示验证失败。</summary>
    /// <param name="method">添删改方法</param>
    public override Boolean Valid(DataMethod method)
    {
        //if (method == DataMethod.Delete) return true;
        // 如果没有脏数据，则不需要进行任何处理
        if (!HasDirty) return true;

        // 建议先调用基类方法，基类方法会做一些统一处理
        if (!base.Valid(method)) return false;

        // 在新插入数据或者修改了指定字段时进行修正
        //if (method == DataMethod.Insert && !Dirtys[nameof(CreateTime)]) CreateTime = DateTime.Now;

        // 检查唯一索引
        // CheckExist(method == DataMethod.Insert, nameof(EventKey));

        return true;
    }

    ///// <summary>首次连接数据库时初始化数据，仅用于实体类重载，用户不应该调用该方法</summary>
    //[EditorBrowsable(EditorBrowsableState.Never)]
    //protected override void InitData()
    //{
    //    // InitData一般用于当数据表没有数据时添加一些默认数据，该实体类的任何第一次数据库操作都会触发该方法，默认异步调用
    //    if (Meta.Session.Count > 0) return;

    //    if (XTrace.Debug) XTrace.WriteLine("开始初始化DeviceEventRaw[原始上报报文]数据……");

    //    var entity = new DeviceEventRaw();
    //    entity.Id = 0;
    //    entity.EventKey = "abc";
    //    entity.MessageId = "abc";
    //    entity.StreamId = "abc";
    //    entity.Event = "abc";
    //    entity.DeviceId = "abc";
    //    entity.EventTimeRaw = "abc";
    //    entity.EventTime = DateTime.Now;
    //    entity.RawJson = "abc";
    //    entity.DataJson = "abc";
    //    entity.ExtJson = "abc";
    //    entity.UnknownFields = "abc";
    //    entity.ParseVersion = "abc";
    //    entity.ParseWarning = "abc";
    //    entity.SourceIp = "abc";
    //    entity.ReceivedAt = DateTime.Now;
    //    entity.PersistedAt = DateTime.Now;
    //    entity.ProcessStatus = 0;
    //    entity.RepeatCount = 0;
    //    entity.ReplayOf = "abc";
    //    entity.HandleStatus = 0;
    //    entity.HandleRemark = "abc";
    //    entity.HandleBy = "abc";
    //    entity.HandleAt = DateTime.Now;
    //    entity.Insert();

    //    if (XTrace.Debug) XTrace.WriteLine("完成初始化DeviceEventRaw[原始上报报文]数据！");
    //}

    ///// <summary>已重载。基类先调用Valid(true)验证数据，然后在事务保护内调用OnInsert</summary>
    ///// <returns></returns>
    //public override Int32 Insert()
    //{
    //    return base.Insert();
    //}

    ///// <summary>已重载。在事务保护范围内处理业务，位于Valid之后</summary>
    ///// <returns></returns>
    //protected override Int32 OnDelete()
    //{
    //    return base.OnDelete();
    //}
    #endregion

    #region 扩展属性
    #endregion

    #region 高级查询

    // Select Count(Id) as Id,DeviceId From DeviceEventRaw Where EventTime>'2020-01-24 00:00:00' Group By DeviceId Order By Id Desc limit 20
    static readonly FieldCache<DeviceEventRaw> _DeviceIdCache = new(nameof(DeviceId))
    {
        //Where = _.EventTime > DateTime.Today.AddDays(-30) & Expression.Empty
    };

    /// <summary>获取设备编号列表，字段缓存10分钟，分组统计数据最多的前20种，用于魔方前台下拉选择</summary>
    /// <returns></returns>
    public static IDictionary<String, String> GetDeviceIdList() => _DeviceIdCache.FindAllName();

    // Select Count(Id) as Id,Event From DeviceEventRaw Where EventTime>'2020-01-24 00:00:00' Group By Event Order By Id Desc limit 20
    static readonly FieldCache<DeviceEventRaw> _EventCache = new(nameof(Event))
    {
        //Where = _.EventTime > DateTime.Today.AddDays(-30) & Expression.Empty
    };

    /// <summary>获取事件类型列表，字段缓存10分钟，分组统计数据最多的前20种，用于魔方前台下拉选择</summary>
    /// <returns></returns>
    public static IDictionary<String, String> GetEventList() => _EventCache.FindAllName();

    // Select Count(Id) as Id,StreamId From DeviceEventRaw Where EventTime>'2020-01-24 00:00:00' Group By StreamId Order By Id Desc limit 20
    static readonly FieldCache<DeviceEventRaw> _StreamIdCache = new(nameof(StreamId))
    {
        //Where = _.EventTime > DateTime.Today.AddDays(-30) & Expression.Empty
    };

    /// <summary>获取RedisStream消息ID列表，字段缓存10分钟，分组统计数据最多的前20种，用于魔方前台下拉选择</summary>
    /// <returns></returns>
    public static IDictionary<String, String> GetStreamIdList() => _StreamIdCache.FindAllName();
    #endregion

    #region 业务操作
    #endregion
}
