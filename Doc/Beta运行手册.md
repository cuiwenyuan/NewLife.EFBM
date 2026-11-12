# EFBM Beta 运行手册

> 目标：**先让设备真的能上报进来，并且每一条报文都在服务器上留下原文。**
> 本版默认「纯文本日志」模式，**不需要 Redis 就能收数据**；Redis 共享流作为可选增强。消费入库（P3）与回放补录工具已完成，后台页面（P4）另行安排。

---

## 1. 这一版能做什么

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 唯一端点 `POST /api/events` | ✅ | 一条报文一次应答，成功即 200 |
| 三种鉴权（匿名 / Basic / AppKey+AppSecret） | ✅ | 外网禁用匿名；凭据绑定 `deviceId`，串用返回 403 |
| 严格解析 + 宽容字段 | ✅ | 只有非法 JSON、缺 `event` / `deviceId` / `timestamp` 才 400；新增字段、嵌套对象、未知事件一律接收 |
| **文本请求日志（本版主能力）** | ✅ | 每条请求一行 JSON 落到 `logs/api/api-yyyyMMdd.log`，含**请求体原文**，按天滚动、超限切分、可配置保留期 |
| 写入 Redis Stream（可选） | ✅ | `EFBM:RedisEnabled=true` 时 `XADD efbm:events`，落库组 `efbm-persist` 自动建组 |
| Redis 故障时本地兜底 | ✅ | 开启 Redis 且连不上时写 SQLite 表 `LocalQueueFallback`，恢复后每 30 秒回灌 |
| 健康检查 `/health` | ✅ | 直接回答「日志写了多少条、有没有写失败、Redis 通不通、兜底积压多少」 |
| Beta 凭据引导 | ✅ | 配置驱动自动建一台设备 + 一套凭据，后台发放页面未上线也能联调 |
| 消费入库（P3 已完成） | ✅ | 开启 Redis 后，常驻消费者 `PersistConsumerService` 把 Stream 消息落进 `DeviceEventRaw` / `WorkOrder` / `ProductionRecord` / `DeviceStatusLog` / `FaultRecord` 等业务表，重复投递幂等（只 1 行 + `RepeatCount`）；`/health` 的 `consumer` 段可见 `taken/persisted/duplicates/deadLettered` |
| 日志回放补录 `--replay` | ✅ | 纯日志模式没有消费者，用 `dotnet EFBM.Web.dll --replay <日志文件或目录>` 把历史日志补录进库，幂等由 `EventKey` 兜住，可重复跑 |

### 1.1 一次请求的三种落点

设备只看 HTTP 状态码，所以**只要数据没丢就是 200**；到底落在哪里看响应 `message`：

| `message` | `queued` | 数据在哪 | 后续动作 |
| --- | --- | --- | --- |
| `accepted` | `true` | Redis Stream `efbm:events` | 等 P3 消费者落库 |
| `accepted-by-fallback` | `false` | SQLite `LocalQueueFallback` 表 | Redis 恢复后自动回灌 |
| `accepted-by-log` | `false` | **文本日志**（默认模式即此） | 用 `dotnet EFBM.Web.dll --replay <日志>` 补录进库（见 §4.4） |

队列、兜底、日志**三者全部写失败**才返回 503 —— 这种情况说明磁盘也写不进去了，必须立即处理。

### 1.2 本机已验证结论（可直接对照）

开发机（Windows + .NET 10.0.11 + SQLite；Redis 5.0.14.1 用于可选模式）实测：

| 场景 | 结果 |
| --- | --- |
| Development 启动（`RedisEnabled=false`） | Beta 引导自动建 `FBOX-A01` 与 `beta-fbox-a01`；SQLite 自动建表；`/health` 显示 `requestLog.enabled=true` |
| 正常上报 | `HTTP 200`、`message=accepted-by-log`、`logged=true`；日志文件新增一行，`status=200`、`queued=false`、`raw` 与原文逐字一致 |
| 非法 JSON | `HTTP 400`；日志仍有一行，`status=400`、`msg=invalid json`、`raw` 保留带尾随逗号的原文 |
| 缺凭据 | `HTTP 401`；日志行含 `deviceId` / `event`，可用于定位是哪台设备在打 |
| 日志状态 | `/health` 的 `requestLog.written` 随请求递增，`failed=0`，`currentFile` 指向当天文件 |
| 切到 Redis 模式（`RedisEnabled=true`） | 同一报文返回 `message=accepted`、`queued=true`；`XLEN` 增长；日志同样留一行 |

