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
RegisterConnection("EFBM", builder.Configuration.GetConnectionString("EFBM"), efbm.AutoMigrate);
RegisterConnection("Membership", builder.Configuration.GetConnectionString("Membership"), efbm.AutoMigrate);

// 建表是异步的（见 WaitForTables 注释）：等业务库的表真正可查再放后台服务跑，
// 否则兜底回灌 / 落库消费者的首轮必然撞「Table doesn't exist」
if (efbm.AutoMigrate) WaitForTables(EfbmDefaults.ConnectionName, TimeSpan.FromSeconds(8));

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

// 关掉魔方的「在线用户」请求级统计：设备上报是机器流量，不带 Cookie 的匿名请求每次都被当成新会话，
// RunTimeMiddleware 会为每条上报插一行 UserOnline，与审计日志 / 业务落库争同一个库的写锁。
// SQLite 部署实测会抛「attempt to write a readonly database」，把上报请求打成 500（等于丢数据）。
// 必须在 UseCube 之前设置：中间件按当前值判定。档位 0/1/2 见 EfbmOptions.UserOnlineMode。
CubeSetting.Current.EnableUserOnline = efbm.UserOnlineMode;

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/CubeHome/Error");

// 后台完全使用魔方自带的页面与权限体系：
// 每个实体的控制器在 Areas/Admin/Controllers 下继承 ReadOnlyEntityController / EntityController，
// 列表、详情、导出由魔方内置模板渲染；权限走 EntityAuthorizeAttribute（菜单 × 权限位），
// 角色在「系统管理 → 角色」里按权限项勾选，用户角色在「用户」页指派。
// 静态文件中间件只服务魔方自带的前端资源，没有任何自建页面。

// 外网暴露面收敛（AUTH-4b）：只放行上报、查询、健康检查与后台入口，其余一律 404。
// 必须排在静态文件与路由之前，被拦下的路径不会进入任何后续管道。
if (efbm.IsInternet() && efbm.ExposureAllowlistEnabled)
{
    app.UseMiddleware<EFBM.Web.Security.ExposureAllowlistMiddleware>();
    XTrace.WriteLine("外网暴露面白名单已启用：{0}", String.Join(" / ", EFBM.Web.Security.ExposureAllowlistMiddleware.Describe()));
}

app.UseStaticFiles();

// 显式三段：路由 → 认证 → 授权，必须早于 UseCube 注册。
// Cube 的 UseCube 把「路由 + 端点映射」放在同一个调用里（内部 UseRouter 会 UseRouting + UseEndpoints），
// 注册在它之后的中间件永远不会执行，所以先 UseRouting 让 Cube 复用同一条路由管道（它检测 __EndpointRouteBuilder）。
// 后台页面的登录与权限由魔方的 EntityAuthorize 授权过滤器判定（它在端点执行前跑，此时登录用户已还原），
// 未登录跳登录页、无权限提示无权限，与魔方其它页面一致。
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseCube(app.Environment);

// 菜单在 UseCube 里扫描控制器生成，角色授权必须排在它后面：把「运维 / 只读」两个内置角色
// 的菜单权限补齐（管理员是系统角色，魔方已自动授予全部菜单）。
if (efbm.SeedAdminRoles) AdminRoleSeed.Seed();

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

// --backfill-fields [天数]：把已固化字段的历史值从原文补进 ExtJson，跑完即退出（FLD-2c）
// 设备不重发历史报文，这是「新增字段只有从今天起有数据」的唯一补救手段
var backfillIndex = Array.FindIndex(args, a => String.Equals(a, "--backfill-fields", StringComparison.OrdinalIgnoreCase));
if (backfillIndex >= 0)
{
    var days = 0;
    if (args.Length > backfillIndex + 1) Int32.TryParse(args[backfillIndex + 1], out days);

    await FieldBackfillRunner.RunAsync(app.Services, days);
    return;
}

app.Run();

