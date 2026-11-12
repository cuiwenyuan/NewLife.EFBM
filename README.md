# EFBM · 电动折盒机上报 API 后端

> 电动折盒机主动上报事件的纯接收端：快速应答、一次写入多系统共享的上报流、异步全量落库、Cube 后台可查可管。

项目标识 `EFBM` = **E**lectric **F**olding **B**ox **M**achine。设备编号示例 `FBOX-A01` 属于设备侧报文格式，与项目名无关。

---

## 亮点

- **一条都不能丢**：设备失败不重发，接收链路零数据库 IO（只有一次文件追加）；`XADD` 成功即 200，队列不可用时降级到本地表或文本日志，落库由 `XACK` + 幂等 + 死信 + 兜底四层保障闭环。
- **多系统共享同一条流**：`RedisStream<T>` 多消费组各拿一份全量，组内竞争分摊、组间互不阻塞；外部系统登记自己的消费组即可订阅。
- **数据库是长期真相源**：原始报文 100% 全量、逐件完成明细全量，Stream 只是有限窗口分发总线；窗口外按时间范围从库重放并广播。
- **字段持续新增不用改接口**：统一单端点 + JSON 树承载 `data` + `ExtJson` 兜底 + 字段字典发现；需要查询时再固化，并从原始报文回溯回填历史数据。
- **内外网都能上线**：内网 HTTP 可选，外网强制 HTTPS + 按设备凭据 + Redis 不对公网；配置不符启动自检直接失败。
- **NewLife 全家桶**：`NewLife.Cube` 宿主与后台、`NewLife.XCode` 持久化（SQLite / MySQL 零代码切换）、`NewLife.Redis` 分发，统一 `net10.0`。
- **可人工兜底**：Cube 后台提供原始报文查看 / 标记 / 重放 / 导出 / 手动清理（强制先导出）、死信重放、字段字典、队列与消费组监控；系统侧无自动清理任务。
- **三条部署路径**：Linux 原生 systemd + Nginx、宝塔面板（进程守护 + 反代）、Windows IIS（含应用池常驻设置）。

---

## 当前状态

**P0 契约已冻结，P1 工程骨架与 P2 接收链路已交付。** 4 个工程（`EFBM.Web` / `EFBM.Data` / `EFBM.Core` / `EFBM.Tests`）统一 `net10.0`；`Model.xml` 十张实体由 `xcode` 生成；契约层、字段字典、`EventKeyBuilder`、`RedisEventQueue`（惰性接入）、内外网配置与启动自检已落地；**P2 已实现单端点 `POST /api/events`、三模式按设备鉴权、`JsonDocument` 严格解析 + 宽容字段、一次 `XADD` 即 200、Redis 故障本地兜底与回灌**。字段字典按示例当前版本冻结为 `ParseVersion=1.0`；架构设计 D-11 已决议为 4 工程。`EFBM.Tests` 使用标准 `ProjectReference`（测试运行的是真实 DLL）；`dotnet build` 0 警告 0 错误，`dotnet test` 294 项通过。**P3 消费者与全量落库已完成**：Redis 模式下 `PersistConsumerService` 异步把 Stream 消息落 `DeviceEventRaw` / `WorkOrder` / `ProductionRecord` / `DeviceStatusLog` / `FaultRecord`，`EventKey` 幂等、死信不自动丢弃；纯日志模式无消费者，靠 `--replay` 把历史日志补录进库。**P4 安全裁剪（按各组最小已确认 ID 的 `XTRIM MINID`）已完成**：`StreamTrimHostedService` 周期驱动，绝不删除未确认消息，Redis < 6.2 自动停跑。**P4 后台接口已完成**：`api/admin/events`（组合过滤 + 分页、原文详情、手动标记、单条与按范围批量重放、CSV 导出、凭导出凭证清理）、`api/admin/deadletters`（列表 / 详情 / 重放），重放生成新 `MessageId` + `replayOf` 且不覆盖已落库数据，清理只删导出过的那部分；接口走 Cube 登录态，未认证返回 401 信封；后台按管理员 / 运维 / 只读三角色矩阵授权，权限不够返回 403（清理只给管理员，运维最多导出与重放）。