> 未在 6.2 上验证 `XTRIM MINID` / `XAUTOCLAIM`（本机用 5.0 仅验证收发），生产若启用 Redis 请用 ≥ 6.2。

---

## 2. 环境要求

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| .NET | 10.0 SDK / Runtime | 开发用 SDK，部署用 ASP.NET Core Runtime |
| 磁盘 | 任意可写目录 | 文本日志是唯一必需的外部依赖；每天一个文件，单文件默认上限 64MB |
| Redis（可选） | ≥ 6.2 | 仅当 `EFBM:RedisEnabled=true` 时需要；纯日志模式完全不需要 |
| 数据库 | SQLite（默认）或 MySQL | 存设备凭据；`RedisEnabled=true` 时还存兜底记录 |

需要 Redis 时（任选其一）：

```bash
# Linux
sudo apt install redis-server        # 确认版本 ≥ 6.2
redis-server --version

# Docker
docker run -d --name efbm-redis -p 6379:6379 redis:7

# Windows（无 Docker 时）
# 用 Memurai，或 WSL2 里跑 redis-server；不建议用已停更的 3.x 移植版
```

> 本版使用 `RedisDb=5`（与业务缓存隔离），排查时记得加 `-n 5`。
>
> ⚠️ 启用 Redis 时**生产必须开持久化**（`appendonly yes` + `appendfsync everysec`，或 RDB）。Stream 是分发总线而非真相源；纯日志模式没有这个问题，但也没有自动落库。

---

## 3. 启动

### 3.1 开发机（最快路径）

```bash
dotnet run --project src/EFBM.Web
```

默认监听 `http://127.0.0.1:5000`，Development 环境已打开 Beta 引导、关闭 Redis，启动日志会给出实际生效的落点：

```
=== EFBM 启动自检 ===
Redis=127.0.0.1:6379 / db=5 / 启用=False / 版本探测=关闭（P5 启用）
请求日志=True / 目录=logs/api / 保留90天 / 记录原文=True / fsync=True
注意：共享上报流已关闭（EFBM:RedisEnabled=false），数据只进本地兜底与文本日志，queued 恒为 false
Beta 引导完成：deviceId=FBOX-A01 appKey=beta-fbox-a01，可直接用于 POST api/events
```

首条请求之后，日志文件出现在**内容根目录**下：`src/EFBM.Web/logs/api/api-yyyyMMdd.log`（发布后是发布目录下的 `logs/api/`）。

> SQLite 文件按**进程工作目录**解析（`dotnet run` 时是 `bin/Debug/net10.0/`）；日志目录按**内容根目录**解析，两者不同，别找错地方。

### 3.2 切到 Redis 模式

```bash
# 打开共享流并指定地址（环境变量优先级高于 appsettings）
EFBM__RedisEnabled=true EFBM__RedisServer=127.0.0.1:6379 dotnet run --project src/EFBM.Web
```

此时响应变成 `accepted` / `queued=true`，日志照旧写；Redis 连不上则自动降级到兜底表。

### 3.3 非 Development 环境（内网 Beta 服务器）

Beta 引导默认关闭，需要显式打开，且**外网部署会被启动自检拒绝**：

```bash
export ASPNETCORE_ENVIRONMENT=Production
export EFBM__Beta__Enabled=true
export EFBM__Beta__AppKey=beta-fbox-a01
export EFBM__Beta__AppSecret='换成长随机串'
export EFBM__RequestLog__Directory=/var/log/efbm/api   # 生产用绝对路径，避免随部署目录漂移
dotnet EFBM.Web.dll
```

---

## 4. 验证

### 4.0 命令在哪里跑（Windows 必读）

本文命令块标 `bash` 的是**通用 shell 片段**，不是一个文件，不能双击运行；要**打开终端窗口，把整段粘进去**回车。

