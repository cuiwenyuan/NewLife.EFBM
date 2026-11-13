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

**P0 契约已冻结，P1 工程骨架与 P2 接收链路已交付。** 4 个工程（`EFBM.Web` / `EFBM.Data` / `EFBM.Core` / `EFBM.Tests`）统一 `net10.0`；`Model.xml` 十一张实体由 `xcode` 生成；契约层、字段字典、`EventKeyBuilder`、`RedisEventQueue`（惰性接入）、内外网配置与启动自检已落地；**P2 已实现单端点 `POST /api/events`、三模式按设备鉴权、`JsonDocument` 严格解析 + 宽容字段、一次 `XADD` 即 200、Redis 故障本地兜底与回灌**。字段字典按示例当前版本冻结为 `ParseVersion=1.0`；架构设计 D-11 已决议为 4 工程。`EFBM.Tests` 使用标准 `ProjectReference`（测试运行的是真实 DLL）；`dotnet build` 0 警告 0 错误，`dotnet test` 494 项通过。**P3 消费者与全量落库已完成**：Redis 模式下 `PersistConsumerService` 异步把 Stream 消息落 `DeviceEventRaw` / `WorkOrder` / `ProductionRecord` / `DeviceStatusLog` / `FaultRecord`，`EventKey` 幂等、死信不自动丢弃；纯日志模式无消费者，靠 `--replay` 把历史日志补录进库。**P4 安全裁剪（按各组最小已确认 ID 的 `XTRIM MINID`）已完成**：`StreamTrimHostedService` 周期驱动，绝不删除未确认消息，Redis < 6.2 自动停跑。**P4 后台已完成（全部魔方原生）**：报文 / 死信 / 字典 / 设备 / 凭据 / 生产记录 / 工单 / 故障 / 状态流水共 9 个页面继承魔方的 `ReadOnlyEntityController` / `EntityController`，列表 / 详情 / 搜索 / 分页 / 导出由内置模板渲染；另加 4 个手写页面——「数据维护」负责重放（单条 / 按范围 / 死信）、标记、导出与凭凭证清理，重放生成新 `MessageId` + `replayOf` 且不覆盖已落库数据，清理只删导出过的那部分；「故障处置」列出未关闭故障并支持手动关闭（`Status=2` + `ClearedAt`，不改设备事实）；「消费组监控」展示 Stream 长度 / 写入速率 / 各组 lag 与 PEL / 最慢组与僵尸组 / 安全裁剪水位；「字段演进」扫描报文里没登记的字段并支持现场补登（`FieldDict` 表，登记不自动建列）。权限走 `[EntityAuthorize]` 的「菜单 × 权限位」，角色在「角色」页维护，「运维」可查可改、「只读」只能看、清理只给管理员；写操作记魔方 `Log`（「日志」页可查），危险操作必须带 `X-Confirm` 确认头。

**P5 已全部完成（查询统计与安全收敛）**：① 统一查询统计 API 8 个端点（`/api/query/devices|orders|production|output|faults|stream`），`X-Query-Key` 定长时间比较、未配密钥时退化为「仅后台登录用户可用」（宁可 503 不裸奔），产量按工单取最大值再加总避免逐件重复计数；② 外网安全基线——暴露面白名单中间件（非白名单 404、`..` 一律拒绝）、可选 HMAC-SHA256 签名、来源 IP 白名单（CIDR / 通配）、公网 Redis 与弱密钥启动直接拒绝；③ 消费组登记（第 12 张表 `ConsumerGroup` + 后台页），组名 `efbm-` 前缀、**禁删禁复用**（重建会从头重放全量）；④ `/health` 六项依赖探测（DB / Redis / 角色 / 消费者 / 各消费组 / 请求日志），任一失败即 `degraded`；⑤ 字段回溯回填（后台页 + `--backfill-fields` 命令行，试运行不写库）；⑥ 跨消费者 PEL 接管，消费者重启不再让消息永久滞留；⑦ SQLite 连接串自动补 `Pooling=True`，落库吞吐从 20 条/秒提到 600+ 条/秒。

**P6 本机部分已完成**：`tools/loadtest`（Node 单文件压测脚本）实测 20 并发 20000 条 → **603 QPS、P99 52ms、0 个 5xx**，2 万条积压 8 秒排空，强杀进程后重启 68 条遗留消息全部接管落库。仍需目标环境验收：MySQL 方言与双库切换、systemd / Nginx / IIS / 宝塔实机部署、外部消费组现场接入、真实设备联调、E2E（二期）。