**尚未实现**：Cube 后台页面（报文列表 / 原文查看 / 死信页 / 状态字典维护 / 消费组监控，P4 进行中；后端接口与三角色权限矩阵已就绪）、敏感操作二次确认与审计查询（AUTH-3b）、部署联调（P5~P6）。P3 消费者落库、P4 安全裁剪与后台接口（检索 / 重放 / 导出 / 清理）均已完成——Redis 模式下返回 200 的数据真正入库、Stream 按最小已确认 ID 安全收缩、后台可按条件检索并重放、清理必须先导出；但**页面 / 压测未做，仍未到生产上线标准**。真实 Redis ≥ 6.2 / MySQL 环境下的运行时验收与 P99 压测仍待补。功能完成状态以 [Doc/功能清单.md](Doc/功能清单.md) 为唯一持久状态源，请勿据文档进度判断系统可用。

### Beta 最小版本（当前可交付）

先打通「设备上报 → **每一条原文都落盘**」，Redis 与消费入库都可以后补。为此交付四件事：

1. **文本请求日志（主能力）**：每条 `POST /api/events` 一行 JSON 追加到 `logs/api/api-yyyyMMdd.log`，含请求体原文、`deviceId` / `event` / `eventKey` / 判定结果；按天滚动、超限切分、保留期可配、`fsync` 可配；**400 / 401 / 403 也照记**，且绝不写 AppSecret 与 Authorization。
2. **SQLite 开箱可用**：引入 `XCode.SQLite`，`EFBM.db` 首次启动自动建表，不再依赖数据库服务器。
3. **Beta 凭据引导**：`EFBM:Beta:Enabled` 按配置幂等补齐一台设备与一套凭据，解决 P4 后台发放页面未上线时全部 401 的问题；仅内网允许，外网启用直接启动失败。
4. **Redis 变成可选项**：`EFBM:RedisEnabled=false` 即纯日志模式（Development 默认），不连 Redis、不写兜底表；打开后仍走 `XADD`，失败降级兜底表。

```bash
dotnet run --project src/EFBM.Web          # Development 默认纯日志模式，自动准备 beta-fbox-a01 / beta-secret-2026
curl -s -X POST http://127.0.0.1:5000/api/events -H "Content-Type: application/json" \
  -H "X-App-Key: beta-fbox-a01" -H "X-App-Secret: beta-secret-2026" \
  -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T10:00:00+08:00","data":{"orderId":"WO-001","boxId":"BOX-0001","planQty":1000,"completedQty":1}}'
tail -n 1 src/EFBM.Web/logs/api/api-$(date +%Y%m%d).log
```

逐步验收见 [Doc/Beta运行手册.md](Doc/Beta运行手册.md)。

**已实测**（Windows + .NET 10.0.11 + SQLite；Redis 5.0.14.1 用于可选模式）：纯日志模式下 `POST` 返回 `200 / accepted-by-log / logged=true`，日志新增一行且 `raw` 与原文逐字一致；非法 JSON 与缺凭据同样留痕；`/health` 的 `requestLog.written` 递增。切到 `RedisEnabled=true` 后同一报文返回 `accepted / queued=true`，`XLEN` 增长、`efbm-persist` 自动建组。MySQL 与 Redis ≥ 6.2 专属命令仍未验证。

---

## 文档索引