Windows 上有两种终端，选一种即可：

| 终端 | 怎么打开 | 说明 |
| --- | --- | --- |
| **Git Bash** | 开始菜单搜 `Git Bash`（装 Git for Windows 时自带） | 标 `bash` 的命令块可**原样**粘贴，语法一致。**最省事** |
| **PowerShell** | 开始菜单搜 `PowerShell` | Windows 自带，但要按下面表格换写法 |
| CMD | 开始菜单搜 `cmd` | 不推荐：`-d '{"a":1}'` 的单引号不生效，容易踩坑 |

**不想折腾语法，就只用这一条**（PowerShell 里执行，脚本自己拼报文、发请求、读 `/health`）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\beta-smoke.ps1 -Count 5
```

#### bash → PowerShell 写法对照

| 手册里的写法 | PowerShell 等价写法 |
| --- | --- |
| `export A=b dotnet run` | `$env:A="b"; dotnet run` |
| `$VAR` | `$env:VAR` |
| `tail -n 1 file` | `Get-Content file -Tail 1` |
| `api-$(date +%Y%m%d).log` | `"api-$(Get-Date -Format yyyyMMdd).log"` |
| `grep xxx file` | `Select-String xxx file` |
| `jq -c 'select(.status!=200)' file` | `Get-Content file \| ConvertFrom-Json \| Where-Object { $_.status -ne 200 }` |
| `curl -X POST -d '{...}'` | 见下方「发一条报文」 |

#### 三个必须知道的坑

1. **PowerShell 5.1 里 `curl` 不是 curl**，它是 `Invoke-WebRequest` 的别名，`-d` / `-H` 这些参数全部不认。要用真 curl 必须写 `curl.exe`。
2. **`curl.exe -d '{"a":1}'` 在 PowerShell 里双引号会被吞掉**，服务端收到 `{a:1}` 直接 400。所以 PowerShell 下要么用 `Invoke-RestMethod`（见 4.1 的 PowerShell 版），要么把报文写进文件用 `-d "@body.json"`。
3. **`Get-Content` 读日志要加 `-Encoding UTF8`**，否则中文全是乱码（文件本身没问题，是终端按 GBK 读错了，详见 4.2）。

### 4.1 发一条真实报文

```bash
curl -i -X POST http://127.0.0.1:5000/api/events \
  -H "Content-Type: application/json" \
  -H "X-App-Key: beta-fbox-a01" \
  -H "X-App-Secret: beta-secret-2026" \
  -H "X-Request-Id: manual-001" \
  -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T10:00:00+08:00","data":{"orderId":"WO-001","boxId":"BOX-0001","planQty":1000,"completedQty":1}}'
```

PowerShell 版（等价，避开 4.0 说的引号坑）：

```powershell
$body = '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T10:00:00+08:00","data":{"orderId":"WO-001","boxId":"BOX-0001","planQty":1000,"completedQty":1}}'
Invoke-RestMethod -Uri http://127.0.0.1:5000/api/events -Method Post `
  -ContentType "application/json" `
  -Headers @{ "X-App-Key"="beta-fbox-a01"; "X-App-Secret"="beta-secret-2026"; "X-Request-Id"="manual-001" } `
  -Body $body | ConvertTo-Json -Depth 5
