# Beta 公网测试部署（宝塔面板）

> 场景：把 Beta 纯日志版部署到公网 Linux 服务器做真机上报测试。
> 这套方案**不需要**安装 .NET、MySQL、Redis：发布包是 linux-x64 自包含的，数据库用 SQLite，数据落点只有两处——文本日志 `logs/api/` 与 SQLite。
> 通用的生产部署（MySQL / Redis / systemd）见《部署文档.md》§3 / §4，本文只覆盖 Beta 测试。

---

## 0. 发布包

开发机已生成：`publish/efbm-beta-linux64.tar.gz`（约 51MB，linux-x64 自包含）。

关键文件：

| 文件 | 说明 |
| --- | --- |
| `EFBM.Web` | Linux 可执行文件（自包含，直接运行，不需要 dotnet 命令） |
| `EFBM.Web.dll` | 主程序 |
| `appsettings.json` | 基础配置（默认 SQLite、Kestrel 监听 `127.0.0.1:5000`） |
| `Config/XCode.config` | `Migration=On`：首次启动自动建表，必须随包 |

---

## 1. 上传与解压

1. 宝塔面板 →「文件」→ 进入 `/www/wwwroot` → 上传 `efbm-beta-linux64.tar.gz`。
2. 在文件列表中右键该包 →「解压」到 `/www/wwwroot/efbm`（没有该目录就先新建）。
3. 解压后确认 `/www/wwwroot/efbm/EFBM.Web`（无扩展名的那个）存在。
4. 宝塔「终端」（或 SSH）执行——Windows 打包不带可执行位，**必须补**：

```bash
chmod +x /www/wwwroot/efbm/EFBM.Web
```

---

## 2. 进程守护（Supervisor）

1. 宝塔「软件商店」→ 搜索安装 **进程守护管理器（Supervisor）**。
2. 「添加守护任务」：

| 项 | 值 |
| --- | --- |
| 名称 | `efbm` |
| 运行目录 | `/www/wwwroot/efbm` |
| 启动命令 | 见下方（环境变量直接写在命令前面，最不容易出错） |
| 启动用户 | `root`（测试期最省事；正式部署换专用账号并收紧权限） |
| 进程数量 | 1 |
| 勾选 | 开机自启、异常自动重启 |

启动命令（一整行，`AppSecret` 换成你自己的强随机串，示例用 `openssl rand -hex 16` 生成）：

```bash
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:5000 EFBM__RedisEnabled=false EFBM__Beta__Enabled=true EFBM__Beta__AppSecret=换成强随机串 EFBM__RequestLog__Directory=/www/wwwroot/efbm/logs/api XCode_EFBM="Data Source=/www/wwwroot/efbm/EFBM.db;Provider=SQLite" XCode_Membership="Data Source=/www/wwwroot/efbm/Membership.db;Provider=SQLite" /www/wwwroot/efbm/EFBM.Web
```

> `XCode_EFBM` / `XCode_Membership` 两个变量**必须带上**：Production 环境会加载 `appsettings.Production.json` 里的 MySQL 模板连接串（占位密码），不带就会启动即连库失败、进程反复重启。XCode 环境变量优先级高于 appsettings。

环境变量逐项解释：