| 文档 | 路径 | 内容 |
| --- | --- | --- |
| 需求说明 | [需求说明.md](需求说明.md) | WHAT：技术约束、接口契约与 6 类事件、FR-1~FR-28、数据与幂等、后台页面、非功能要求 |
| 开发计划 | [开发计划.md](开发计划.md) | HOW 与阶段：§0 开工准备、工程结构、P0~P6、实现要点、风险、交付物、18 条验收用例 |
| 部署文档 | [部署文档.md](部署文档.md) | Linux 原生 / 宝塔 / Windows IIS 三条路径、配置清单、验证清单、升级回滚、故障速查 |
| Beta 运行手册 | [Doc/Beta运行手册.md](Doc/Beta运行手册.md) | 最小版本范围、Redis 准备、启动与凭据引导、`/health` 与冒烟验收、故障排查、已知限制 |
| 架构设计 | [Doc/架构设计.md](Doc/架构设计.md) | 分层与组件所在工程、核心流程时序、契约与不变量、30 条设计决策（D-11 决议 4 工程、D-15~D-19 为 P2 决策、D-20~D-21 为 Beta 决策、D-22~D-23 为文本日志决策、D-24~D-27 为 P3 消费落库决策、D-28 为安全裁剪、D-29 为后台接口先于页面、D-30 为宿主中间件顺序）、扩展点、收口记录 |
| 功能清单 | [Doc/功能清单.md](Doc/功能清单.md) | 拆分决策表、130 个功能点的实现 / 测试 / 注释三维状态、统计与待修复项 |
| 上报字段字典 | [Doc/上报字段字典.md](Doc/上报字段字典.md) | 当前版本冻结（`ParseVersion=1.0`）：信封 / `data` 字段矩阵、接收端生成字段、状态字典、`ParseWarning`、演进流程 |
| 竞品分析 | [Doc/竞品分析.md](Doc/竞品分析.md) | 内部系统降级说明 + 可选技术路径对比（WHY NOT）与功能遴选结果 |
| 原始样例 | [API上报接口数据示例.txt](API上报接口数据示例.txt) | 设备侧上报示例，已归纳进需求说明 |
| 项目级技能 | [.workbuddy/skills/newlife-efbm/SKILL.md](.workbuddy/skills/newlife-efbm/SKILL.md) | WorkBuddy 侧的项目协作规范与官方资产索引 |

> 约定：需求与计划处于讨论完善阶段，**不标版本号、不写变更记录、不登记决策过程**；改哪条直接改对应段落。Markdown 统一 UTF-8 无 BOM、简体中文，标准产出放 `Doc/`。

---

## AI 协作声明

> 官方 `dev-loop` agent 会优先读取本节，编译 / 测试命令以此为准。

- **项目类型**：`net10.0` / ASP.NET Core 后端 + NewLife.Cube 管理后台（含内嵌后台消费者），无独立前端工程。
- **编译命令**：`dotnet build EFBM.sln`（解决方案根目录，包含 4 个工程；当前 0 警告 0 错误）。
- **测试命令**：`dotnet test EFBM.sln`（xUnit，当前 294 项通过，全部带中文 `DisplayName`；集成测试用 XCode + SQLite，P3 消费落库集成测试覆盖幂等 / 死信 / 全量留存，P4 安全裁剪纯算法单测覆盖，P4 后台接口集成测试覆盖过滤 / 分页 / 标记 / 重放语义，双数据库矩阵与故障注入按开发计划 §7）。
- **核心工程**（4 个，按架构设计 D-11 决议收敛；Queue / Services / Consumers 是 `EFBM.Web` 内的目录分层）：

| 工程 | 职责 |
| --- | --- |
| `EFBM.Core` | 契约与纯逻辑：事件常量、信封、Stream 消息体、字段字典、幂等键、`EventEnvelopeParser` / `RequestAuthenticator` / `EventIngestService` 及队列 / 凭据 / 兜底接口（不依赖 Web / DB） |
| `EFBM.Data` | XCode 数据层：官方模板产出 `Model.xml`、`xcode` 生成的实体与 `.Biz.cs` 扩展 |
| `EFBM.Web` | 唯一宿主 NewLife.Cube：`Controllers/` 上报端点与后台页、`Queue/` Stream 与惰性建组、`Services/` 凭据缓存与兜底回灌、`Consumers/` 落库组与事件 handler、`IHostedService`、`/health` |
| `EFBM.Tests` | 单元测试 + 双数据库 / 多消费组 / 字段扩展 / 队列故障注入测试 |