```

期望（纯日志模式）：

```json
{"code":0,"message":"accepted-by-log","data":{"messageId":"...","eventKey":"...","queued":false,"logged":true}}
```

### 4.2 看日志有没有真的写下

```bash
tail -n 1 src/EFBM.Web/logs/api/api-$(date +%Y%m%d).log
```

```powershell
# PowerShell 等价（-Encoding UTF8 不能省，见下方说明）
Get-Content "src/EFBM.Web/logs/api/api-$(Get-Date -Format yyyyMMdd).log" -Tail 1 -Encoding UTF8
```

> ⚠️ 日志是 **UTF-8 无 BOM**。PowerShell 5.1 的 `Get-Content` 默认按系统 ANSI（国区即 GBK）解码，读出来中文是 `鏈惎鐢` 这种乱码——**文件本身没坏**，是终端读错了。加 `-Encoding UTF8` 即可；用 Git Bash 的 `tail` 或 VS Code 打开也正常。之所以不写 BOM，是为了 `jq` / `grep` 处理首行时不带多余字节。

```json
{"ts":"2026-11-12T02:25:23.242Z","trace":"manual-001","method":"POST","path":"/api/events","scheme":"http","ip":"127.0.0.1","ua":"curl/8.21.0","ctype":"application/json","status":200,"code":0,"msg":"accepted-by-log","queued":false,"fallback":false,"messageId":"6eadf163-...","eventKey":"090a805f...","event":"ORDER_COMPLETED","deviceId":"FBOX-A01","auth":"AppKeyAppSecret","appKey":"beta****01","ms":15,"raw":"{\"event\":\"ORDER_COMPLETED\",...}"}
```

只要 `raw` 与设备原文一致，这条数据就没有丢。

### 4.3 健康检查

```bash
curl -s http://127.0.0.1:5000/health
```

```powershell
# PowerShell 等价
Invoke-RestMethod http://127.0.0.1:5000/health | ConvertTo-Json -Depth 5
```

```json
{
  "status": "ok",
  "redis": { "enabled": false, "connected": false, "streamLength": 0, "error": "Redis 未启用（EFBM:RedisEnabled=false）" },
  "requestLog": { "enabled": true, "written": 12, "failed": 0, "currentFile": "/.../logs/api/api-20261112.log" },
  "fallback": { "pending": 0, "error": null }
}
```

判读规则：

- `requestLog.failed > 0` → **必须立刻处理**（磁盘满 / 权限），此时数据是真的没落盘，`status` 会变 `degraded`。
- `RedisEnabled=true` 且 `redis.connected=false` → 上报仍 200，走兜底表，`status=degraded`。
- `RedisEnabled=false` → `redis.connected=false` 属正常，**不是故障**。

### 4.4 日志回放补录（--replay）

纯日志模式（默认）没有常驻消费者，历史日志不会自动进业务表；Redis 模式下若某条报文「只进了日志没进流」也漏在库外。两者都用 `--replay` 把日志补录进库：

```bash
# 补录单个日志文件
dotnet EFBM.Web.dll --replay logs/api/api-20261112.log

# 补录整个目录（按文件名排序、逐文件逐行回放）
dotnet EFBM.Web.dll --replay logs/api
```

要点：

- **只回放成功的**：逐行解析 JSON，只处理 `status=200` 的行；`400/401/403` 等被拒行本就不该落业务表，直接跳过。`raw` 字段缺失或 JSON 非法的行也跳过，结尾统一汇总。
- **幂等兜住**：回放走与 Redis 消费者完全相同的 `EventPersistService`，以 `EventKey = SHA1(event|deviceId|timestamp)` 去重。同一份日志重复跑、断点续跑、多份日志交叉，都只产生一条原始报文（`RepeatCount` 累加），不会重复落明细。
- **与 Redis 模式无关**：`--replay` 只依赖 SQLite/MySQL 业务库与日志文件，**不连 Redis**；即便 `RedisEnabled=true`，也能把「只进日志没进流」的报文补进库。
- **SQLite 路径坑（必读）**：`Data Source=EFBM.db` 的相对路径解析到 **DLL 所在目录**（`bin/Debug/net10.0/EFBM.db` 或发布目录），不是进程工作目录。在 `logs/` 旁直接跑 `--replay` 会新建一份空库，看着像「回放没写数据」。`Program.cs` 已显式 `DAL.AddConnStr` 登记 `EFBM`/`Membership` 连接串，确保在任意工作目录都命中同一份库文件。若回放后查不到数据，先 `cd` 到 DLL 目录或确认连接串指向的业务库路径。
- **输出**：结尾打印各业务表的写入 / 跳过计数与耗时；失败行单独列数，不改库。

回放后核验（与 Redis 模式落库结果一致）：

```bash
sqlite3 bin/Debug/net10.0/EFBM.db \
  "SELECT (SELECT COUNT(*) FROM DeviceEventRaw) AS raw,
          (SELECT COUNT(*) FROM WorkOrder) AS wo,
          (SELECT COUNT(*) FROM ProductionRecord) AS pr,
          (SELECT COUNT(*) FROM FaultRecord) AS fault;"