| 变量 | 作用 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT=Production` | 生产配置；Beta 引导在 Production 下允许（外网 Internet 配置才禁止） |
| `ASPNETCORE_URLS` | 只监听本机 5000，公网流量由 Nginx 反代进来 |
| `EFBM__RedisEnabled=false` | **纯日志模式**：不连 Redis、不写兜底表，数据只进文本日志 |
| `EFBM__Beta__Enabled=true` | 启动时自动创建设备 `FBOX-A01` 与凭据 `beta-fbox-a01`（幂等，重复启动不会重复建） |
| `EFBM__Beta__AppSecret=<强随机串>` | 服务器上的上报密钥；**注意与本地开发的 `beta-secret-2026` 不同**，设备侧要配这个新值 |
| `EFBM__RequestLog__Directory=/www/wwwroot/efbm/logs/api` | 请求日志绝对路径，避免依赖进程工作目录 |
| `XCode_EFBM` / `XCode_Membership` | **必加（见下方「重要」）**：把连接串覆盖回 SQLite |

3. 保存后状态应为「运行中」。点「日志」应能看到启动输出，包含：
   `数据库: SQLite`、`Redis 未启用（纯日志模式）`、`Beta 引导已启用`。

> 备选：不用插件，直接写 systemd（`/etc/systemd/system/efbm.service`）也可以，模板见《部署文档.md》§3.3。**两种守护只留一种。**

### 2.1 备选：宝塔自带「.Net项目」模块

新版宝塔软件商店有 .NET 项目管理模块（装 .NET 10 后可用），弹窗本身**没有环境变量输入框**，做法：

- 项目名称填域名；运行路径 `/www/wwwroot/<域名>`；.Net 版本选 10.x；启动用户 `www`（目录先 `chown -R www:www`）。
- 项目端口 5000，**不勾「放行端口」**（走域名反代，不把 5000 露公网）。
- 启动命令填 `bash /www/wwwroot/<域名>/start.sh`，环境变量全部写进 start.sh：

```bash
#!/bin/bash
cd /www/wwwroot/<域名>
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5000
export EFBM__RedisEnabled=false
export EFBM__Beta__Enabled=true
export EFBM__Beta__AppSecret=换成强随机串
export EFBM__RequestLog__Directory=/www/wwwroot/<域名>/logs/api
# 必加：覆盖 Production 的 MySQL 模板连接串，测试用 SQLite
export XCode_EFBM="Data Source=/www/wwwroot/<域名>/EFBM.db;Provider=SQLite"
export XCode_Membership="Data Source=/www/wwwroot/<域名>/Membership.db;Provider=SQLite"
exec dotnet EFBM.Web.dll
```

- 自包含发布包可直接跑 `EFBM.Web` 可执行文件（先 `chmod +x`）；若服务器已装 .NET 10 运行时，用 `dotnet EFBM.Web.dll` 即可，不需要 chmod。

---

## 3. 建站与反向代理

1. 宝塔「网站」→「添加站点」：
   - 域名：填你解析到这台服务器的域名；**没有域名就填服务器公网 IP**。
   - 根目录：`/www/wwwroot/efbm`（仅占位，请求实际走反代）。
   - PHP 版本：纯静态；不创建数据库。
2. 站点设置 →「反向代理」→ 添加：
   - 目标 URL：`http://127.0.0.1:5000`
   - 代理名称：`efbm`，发送域名：`$host`，开启。
3. 点开生成的反代配置，确认里面有这两行（宝塔默认模板通常只有第一行，缺就补）：

```nginx
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

   缺 `X-Forwarded-Proto` 时，日志里的 `scheme` 会误记为 http，且后续开强制 HTTPS 容易重定向循环。
4. 站点设置 →「SSL」：有域名就 **Let's Encrypt 一键申请**，然后开「强制 HTTPS」。没域名可先跳过，用 `http://IP` 测试（能用，但 AppSecret 是明文传输，测完尽快换 HTTPS）。

---

## 4. 防火墙放行

| 位置 | 放行 | 禁止 |
| --- | --- | --- |
| 宝塔「安全」+ 云厂商安全组 | `80`、`443` | `5000`（Kestrel 只听本机，本来也不对外）、`6379`、`3306` |

---

## 5. 验证

在服务器终端逐条执行：

```bash
# 1) 进程活着，5000 在听
ps -ef | grep EFBM.Web | grep -v grep
curl -s http://127.0.0.1:5000/health

# 2) 本机模拟一次上报（AppSecret 换成你在第 2 步配的值）
curl -i -X POST http://127.0.0.1:5000/api/events \
  -H "Content-Type: application/json" \
  -H "X-App-Key: beta-fbox-a01" \
  -H "X-App-Secret: 换成强随机串" \
  -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T12:00:00+08:00","data":{"orderId":"WO-TEST","boxId":"BOX-0001","planQty":1000,"completedQty":1}}'
# 期望 200，body: {"code":0,"message":"accepted-by-log",...,"logged":true}

# 3) 日志真的落了一行，raw 与原文一致
tail -n 1 /www/wwwroot/efbm/logs/api/api-$(date +%Y%m%d).log

# 4) 从公网走域名/IP 再来一次（本机或你自己电脑上）
curl -i -X POST https://你的域名/api/events -H "Content-Type: application/json" \
  -H "X-App-Key: beta-fbox-a01" -H "X-App-Secret: 换成强随机串" \
  -d '{"event":"ORDER_COMPLETED","deviceId":"FBOX-A01","timestamp":"2026-11-12T12:00:01+08:00"}'
```