- **代码规范**：遵循 NewLife 官方全局指令（`String` / `Int32` 等 .NET 正式名、C# 14、file-scoped namespace、XML 注释、防御性注释禁止删除、内置工具优先、Conventional Commits 提交信息）。
- **文档与流程**：改动先过变更路由（小改直改 / 中改回流文档 / 大改先谈方案）；行为或需求变化与代码同批更新需求说明与功能清单；交付前必须编译与测试通过。

---

## NewLife 官方 Skills（已安装）

| 项 | 状态 |
| --- | --- |
| 上游仓库 | `https://github.com/NewLifeX/NewLife.Skills.git`；本机克隆在 `%USERPROFILE%\.workbuddy\vendor\NewLife.Skills` |
| 全局 Copilot 资产 | 已由官方脚本 `scripts/install-copilot-assets.ps1` 安装到 `%USERPROFILE%\.copilot\`：10 skills + 8 agents + 3 专用指令 + 1 全局指令（共 22 项），manifest 与逐文件哈希校验通过 |
| 官方一致性检查 | `scripts/verify-assets.ps1` 返回 `OK: all assets consistent.` |
| WorkBuddy 项目级技能 | `.workbuddy/skills/newlife-efbm/SKILL.md`，提供加载标注、触发检查、硬约束摘要、项目共识与文档索引 |
| VS Code Agent Plugin | 官方推荐方式需在 GUI 完成：扩展视图 `Ctrl+Shift+X` 搜 `@agentPlugins` → 安装 **NewLife Skills**，或在 Copilot Chat 输入 `/plugin install`；要求 VS Code ≥1.96 + Copilot Chat，装完重启 VS Code。本机 VS Code 1.133.0 已满足版本要求 |
| CLI 通道（可选，等价，勿重复安装） | `npx skills add https://github.com/NewLifeX/NewLife.Skills.git --list`；本机 Node 22 已具备 |

更新 / 校验 / 卸载（PowerShell，在上游仓库目录）：

```powershell
.\scripts\install-copilot-assets.ps1
.\scripts\verify-assets.ps1
.\scripts\install-copilot-assets.ps1 -Uninstall
```

按官方全局指令，**每次回答开头第一行**输出加载标注，便于验证资产是否生效：

```
> 📋 **生效**: instructions=[xcode,development] | skills=[development-process] | agent=default
```

---

## 快速开始

> 工程骨架与 P2 接收链路已建立，构建 / 测试命令可直接运行；模板与 `xcodetool` 本机已安装，换机器时才需重新安装。`dotnet build` 会自动使用根目录 `EFBM.sln`。
>
> ⚠️ Windows 开发机若开启 **Smart App Control**，新编译的未签名 `EFBM.*.dll` 会被应用程序控制策略阻止加载（`0x800711C7`），表现为 `dotnet build` 正常而 `dotnet test` 失败。在「Windows 安全中心 → 应用和浏览器控制 → 智能应用控制设置」关闭即可（本机已关闭，该 Windows 版本开关可逆）；Defender 排除项与 `Unblock-File` 对 SAC 无效。

```bash
# 工具（本机已安装：NewLife.Templates 1.1.2026.207、xcodetool 11.25.2026.1109-beta1547）
dotnet new install NewLife.Templates
dotnet tool install xcodetool -g --prerelease

# 生成实体：官方模板产出 Model.xml，工具名 xcodetool、命令是 xcode
# （.cs 可覆盖，.Biz.cs 不覆盖）
xcode src/EFBM.Data/Model.xml

# 构建与测试
dotnet build
dotnet test

# 发布（部署细节见《部署文档.md》§2.1）
dotnet publish src/EFBM.Web/EFBM.Web.csproj -c Release -o ./publish/efbm
```