/// <summary>把配置里的连接串显式登记进 XCode DAL；已存在则覆盖，幂等。</summary>
/// <param name="name">连接名，须与实体 <c>ConnName</c> 一致（EFBM / Membership）。</param>
/// <param name="connectionString">连接串；含 <c>Provider=MySql</c> 视为 MySQL，否则 SQLite。</param>
static void RegisterConnection(String name, String connectionString, Boolean autoMigrate)
{
    if (String.IsNullOrWhiteSpace(connectionString)) return;

    // SQLite 默认不开连接池：每次开连接约 8ms，一条事件 5~6 条语句 → 落库吞吐只有 20 条/秒。
    // 在这里统一补齐 Pooling=True（用户显式写了就不覆盖），避免某个部署文件漏配导致静默退化。
    connectionString = ConnectionStringTuning.Normalize(connectionString);

    var dbType = connectionString.Contains("Provider=MySql", StringComparison.OrdinalIgnoreCase) ? "MySql" : "SQLite";
    DAL.AddConnStr(name, connectionString, null, dbType);

    // 启动即建表：XCode 的反向工程是惰性的（首次访问实体才检查表结构），而兜底回灌服务与落库
    // 消费者在启动后几十毫秒内就开始跑 SQL。空库首次部署会在建表之前撞「Table doesn't exist」，
    // 兜底扫描直接失败。SQLite 老库因历史表已存在一直没暴露，换 MySQL 空库后 100% 复现。
    //
    // 必须用 InitConnection 而不是 CheckTables：实测 CheckTables 只是把建表任务排进去就返回
    // （日志显示 194ms「完成」，但 MySQL 的 create_time 记录表实际建在 1.5 秒之后），
    // 期间后台服务查表已经失败。InitConnection 会同步完成反向工程检查+建表，返回即可查。
    // 库不可达时这里抛异常让启动失败——这是刻意的：连不上库就不该假装正常。
    if (!autoMigrate) return;

    var sw = System.Diagnostics.Stopwatch.StartNew();

    // EFBM 是我们自己的模型，主动触发反向工程；Membership 归 Cube 管，只做结构检查不越权初始化数据
    if (String.Equals(name, EfbmDefaults.ConnectionName, StringComparison.OrdinalIgnoreCase))
        InitEntities(name);
    else
        DAL.Create(name).CheckTables();

    XTrace.WriteLine("启动建表完成：{0}（provider={1}，{2}ms）", name, dbType, sw.ElapsedMilliseconds);
}

/// <summary>主动初始化指定连接下的全部实体，触发反向工程建表。</summary>
/// <param name="name">连接名。</param>
/// <remarks>
/// 为什么不能只调 <c>EntityFactory.InitConnection</c> / <c>DAL.CheckTables</c>：这两个方法都依赖
/// 「实体已注册」，而 XCode 的实体是在<b>类型被首次访问</b>时才由静态构造函数注册的。启动阶段
/// 没有任何代码引用过实体类，于是 InitConnection 拿到的实体清单是空的——它老老实实返回「完成」，
/// 却一张表都没建。空库实测：等 30 秒表依然不存在，直到兜底回灌服务第一次查询触发实体静态构造。</remarks>
static void InitEntities(String name)
{
    var assembly = typeof(EFBM.Data.Entities.Device).Assembly;
    var count = 0;

    foreach (var type in assembly.GetTypes())
    {
        if (type.IsAbstract || type.IsInterface || !typeof(IEntity).IsAssignableFrom(type)) continue;

        EntityFactory.InitEntity(type);
        count++;
    }

    XTrace.WriteLine("实体初始化：{0} 个（连接 {1}）", count, name);
}

/// <summary>等到连接下所有表真正可查，避免后台服务撞上还在建的窗口。</summary>
/// <param name="name">连接名。</param>
/// <param name="timeout">最长等待时间。</param>
/// <remarks>
/// 为什么光有 <c>CheckTables</c> / <c>InitConnection</c> 不够：XCode 的表结构准备是<b>异步</b>的
/// （<c>DAL.Tables</c> 的文档原文就是「异步缓存延迟 1 秒」），空库实测 initConnection 在 362ms
/// 就返回「完成」，但 MySQL 的 <c>create_time</c> 记录 12 张表实际建在 1.5 秒之后。这中间
/// 兜底回灌服务已经在查 <c>LocalQueueFallback</c>，撞上「Table doesn't exist」。
///
/// 探测用 <c>where 1=0</c> 而不是 <c>count(*)</c>：不扫数据，百万级表也是毫秒返回。
/// 表名取 <see cref="IDataTable.TableName"/>（数据库实际表名，已按 NameFormat 转换），
/// 不能用模型名——Linux 上的 MySQL 表名区分大小写。
/// </remarks>
static void WaitForTables(String name, TimeSpan timeout)
{
    var dal = DAL.Create(name);
    var sw = System.Diagnostics.Stopwatch.StartNew();

    while (sw.Elapsed < timeout)
    {
        var tables = dal.Tables;
        if (tables != null && tables.Count > 0)
        {
            var pending = 0;
            foreach (var table in tables)
            {
                try
                {
                    dal.Query<Int32>($"select count(*) from {table.TableName} where 1=0");
                }
                catch
                {
                    pending++;
                }
            }

            if (pending == 0)
            {
                if (sw.ElapsedMilliseconds > 50)
                    XTrace.WriteLine("表结构已就绪：{0}（{1} 张表，等待 {2}ms）", name, tables.Count, sw.ElapsedMilliseconds);
                return;
            }
        }

        Thread.Sleep(100);
    }

    XTrace.WriteLine("表结构等待超时（{0}ms）：{1}，继续启动；后续查询可能首批失败并自动重试", timeout.TotalMilliseconds, name);
}