**v1.0.2026.1113 正式版已发布（2026-11-13）**：P0~P6 全部交付，494 项单测通过；本机验收已补齐 MySQL 8.0.46（空库建表 / 双向切换 / 字段固化闭环）与 Redis 8.10.3（`XTRIM MINID` / `XCLAIM` 命令级验证）；NewLife 全家桶升级最新稳定版（Core 12 / XCode 12.3 / Cube 6.16 / Redis 7）。发布物为 linux-x64 自包含（58MB）与框架依赖（13MB）两种包，随包生成统一启动入口 `start.sh`（`bash tools/release.sh`）。剩余为需目标环境的验收：systemd / Nginx / IIS / 宝塔实机部署、外部消费组现场接入、真实设备联调、明细条数逐条核对、E2E（二期）。功能完成状态以 [Doc/功能清单.md](Doc/功能清单.md) 为唯一持久状态源。

### 本机快速运行（开发）

```bash
dotnet run --project src/EFBM.Web          # Development 默认纯日志模式，凭据引导自动准备 beta-fbox-a01 / beta-secret-2026
curl -s -X POST http://127.0.0.1:5000/api/events -H "Content-Type: application/json" \
  -H "X-App-Key: beta-fbox-a01" -H "X-App-Secret: beta-secret-2026" \
  -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T10:00:00+08:00","data":{"orderId":"WO-001","boxId":"BOX-0001","planQty":1000,"completedQty":1}}'
tail -n 1 src/EFBM.Web/logs/api/api-$(date +%Y%m%d).log
```

启动、验证、`/health` 判读、回放补录、后台页面与排障全流程见 [Doc/运行手册.md](Doc/运行手册.md)；部署走 [Doc/部署文档.md](Doc/部署文档.md)。

---

## 文档索引

| 文档 | 路径 | 内容 |
| --- | --- | --- |
| 需求说明 | [Doc/需求说明.md](Doc/需求说明.md) | WHAT：技术约束、接口契约与 6 类事件、FR-1~FR-28、数据与幂等、后台页面、非功能要求 |
| 开发计划 | [Doc/开发计划.md](Doc/开发计划.md) | HOW 与阶段：§0 开工准备、工程结构、P0~P6、实现要点、风险、交付物、18 条验收用例 |
| 部署文档 | [Doc/部署文档.md](Doc/部署文档.md) | Linux 原生 / 宝塔 / Windows IIS 三条路径、配置清单、验证清单、升级回滚、故障速查 |
| 运行手册 | [Doc/运行手册.md](Doc/运行手册.md) | 启动与凭据引导、三种落点语义、`/health` 判读与冒烟、回放补录、后台页面 / 权限 / 审计、查询 API、安全基线、日志格式与检索、PEL 排查、压测、故障排查 |
| 架构设计 | [Doc/架构设计.md](Doc/架构设计.md) | 分层与组件所在工程、核心流程时序、契约与不变量、36 条设计决策（D-11 决议 4 工程、D-15~D-19 为 P2 决策、D-20~D-21 为 Beta 决策、D-22~D-23 为文本日志决策、D-24~D-27 为 P3 消费落库决策、D-28 为安全裁剪、D-29 为后台接口先于页面、D-30 为宿主中间件顺序、D-31~D-32 为导出与清理、D-33 后台权限选魔方原生、D-34 审计与二次确认、D-35 字段登记表、D-36 自定义页基类）、扩展点、收口记录 |
| 功能清单 | [Doc/功能清单.md](Doc/功能清单.md) | 拆分决策表、133 个功能点的实现 / 测试 / 注释三维状态、统计与待修复项 |
| 上报字段字典 | [Doc/上报字段字典.md](Doc/上报字段字典.md) | 当前版本冻结（`ParseVersion=1.0`）：信封 / `data` 字段矩阵、接收端生成字段、状态字典、`ParseWarning`、演进流程 |
| 竞品分析 | [Doc/竞品分析.md](Doc/竞品分析.md) | 内部系统降级说明 + 可选技术路径对比（WHY NOT）与功能遴选结果 |
| 原始样例 | [Doc/API上报接口数据示例.txt](Doc/API上报接口数据示例.txt) | 设备侧上报示例，已归纳进需求说明 |
| 项目级技能 | [.workbuddy/skills/newlife-efbm/SKILL.md](.workbuddy/skills/newlife-efbm/SKILL.md) | WorkBuddy 侧的项目协作规范与官方资产索引 |

