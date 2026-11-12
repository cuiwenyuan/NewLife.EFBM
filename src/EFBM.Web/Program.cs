using EFBM.Core;
using EFBM.Web.Queue;
using EFBM.Web.Services;
using NewLife.Cube;
using NewLife.Log;
using XCode;

// 📋 生效: instructions=[newlife-global,cube,xcode,development] | skills=[project-architecture,cube-mvc-backend] | agent=default
//!!! 基于 NewLife.Templates 的 cube 模板精简：去掉星尘注册、IoT 节点服务与演示 Areas，仅保留 Cube 宿主骨架。

// 启用控制台日志并拦截全局异常（官方模板做法）
XTrace.UseConsole();

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// 应用配置：内外网共用一套代码，差异只在 appsettings.{Environment}.json
var efbm = builder.Configuration.GetSection(EfbmOptions.SectionName).Get<EfbmOptions>() ?? new EfbmOptions();
services.AddSingleton(efbm);
efbm.ValidateOrThrow();

// 关闭 SQL 日志；Migration=On 由 Config/XCode.config 控制，字段固化时自动建表 / 加列
var xset = XCodeSetting.Current;
if (xset.IsNew)
{
    xset.ShowSQL = false;
    xset.Save();
}

services.AddControllersWithViews();
services.AddCube();                 // 权限、菜单、实体控制器扫描
services.AddEFBMQueue();           // 共享上报流（惰性构造，首次写入才连 Redis 建组）
services.AddEFBMReceive();         // P2：解析 / 三模式鉴权 / 单端点编排 / 本地兜底回灌

var app = builder.Build();

// Beta：按配置补齐引导设备与凭据。后台发放页面属 P4，没有它 Beta 无法联调；
// 仅内网允许启用，外网部署会在 ValidateOrThrow 阶段直接拒绝启动。
if (efbm.Beta?.Enabled == true) BetaSeedService.SeedOrThrow(efbm);

// 启动日志：打印实际 provider、Redis 地址、Stream key、消费组与部署环境，便于核对内外网配置
StartupDiagnostics.Log(app.Configuration, app.Environment, efbm);

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/CubeHome/Error");

app.UseCube(app.Environment);
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=CubeHome}/{action=Index}/{id?}");

app.Run();
