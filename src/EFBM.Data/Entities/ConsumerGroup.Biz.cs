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

public partial class ConsumerGroup : Entity<ConsumerGroup>
{
    #region 对象操作
    // 控制最大缓存数量，Find/FindAll查询方法在表行数小于该值时走实体缓存
    private static Int32 MaxCacheCount = 1000;

    static ConsumerGroup()
    {

        // 拦截器 UserInterceptor、TimeInterceptor、IPInterceptor
        Meta.Interceptors.Add<TimeInterceptor>();
        Meta.Interceptors.Add(new IPInterceptor { AllowEmpty = false });

        // 实体缓存
        // var ec = Meta.Cache;
        // ec.Expire = 60;

        // 单对象缓存
        var sc = Meta.SingleCache;
        // sc.Expire = 60;
        sc.FindSlaveKeyMethod = k => Find(_.Name == k);
        sc.GetSlaveKeyMethod = e => e.Name;
    }

    /// <summary>验证并修补数据：校验组名格式、补齐归属系统名（STR-2a）。</summary>
    /// <param name="method">添删改方法</param>
    /// <returns>校验通过返回 <c>true</c>；格式不合法时抛出带原因的业务异常。</returns>
    public override Boolean Valid(DataMethod method)
    {
        if (method == DataMethod.Delete) return true;

        Name = Name?.Trim();
        var error = EFBM.Core.ConsumerGroupRules.Validate(Name);
        if (error != null) throw new InvalidOperationException(error);

        if (EFBM.Core.ConsumerGroupRules.IsInternal(Name))
            throw new InvalidOperationException($"{Name} 是本系统落库组，属于内部组，不需要登记");

        // 归属系统名由组名推导，不允许手填成不一致的值
        SystemName = EFBM.Core.ConsumerGroupRules.GetSystemName(Name);

        // 建议先调用基类方法，基类方法会做一些统一处理
        if (!base.Valid(method)) return false;

        return true;
    }

    /// <summary>禁止删除：组名一旦分配就不得复用，停用即可。</summary>
    /// <returns>不会返回，必然抛异常。</returns>
    /// <remarks>
    /// 删掉登记记录后，同名组可以重新登记给另一个系统，而 Redis 里那个组的
    /// <c>last-delivered-id</c> 与 PEL 还在原地——新系统会继承旧的积压，旧系统则永久丢消息。
    /// 「停用 + 备注」足以表达退出，历史留在表里。
    /// </remarks>
    protected override Int32 OnDelete()
        => throw new InvalidOperationException("消费组登记不允许删除：请把「启用」改为否并填写备注，组名不得复用");

    /// <summary>把监控里看到的组回写「最后出现时间」，未登记的组原样返回供页面提示。</summary>
    /// <param name="names">本次扫描到的组名集合。</param>
    /// <returns>未登记的组名。</returns>
    /// <remarks>
    /// 只回写已登记项：<b>不自动登记</b>未知名，否则任何人都能靠建一个消费组在后台凭空造出记录，
    /// 「登记」这件事必须有人负责（有责任人、有用途说明）。
    /// </remarks>
    public static String[] TouchSeen(IEnumerable<String> names)
    {
        var list = names?.Where(e => !String.IsNullOrWhiteSpace(e)).Distinct(StringComparer.Ordinal).ToArray()
            ?? Array.Empty<String>();
        if (list.Length == 0) return Array.Empty<String>();

        var unknown = new List<String>();
        var now = DateTime.UtcNow;

        foreach (var name in list)
        {
            var entity = FindByName(name);
            if (entity == null)
            {
                if (!EFBM.Core.ConsumerGroupRules.IsInternal(name)) unknown.Add(name);
                continue;
            }

            // 每分钟最多写一次，避免 10 秒刷新的监控页把库写热
            if (entity.LastSeen > now.AddMinutes(-1)) continue;

            entity.LastSeen = now;
            entity.Update();
        }

        return unknown.ToArray();
    }

    ///// <summary>首次连接数据库时初始化数据，仅用于实体类重载，用户不应该调用该方法</summary>
    //[EditorBrowsable(EditorBrowsableState.Never)]
    //protected override void InitData()
    //{
    //    // InitData一般用于当数据表没有数据时添加一些默认数据，该实体类的任何第一次数据库操作都会触发该方法，默认异步调用
    //    if (Meta.Session.Count > 0) return;

    //    if (XTrace.Debug) XTrace.WriteLine("开始初始化ConsumerGroup[消费组登记]数据……");

    //    var entity = new ConsumerGroup();
    //    entity.Name = "abc";
    //    entity.SystemName = "abc";
    //    entity.Owner = "abc";
    //    entity.Enabled = true;
    //    entity.LastSeen = DateTime.Now;
    //    entity.Description = "abc";
    //    entity.Insert();

    //    if (XTrace.Debug) XTrace.WriteLine("完成初始化ConsumerGroup[消费组登记]数据！");
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

    // Select Count(Id) as Id,Category From ConsumerGroup Where CreateTime>'2020-01-24 00:00:00' Group By Category Order By Id Desc limit 20
    //static readonly FieldCache<ConsumerGroup> _CategoryCache = new(nameof(Category))
    //{
    //Where = _.CreateTime > DateTime.Today.AddDays(-30) & Expression.Empty
    //};

    ///// <summary>获取类别列表，字段缓存10分钟，分组统计数据最多的前20种，用于魔方前台下拉选择</summary>
    ///// <returns></returns>
    //public static IDictionary<String, String> GetCategoryList() => _CategoryCache.FindAllName();
    #endregion

    #region 业务操作
    #endregion
}