> 约定：需求与计划处于讨论完善阶段，**不标版本号、不写变更记录、不登记决策过程**；改哪条直接改对应段落。Markdown 统一 UTF-8 无 BOM、简体中文，标准产出放 `Doc/`。

---

## AI 协作声明

> 官方 `dev-loop` agent 会优先读取本节，编译 / 测试命令以此为准。

- **项目类型**：`net10.0` / ASP.NET Core 后端 + NewLife.Cube 管理后台（含内嵌后台消费者），无独立前端工程。
- **编译命令**：`dotnet build EFBM.sln`（解决方案根目录，包含 4 个工程；当前 0 警告 0 错误）。
- **测试命令**：`dotnet test EFBM.sln`（xUnit，当前 494 项通过，全部带中文 `DisplayName`；集成测试用 XCode + SQLite，P3 消费落库集成测试覆盖幂等 / 死信 / 全量留存，P4 安全裁剪纯算法单测覆盖，后台服务集成测试覆盖过滤 / 分页 / 标记 / 重放语义，`AdminAreaTests` 守住控制器结构 / 菜单 / 权限标注，`QueueMetricsTests` 覆盖最慢组与僵尸组判定和写入速率，`FaultDisposalServiceTests` 覆盖手动关闭语义，`FieldEvolutionServiceTests` 覆盖字段扫描与登记校验，双数据库矩阵与故障注入按开发计划 §7）。
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
bash tools/release.sh          # 双包 + start.sh，版本号按 NewLife 规则自动生成
# 或打 tag 让 GitHub Actions 自动发版（产物发到 GitHub Release）：
git tag v1.0.2026.1201 && git push origin v1.0.2026.1201
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
2. ✅ **P1 骨架已完成**：`net10.0` 4 工程、`Model.xml` 十一张实体与生成实体、契约层 / 字段字典 / 内外网配置 / 启动自检、`RedisEventQueue` 惰性接入。
3. ✅ **P2 接收链路已完成**：单端点 `POST /api/events`、三模式按设备鉴权、`JsonDocument` 严格解析 + `data` JSON 树承载、`MessageId` / `EventKey` 生成、一次 `XADD` 即 200、Redis 故障本地兜底与回灌、双失败 503。
4. ✅ **Beta 最小版本已就绪**：SQLite 开箱自动建表、配置驱动凭据引导、`/health` 队列可观测、一键冒烟脚本；验收标准是「200 且 `redis.streamLength` 增长」，消费入库不在本版范围。
5. ✅ **P3 消费者与全量落库已完成**：`PersistConsumerService`（`IHostedService`）常驻消费、批量取 + 显式 `XACK`、进程内重试计数 + 超限死信、`EventHandlerRegistry` + 7 个 handler、逐件明细全量落库、`EventKey` 幂等 + `RepeatCount`、设备自动登记；纯日志模式补录由 `--replay` 承担。
6. ✅ **P4 安全裁剪与魔方原生后台已完成**：`StreamTrimHostedService` 按各组最小已确认 ID 执行 `XTRIM MINID`（绝不删未确认消息）；后台 9 个实体页面 + 数据维护 / 故障处置 / 消费组监控 / 字段演进 4 个手写页面，权限走魔方「菜单 × 权限位」，重放 / 导出 / 凭凭证清理 / 关闭故障 / 登记新字段齐备。P4 已无剩余项，下一步 P5 部署与 P6 压测。
7. ✅ **本机验收已补齐（2026-11-13）**：MySQL 8.0.46 空库建表与双向切换、字段固化全闭环、Redis 8.10.3 命令级验证（`XTRIM MINID` / `XCLAIM`）；多组广播 / 组内分摊 / PEL 重投 / 裁剪安全已由真实 Redis 集成测试覆盖。**v1.0.2026.1113 正式版已发布**，剩余为需目标环境的实机验收。
8. 状态随进度同步回 [Doc/功能清单.md](Doc/功能清单.md)。
