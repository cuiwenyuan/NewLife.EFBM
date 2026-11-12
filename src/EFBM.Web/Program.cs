using EFBM.Core;
using EFBM.Web.Consumers;
using EFBM.Web.Queue;
using EFBM.Web.Services;
using NewLife.Cube;
using NewLife.Log;
using XCode;
using XCode.DataAccessLayer;

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

// 显式登记数据库连接：XCode 默认从 appsettings 自动发现连接，但当进程内容根不是应用目录
// （例如 --replay 在别处目录执行）时可能漏配，导致数据被写进回退库而静默丢失。这里从配置
// 显式登记 EFBM / Membership，保证接收链路与回放工具都绑定到真实库（数据完整性优先）。
RegisterConnection("EFBM", builder.Configuration.GetConnectionString("EFBM"));
RegisterConnection("Membership", builder.Configuration.GetConnectionString("Membership"));

// 关闭 SQL 日志；Migration=On 由 Config/XCode.config 控制，字段固化时自动建表 / 加列
var xset = XCodeSetting.Current;
if (xset.IsNew)
{
    xset.ShowSQL = false;
    xset.Save();
}

services.AddControllersWithViews();
services.AddCube();                 // 权限、菜单、实体控制器扫描
services.AddEFBMQueue();            // 共享上报流（惰性构造，首次写入才连 Redis 建组）
services.AddEFBMReceive();          // P2：解析 / 三模式鉴权 / 单端点编排 / 本地兜底回灌
services.AddEFBMConsumers();        // P3：事件处理器注册表 / 消费落库编排 / 常驻落库消费者
services.AddEFBMAdmin();            // P4：后台原始报文检索 / 死信查询 / 重放与手动标记

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

// 显式三段：路由 → 认证 → 授权，必须早于 UseCube 注册。
// Cube 的 UseCube 把「路由 + 端点映射」放在同一个调用里（内部 UseRouter 会 UseRouting + UseEndpoints），
// 因此注册在它之后的 UseAuthorization 永远不会执行，后台接口的 [Authorize] 会退化成
// 「endpoint 带授权元数据却没有授权中间件」的 500。先 UseRouting 让 Cube 复用同一条路由管道
// （它检测 __EndpointRouteBuilder），授权中间件才能读到端点元数据；Cube 内部的 UseAuthentication
// 变成第二次调用，对已认证的请求是幂等的。
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseCube(app.Environment);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=CubeHome}/{action=Index}/{id?}");

// --replay <文件或目录>：把 Beta 文本日志里的数据补录进库，跑完即退出（不开 Web 服务）
// 纯日志模式（RedisEnabled=false）服务器上没有消费者，这是入库的唯一通道；幂等由 EventKey 兜住
var replayIndex = Array.FindIndex(args, a => String.Equals(a, "--replay", StringComparison.OrdinalIgnoreCase));
if (replayIndex >= 0)
{
    var replayPath = args.Length > replayIndex + 1 ? args[replayIndex + 1] : String.Empty;
    await LogReplayRunner.RunAsync(app.Services, replayPath);
    return;
}

app.Run();

/// <summary>把配置里的连接串显式登记进 XCode DAL；已存在则覆盖，幂等。</summary>
/// <param name="name">连接名，须与实体 <c>ConnName</c> 一致（EFBM / Membership）。</param>
/// <param name="connectionString">连接串；含 <c>Provider=MySql</c> 视为 MySQL，否则 SQLite。</param>
static void RegisterConnection(String name, String connectionString)
{
    if (String.IsNullOrWhiteSpace(connectionString)) return;
    var dbType = connectionString.Contains("Provider=MySql", StringComparison.OrdinalIgnoreCase) ? "MySql" : "SQLite";
    DAL.AddConnStr(name, connectionString, null, dbType);
}
