#!/bin/bash
# ============================================================================
# EFBM 公网冒烟测试（bash 版，只用 curl，不依赖 jq）
#
# 用法：
#   bash smoke.sh                          # 默认打 https://efbm.cuiwenyuan.cn
#   bash smoke.sh -n 5                     # 发 5 条
#   bash smoke.sh -u http://1.2.3.4        # 换地址（SSL 没配好时先用 http）
#   bash smoke.sh -s 你的AppSecret          # 换密钥
#   bash smoke.sh -k                       # 跳过证书校验（证书链不完整时）
#
# 退出码：0 = 全部 200；非 0 = 有失败
# ============================================================================
set -u

BASE_URL="https://efbm.cuiwenyuan.cn"
APP_KEY="beta-fbox-a01"
# 密钥不入库：必须用 -s 或环境变量 APP_SECRET 传入
APP_SECRET="${APP_SECRET:-}"
DEVICE_ID="FBOX-A01"
EVENT_NAME="ORDER_COMPLETED"
COUNT=1
INSECURE=""

while getopts "u:s:k:n:d:e:h" opt; do
  case "$opt" in
    u) BASE_URL="$OPTARG" ;;
    s) APP_SECRET="$OPTARG" ;;
    k) INSECURE="-k" ;;
    n) COUNT="$OPTARG" ;;
    d) DEVICE_ID="$OPTARG" ;;
    e) EVENT_NAME="$OPTARG" ;;
    h) sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "未知参数，-h 查看用法"; exit 2 ;;
  esac
done

BASE_URL="${BASE_URL%/}"
ENDPOINT="$BASE_URL/api/events"
HEALTH="$BASE_URL/health"

if [ -z "$APP_SECRET" ]; then
  echo "✗ 未提供 AppSecret：bash smoke.sh -s <你的AppSecret>（或环境变量 APP_SECRET）"
  exit 2
fi

# 每次独立时间戳（毫秒精度），保证 EventKey 不重复
ts() { date +%Y-%m-%dT%H:%M:%S.%3N%:z 2>/dev/null || date +%Y-%m-%dT%H:%M:%S%z; }

echo "=============================================================="
echo " POST $ENDPOINT"
echo " device=$DEVICE_ID  event=$EVENT_NAME  count=$COUNT"
echo "=============================================================="

FAILED=0
ACCEPTED=0
FALLBACK=0
BYLOG=0
REJECTED=0

for i in $(seq 1 "$COUNT"); do
  BODY=$(printf '{"event":"%s","deviceId":"%s","timestamp":"%s","data":{"orderId":"WO-SMOKE","boxId":"BOX-0001","planQty":1000,"completedQty":1}}' \
        "$EVENT_NAME" "$DEVICE_ID" "$(ts)")

  RESP=$(curl -sS $INSECURE --max-time 20 -w '\n%{http_code}' \
    -X POST "$ENDPOINT" \
    -H "Content-Type: application/json" \
    -H "X-App-Key: $APP_KEY" \
    -H "X-App-Secret: $APP_SECRET" \
    -H "X-Request-Id: smoke-$i" \
    -d "$BODY" 2>&1)

  CODE=$(printf '%s' "$RESP" | tail -n 1)
  JSON=$(printf '%s' "$RESP" | sed '$d')
  MSG=$(printf '%s' "$JSON" | grep -o '"message":"[^"]*"' | head -1 | cut -d'"' -f4)
  LOGGED=$(printf '%s' "$JSON" | grep -o '"logged":[a-z]*' | head -1 | cut -d':' -f2)

  if [ "$CODE" = "200" ]; then
    case "$MSG" in
      accepted)            ACCEPTED=$((ACCEPTED+1)) ;;
      accepted-by-fallback) FALLBACK=$((FALLBACK+1)) ;;
      accepted-by-log)     BYLOG=$((BYLOG+1)) ;;
    esac
    echo "  [$i] HTTP $CODE  message=${MSG:-?}  logged=${LOGGED:-?}"
  else
    FAILED=$((FAILED+1))
    if [ "$CODE" = "401" ] || [ "$CODE" = "403" ]; then REJECTED=$((REJECTED+1)); fi
    echo "  [$i] HTTP $CODE  ${MSG:-$(printf '%s' "$JSON" | head -c 300)}"
  fi
done

echo
echo "=============================================================="
echo " GET $HEALTH"
echo "=============================================================="
HEALTH_JSON=$(curl -sS $INSECURE --max-time 20 "$HEALTH" 2>&1)
echo "$HEALTH_JSON" | head -c 1200
echo

STATUS=$(printf '%s' "$HEALTH_JSON" | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4)
LOG_WRITTEN=$(printf '%s' "$HEALTH_JSON" | grep -o '"written":[0-9]*' | head -1 | cut -d':' -f2)
LOG_FAILED=$(printf '%s' "$HEALTH_JSON" | grep -o '"failed":[0-9]*' | head -1 | cut -d':' -f2)

echo
echo "--------------------------------------------------------------"
echo " 落点汇总：accepted=$ACCEPTED  by-fallback=$FALLBACK  by-log=$BYLOG  失败=$FAILED"
echo " 健康检查：status=${STATUS:-?}  requestLog.written=${LOG_WRITTEN:-?}  requestLog.failed=${LOG_FAILED:-?}"
echo "--------------------------------------------------------------"

if [ "${LOG_FAILED:-0}" != "0" ]; then
  echo " ⚠ requestLog.failed>0：日志写不进去（权限/磁盘），数据真的会丢，立刻处理！"
fi

if [ "$FAILED" -gt 0 ]; then
  echo " ✗ $FAILED / $COUNT 条失败"
  [ "$REJECTED" -gt 0 ] && echo "   401/403：核对 X-App-Key / X-App-Secret 与服务器 start.sh 中的 AppSecret 是否一致"
  exit 1
fi

echo " ✓ $COUNT 条全部 HTTP 200"
exit 0
