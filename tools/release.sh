#!/bin/bash
# EFBM 发布打包脚本 —— 每次发版生成两种发布物 + start.sh + tar.gz
# 版本号遵循 NewLife 规则：主.次.年.MDD（如 1.0.2026.1113 = 2026年11月13日，MDD 月不补零）
# 用法：./tools/release.sh [版本号]（缺省 = csproj 的 主.次 + 当天 年.MDD 自动生成）
# 产物（publish/ 下）：
#   efbm-<版本>-linux64.tar.gz       自包含版：服务器无需安装 .NET 运行时（推荐，宝塔首选）
#   efbm-<版本>-linux64-fdd.tar.gz   框架依赖版：包体小，需服务器已装 ASP.NET Core 10.x 运行时
set -euo pipefail
cd "$(dirname "$0")/.."

BASE="$(grep -oP '(?<=<Version>)[^<]+' src/EFBM.Web/EFBM.Web.csproj | head -1)"
if [ $# -ge 1 ]; then
    VERSION="$1"
else
    # NewLife 规则自动拼版本：主.次.年.MDD（%-m%-d 不补零：3月5日 = 35）
    VERSION="${BASE}.$(date +%Y).$(date +%-m%-d)"
fi
case "$VERSION" in
    *.[0-9][0-9][0-9][0-9].[0-9]*) ;;               # 含 年.MDD，合规
    *) echo "⚠️ 版本号 $VERSION 不含 年.MDD，不符合 NewLife 规则（主.次.年.MDD，如 1.0.2026.1113）"; exit 1 ;;
esac

# start.sh：两种发布物共用同一份（自动探测：有自包含可执行文件就用它，否则回退 dotnet）
write_start_sh() {
cat > "$1/start.sh" <<'EOF'
#!/bin/bash
# EFBM 启动脚本（发布时由 tools/release.sh 自动生成，勿手工改动后覆盖）
# 用法：bash start.sh   或   ./start.sh
# ⚠️ 本脚本每次发版都会被新包覆盖：持久性配置请放宝塔面板「环境变量」或
#    appsettings.Production.json（升级时保留），不要只改在这里。
# 配置优先级：面板环境变量 / 此处 export > appsettings.Production.json > appsettings.json
# 环境变量均可从外部覆盖：
#   ASPNETCORE_ENVIRONMENT / ASPNETCORE_URLS / XCode_EFBM / XCode_Membership / EFBM__* 等
cd "$(dirname "$0")" || exit 1

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5000}"

# 连接串缺省 SQLite（零依赖起步，首次启动自动建表）。
# 生产 MySQL：在面板环境变量里设置 XCode_EFBM / XCode_Membership 即可覆盖，例如
#   XCode_EFBM="Server=127.0.0.1;Port=3306;Database=EFBM;Uid=efbm;Pwd=强密码;Provider=MySql"
export XCode_EFBM="${XCode_EFBM:-Data Source=$PWD/EFBM.db;Provider=SQLite}"
export XCode_Membership="${XCode_Membership:-Data Source=$PWD/Membership.db;Provider=SQLite}"

# 请求日志目录（生产建议绝对路径）
export EFBM__RequestLog__Directory="${EFBM__RequestLog__Directory:-$PWD/logs/api}"
mkdir -p logs/api

# ── 常用开关（按需取消注释；也可配到宝塔面板「.Net项目」的环境变量里，效果相同）──
# export EFBM__RedisEnabled=false          # 纯日志模式：不连 Redis，数据只进文本日志与 SQLite（服务器没装 Redis 时建议打开）
# export EFBM__RedisServer=127.0.0.1:6379  # Redis 地址（默认即此，可改）
# export EFBM__RedisPassword=              # Redis 密码
# export EFBM__AuthMode=Anonymous          # 业务鉴权模式；外网必须 AppKeyAppSecret 或 Basic，禁止 Anonymous
# export EFBM__Beta__Enabled=true          # 仅内网测试：Beta 引导凭据（外网启动自检会拒绝）

# 自包含版：直接运行（chmod 兜底修复 Windows 打包丢失的可执行位）；
# 框架依赖版：回退 dotnet（服务器需已装 ASP.NET Core 10.x 运行时，dotnet --info 核对）
EXEC="$PWD/EFBM.Web"
if [ -f "$EXEC" ]; then
    chmod +x "$EXEC" 2>/dev/null || true
    exec "$EXEC"
fi
exec dotnet EFBM.Web.dll
EOF
chmod +x "$1/start.sh"
}

package() { # $1=发布目录 $2=包文件名 —— 文件直接放包根目录（无外层文件夹），与 beta 包布局一致
    rm -f "$2"
    tar -czf "$2" -C "$1" .
    echo "包: $2  $(ls -lh "$2" | awk '{print $5}')  SHA256: $(sha256sum "$2" | awk '{print $1}')"
}

echo "==> 发布版本: ${VERSION}"

echo "==> [1/2] 自包含版（linux-x64，服务器无需 .NET 运行时）"
OUT="publish/efbm"
rm -rf "$OUT"   # publish 不清理输出目录，必须手动清，否则旧产物残留
dotnet publish src/EFBM.Web/EFBM.Web.csproj -c Release -r linux-x64 --self-contained true -o "$OUT" -p:Version="$VERSION" -v q --nologo
write_start_sh "$OUT"
[ -f "$OUT/EFBM.Web" ] && chmod +x "$OUT/EFBM.Web" 2>/dev/null || true
package "$OUT" "publish/efbm-${VERSION}-linux64.tar.gz"

echo "==> [2/2] 框架依赖版（需服务器已装 ASP.NET Core 10.x 运行时）"
OUT="publish/efbm-fdd"
rm -rf "$OUT"   # publish 不清理输出目录，必须手动清，否则旧产物残留
# 同样指定 linux-x64 但不自包含：只保留 linux-x64 原生库。
# 不带 -r 的"便携式"发布会把 32 个平台（Android/iOS/wasm/win/mac/linux）的原生库
# 全塞进 runtimes/（实测 69MB），导致包体与自包含版几乎一样大。
dotnet publish src/EFBM.Web/EFBM.Web.csproj -c Release -r linux-x64 --self-contained false -o "$OUT" -p:Version="$VERSION" -v q --nologo
rm -f "$OUT/EFBM.Web.exe"   # Windows 宿主 apphost，Linux 上无用
write_start_sh "$OUT"
package "$OUT" "publish/efbm-${VERSION}-linux64-fdd.tar.gz"

echo "==> 完成（两版 start.sh 内容一致，部署方式见《部署文档.md》§2.1 / §4）"