全通之后，把设备侧的上报地址、AppKey、AppSecret 配上即可真机联调。本机 Windows 上用

```powershell
powershell -ExecutionPolicy Bypass -File tools\beta-smoke.ps1 -BaseUrl https://你的域名 -AppSecret 换成强随机串 -Count 5
```

可做批量冒烟。

---

## 6. 数据在哪、怎么查

| 数据 | 位置 | 查看方式 |
| --- | --- | --- |
| 每条请求一行 JSON（含原文 `raw`） | `/www/wwwroot/efbm/logs/api/api-yyyyMMdd.log` | `tail` / `grep` / 宝塔文件管理直接下载 |
| 设备与凭据表 | `/www/wwwroot/efbm/EFBM.db`（首启自动建） | SQLite 工具或 `sqlite3` 命令 |
| 运行日志（启动 / 异常） | Supervisor 任务「日志」按钮 | 面板查看 |

日志按天滚动、单文件超 64MB 切分、保留 90 天自动清理，无需 logrotate。

---

## 7. 常见问题

| 现象 | 原因 / 处理 |
| --- | --- |
| 启动后很快停止，日志大量 `Access denied for user 'efbm'@'localhost'`（MySQL） | **`ASPNETCORE_ENVIRONMENT=Production` 加载了 `appsettings.Production.json` 的 MySQL 模板连接串**（占位密码，服务器上没建 efbm 库和账号）。测试用 SQLite 必须加环境变量覆盖：`XCode_EFBM="Data Source=<目录>/EFBM.db;Provider=SQLite"` 与 `XCode_Membership="Data Source=<目录>/Membership.db;Provider=SQLite"`（XCode 环境变量优先级高于 appsettings）。日志里 XCode 自动下载 MySql.Data 也是同一原因 |
| Supervisor 状态一直「已停止」或反复重启 | 点任务日志看报错。最常见：① 没执行 `chmod +x EFBM.Web` → `Permission denied`；② 端口被占；③ AppSecret 少于 8 位被自检拒绝 |
| 公网访问 502 | 守护进程没起来，或反代目标写错；先 `curl http://127.0.0.1:5000/health` 在服务器本机验证 |
| 上报 401 | AppKey/AppSecret 与服务器配置不一致；注意服务器用的是第 2 步的新密钥，不是 `beta-secret-2026` |
| 上报 403 | 报文 `deviceId` 与凭据绑定设备不一致（只能用 `FBOX-A01`） |
| 启动报 `Unable to load DLL sqlite3` 之类 | 系统缺 SQLite 原生库：`apt install libsqlite3-0`（Debian/Ubuntu）或 `yum install sqlite-libs`（CentOS） |
| 启动报「Beta 引导 AppSecret 校验失败」 | 密钥少于 8 位，换长随机串 |
| HTTPS 一直重定向循环 | 反代缺 `proxy_set_header X-Forwarded-Proto $scheme;` |
| `/health` 的 `requestLog.failed > 0`、status=degraded | 日志目录不可写：`chown -R root:root /www/wwwroot/efbm/logs` 或检查磁盘空间；此时数据真的没落盘，必须立刻处理 |

---

## 8. 测试结束后

- 这批数据只在日志和 SQLite 里，**没有业务落库**（P3 未做）；要留数据就定期把 `logs/api` 与 `EFBM.db` 下载备份。
- 换新版本：Supervisor 里停止任务 → 备份 `EFBM.db` 与 `logs/` → 解压新包覆盖其余文件 → `chmod +x` → 启动。配置在守护任务的环境变量里，不受覆盖影响。
