using System.ComponentModel;
using NewLife.Cube;

namespace EFBM.Web.Areas.Efbm;

/// <summary>EFBM 业务管理区域：把业务菜单与 Cube 自带的「系统管理 / 魔方管理」菜单分树展示。</summary>
/// <remarks>
/// Cube 按 Area 类型所在命名空间扫描控制器（本例为 <c>EFBM.Web.Areas.Efbm.Controllers</c>），
/// 并为每个 Area 建一个根菜单。基类名去掉 <c>Area</c> 后缀即区域名 <c>Efbm</c>，
/// 控制器 URL 随之变为 <c>/Efbm/{Controller}</c>，菜单显示名取 <see cref="DisplayNameAttribute"/>。
/// </remarks>
[DisplayName("电动折盒机")]
[Description("电动折盒机上报接收端的业务管理：报文、死信、设备、凭据、生产数据、故障处置与监控维护。")]
[Menu(120, true, Icon = "fa-industry")]
public class EfbmArea : AreaBase
{
    /// <summary>注册业务区域，区域名为 Efbm。</summary>
    public EfbmArea() : base(nameof(EfbmArea).TrimEnd("Area").ToString()) { }
}