```

### 4.5 一键冒烟

```powershell
powershell -ExecutionPolicy Bypass -File tools/beta-smoke.ps1 -Count 5
```

脚本逐条打印 `HTTP 码 / message / queued / logged`，最后汇总 `requestLog.written` 与落点分布；任一请求非 200 则退出码非 0。

### 4.6 启用 Redis 时确认消息真的在流里

```bash
redis-cli -n 5 XLEN efbm:events          # 条数，应随上报增长
redis-cli -n 5 XINFO GROUPS efbm:events  # 消费者组与 lag（消费未开始时 lag=条数）
redis-cli -n 5 XRANGE efbm:events - + COUNT 1
```

---

## 5. 文本日志格式

一台设备一行，UTF-8 无 BOM，`\n` 结尾；字段固定、空值省略，可直接 `grep` / `jq`。

| 字段 | 含义 |
| --- | --- |
| `ts` | 服务端接收时间（UTC，毫秒） |
| `trace` | `X-Request-Id`，缺省用框架 TraceIdentifier |
| `method` / `path` / `scheme` | `POST` / `/api/events` / `http` 或 `https` |
| `ip` | 来源 IP（可信代理时取 `X-Forwarded-For` 首段） |
| `ua` / `ctype` | User-Agent、Content-Type |
| `status` / `code` / `msg` | HTTP 状态码、业务码、响应文案 |
| `queued` / `fallback` | 是否入 Stream、是否写兜底表 |
| `messageId` / `eventKey` | 消息追踪 ID、幂等键 `SHA1(event\|deviceId\|timestamp)` |
| `event` / `deviceId` | 报文中的事件与设备（解析失败时缺省） |
| `auth` / `appKey` | 认证模式、**脱敏后**的 AppKey（如 `beta****01`） |
| `parseWarning` | 解析告警编码（`W100`~`W108`），见 [上报字段字典](上报字段字典.md) §5 |
| `warn` | 降级 / 拒绝原因，正常入流时省略 |
| `ms` | 同步链路耗时（毫秒） |
| `raw` | **请求体原文**，逐字保留；`IncludeRaw=false` 时省略 |

**绝不记录** `AppSecret` 与 `Authorization`。

常用检索：

```bash
# 某台设备的全部记录
jq -c 'select(.deviceId=="FBOX-A01")' logs/api/api-20261112.log

# 只看被拒的
jq -c 'select(.status!=200) | {ts,status,msg,deviceId,warn}' logs/api/api-20261112.log