`cube` 模板默认 `net10.0`，`xcode` 模板默认 `netstandard2.0`，由 `Directory.Build.props` 统一 TFM；模板自带的演示 Areas / 服务在 P1 精简。

---

## 关键约束速览

| 项 | 约定 |
| --- | --- |
| 端点 | 唯一 `POST /api/events`，按 `event` 分发，无版本号 |
| 认证 | 匿名 / Basic / AppKey+AppSecret 三模式；凭据按设备发放，接收层校验绑定 `deviceId`；外网禁匿名 |
| 幂等 | `EventKey = SHA1(event|deviceId|timestamp)`（竖线拼接原始字符串），不含 `data`；`MessageId` 只做追踪 |
| 分发 | Stream `efbm:events`；本系统组 `efbm-persist`，外部组 `efbm-<系统名>`；Redis ≥ 6.2（建议 7.x） |
| 确认 | 业务成功才显式 `XACK`；禁止框架 `MaxRetry` 自动丢弃与按长度 / 时间自动裁剪 |
| 保存 | 原始报文与逐件明细全量、按月分表、**无自动清理任务**；清理前强制先导出归档 |
| 数据库 | 业务库 `EFBM`、权限库 `Membership` 同实例分库；开发 SQLite / 生产 MySQL，切换零代码 |
| 目标框架 | 全部工程 `net10.0`；服务器 ASP.NET Core Runtime 10.x（IIS 需同版本 Hosting Bundle） |
| 部署 | 内网 / 外网均支持；进程必须常驻（消费者内嵌），IIS 须关闭应用池回收 |

---

## 下一步

1. ✅ **P0 已完成**：接口契约、Stream 消息体、消费组命名、《上报字段字典》初版（`ParseVersion=1.0`）已冻结；D-11 决议收敛为 4 工程。
2. ✅ **P1 骨架已完成**：`net10.0` 4 工程、`Model.xml` 十张实体与生成实体、契约层 / 字段字典 / 内外网配置 / 启动自检、`RedisEventQueue` 惰性接入。
3. ✅ **P2 接收链路已完成**：单端点 `POST /api/events`、三模式按设备鉴权、`JsonDocument` 严格解析 + `data` JSON 树承载、`MessageId` / `EventKey` 生成、一次 `XADD` 即 200、Redis 故障本地兜底与回灌、双失败 503。
4. ✅ **Beta 最小版本已就绪**：SQLite 开箱自动建表、配置驱动凭据引导、`/health` 队列可观测、一键冒烟脚本；验收标准是「200 且 `redis.streamLength` 增长」，消费入库不在本版范围。
5. ✅ **P3 消费者与全量落库已完成**：`PersistConsumerService`（`IHostedService`）常驻消费、批量取 + 显式 `XACK`、进程内重试计数 + 超限死信、`EventHandlerRegistry` + 7 个 handler、逐件明细全量落库、`EventKey` 幂等 + `RepeatCount`、设备自动登记；纯日志模式补录由 `--replay` 承担。
6. ✅ **P4 安全裁剪与后台接口已完成**：`StreamTrimHostedService` 按各组最小已确认 ID 执行 `XTRIM MINID`（绝不删未确认消息）；`api/admin/events` 与 `api/admin/deadletters` 提供检索、原文、手动标记、单条重放（新 `MessageId` + `replayOf`，不覆盖已落库数据）。P4 剩余：Cube 页面、敏感操作二次确认与审计查询。
7. 需在具备 **Redis ≥ 6.2 与 MySQL** 的环境补做完整运行时验收（多组广播 / 组内分摊 / PEL 重投 / 裁剪安全性 / P99）；本机已用 SQLite + 便携版 Redis 验证「接收 → 入流」与「降级 → 回灌」，MySQL 与 6.2 专属命令仍待目标环境补跑。
8. 状态随进度同步回 [Doc/功能清单.md](Doc/功能清单.md)。