# 提取原文做人工补录（P3 前的手工路径）
jq -r 'select(.status==200) | .raw' logs/api/api-20261112.log > replay.jsonl
```

### 5.1 滚动与保留

| 配置 | 默认 | 行为 |
| --- | --- | --- |
| `Directory` | `logs/api` | 相对内容根目录；生产建议绝对路径 |
| `FileNamePrefix` | `api` | 文件形如 `api-20261112.log`、超限后 `api-20261112-002.log` |
| `MaxFileMB` | `64` | 单文件上限，超过当日切序号 |
| `RetentionDays` | `90` | 过期自动删除；`0` 表示永不清理；**只删本目录本前缀的 `*.log`** |
| `IncludeRaw` | `true` | 关掉后无法离线补录，仅在报文含敏感数据时关闭 |
| `FlushToDisk` | `true` | 每条 `fsync`（约 1ms/条），断电不丢；高并发可关 |

---

## 6. 关键配置

| 配置项 | 默认 | 说明 |
| --- | --- | --- |
| `EFBM:Deployment` | `Intranet` | 改 `Internet` 会强制要求 HTTPS 且禁用匿名 |
| `EFBM:AuthMode` | `AppKeyAppSecret` | `Anonymous` / `Basic` / `AppKeyAppSecret` |
| `EFBM:RedisEnabled` | `true`（Development 中为 `false`） | 关闭即纯日志模式：不连 Redis、不写兜底表 |
| `EFBM:RedisServer` | `127.0.0.1:6379` | 生产用环境变量覆盖 |
| `EFBM:RedisDb` | `5` | 与业务缓存隔离 |
| `EFBM:StreamKey` | `efbm:events` | 多系统共享同一条流 |
| `EFBM:PersistGroup` | `efbm-persist` | 本系统落库组，最先建组 |
| `EFBM:LocalFallbackEnabled` | `true` | 仅 `RedisEnabled=true` 时生效 |
| `EFBM:RequestLog:Enabled` | `true` | 关掉后 Redis 与兜底同时失效将直接 503 |
| `EFBM:RequestLog:Directory` | `logs/api` | 相对内容根目录 |
| `EFBM:RequestLog:RetentionDays` | `90` | `0` 不清理 |
| `EFBM:RequestLog:FlushToDisk` | `true` | 断电不丢 vs 吞吐 |
| `EFBM:MaxBodyBytes` | `4194304` | 仅防异常大包 |
| `EFBM:Beta:Enabled` | `false` | 配置驱动凭据引导，**外网禁止** |
| `EFBM:Beta:AppKey` / `AppSecret` | 空 | 引导凭据，Secret ≥ 8 位 |
| `ConnectionStrings:EFBM` | `Data Source=EFBM.db;Provider=SQLite` | 换 MySQL 只改连接串 |

> 生产环境的 `EFBM:Beta:*` 应改用后台发放（P4）；Beta 引导只是联调期临时通道。

---

## 7. 常见问题

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 400 `invalid json` | JSON 不合法（含尾随逗号）或缺少 `event` / `deviceId` / `timestamp` | 请求方保证格式；设备不会重发，日志里有原文可补录 |
| 401 | AppKey 不存在 / Secret 不匹配 | 核对 `EFBM:Beta:AppKey` / `AppSecret`，或查 `AppCredential` 表 |
| 403 | 凭据已停用，或报文 `deviceId` 与凭据绑定不一致 | 一个凭据只允许一台设备，串用会被拒 |
| 200 但 `queued=false` | 纯日志模式，或 Redis 连不上走了兜底 | 看响应 `message`；`accepted-by-log` 表示只在文本日志里 |
| 200 但日志文件没新增 | 目录不对（相对内容根目录）或 `RequestLog:Enabled=false` | 看 `/health` 的 `requestLog.currentFile` 绝对路径 |
| `/health` 里 `requestLog.failed>0` | 磁盘满 / 目录无写权限 | **高优处理**：这段时间的数据没有落盘，`status=degraded` |
| 503 | 队列、兜底、日志全失败 | 磁盘不可写，属于必须立即告警的故障 |
| `/health` 里 `fallback.pending` 一直涨 | Redis 长期不可用 | 先修 Redis，回灌服务每 30 秒最多回灌 100 条 |
| 启动报「外网部署禁止启用 Beta 引导」 | `Deployment=Internet` 且 `Beta:Enabled=true` | 关掉 Beta 引导，改用正式凭据 |
| 启动报 `attempt to write a readonly database`（Cube 菜单初始化） | Cube 在 SQLite 下初始化后台菜单的已知噪音 | 不影响上报链路；后台属 P4，届时改用 MySQL 或单独排查 |

---

## 8. 已知限制与下一步

1. **日志不是数据库**：查询只能 `grep` / `jq`，没有后台页面（P4）。P3 落库后日志已退化为**审计与补录源**——`--replay`（§4.4）可把历史日志补进业务表。
2. **纯日志模式不写兜底表**：避免同一份数据在 SQLite 里无限堆积；切到 Redis 模式后新请求才会进兜底表。
3. **纯日志模式无消费者**：默认 `RedisEnabled=false` 时数据只进文本日志，靠 `--replay` 补录；启用 Redis 后 `PersistConsumerService` 常驻消费落库。
4. **没有安全裁剪**：Stream 只增不减，Beta 数据量小可接受；正式上线前必须落 P4 的按组最小已确认 ID 裁剪。
5. **未做压测**：P99 与吞吐需在目标环境用真实设备节拍验证（单件 > 1 秒，压力不大）；`FlushToDisk=true` 每条约 1ms，属于可接受成本。
6. 下一步按《开发计划.md》：P3 消费者与全量落库（含日志回放工具）**已完成** → P4 后台与裁剪 → P5 部署监控 → P6 联调压测。
