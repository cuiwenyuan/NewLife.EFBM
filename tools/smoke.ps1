<#
.SYNOPSIS
    EFBM smoke test - send device reports and verify the receiver accepts them.

.DESCRIPTION
    Posts one or more reports to POST /api/events, prints the HTTP status plus where
    each message actually landed (response message field):

        accepted              -> written to the Redis Stream (EFBM:RedisEnabled=true)
        accepted-by-fallback  -> Redis failed, saved to the local fallback table
        accepted-by-log       -> log-only mode / everything else failed, but the raw
                                 payload is in logs/api/api-yyyyMMdd.log

    Then prints GET /health so you can confirm requestLog.written grew and, when
    Redis is enabled, redis.streamLength grew too.

    Exit code 0 = every report returned HTTP 200. Non-zero = at least one failed.

.PARAMETER BaseUrl
    Receiver base URL. Default http://127.0.0.1:5000

.PARAMETER Count
    How many reports to send. Each uses a unique timestamp, so EventKey differs.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/smoke.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/smoke.ps1 -BaseUrl http://10.0.0.20:5000 -Count 5
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5000",
    [string]$AppKey = "beta-fbox-a01",
    [string]$AppSecret = "beta-secret-2026",
    [string]$DeviceId = "FBOX-A01",
    [string]$EventName = "ORDER_COMPLETED",
    [int]$Count = 1
)

$ErrorActionPreference = "Stop"
# Invoke-WebRequest 的进度条在 5.1 下会污染输出
$ProgressPreference = "SilentlyContinue"
$endpoint = "$($BaseUrl.TrimEnd('/'))/api/events"
$headers = @{ "X-App-Key" = $AppKey; "X-App-Secret" = $AppSecret }
$failed = 0
$landed = @{}

Write-Host "POST $endpoint  device=$DeviceId  count=$Count" -ForegroundColor Cyan

for ($i = 1; $i -le $Count; $i++) {
    # Millisecond precision keeps EventKey unique even when sending in a tight loop
    $ts = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")
    # Full payload matches the frozen field dictionary, so ParseWarning stays empty
    $payload = @{
        event     = $EventName
        deviceId  = $DeviceId
        timestamp = $ts
        data      = @{
            orderId      = "WO-SMOKE"
            boxId        = "BOX-0001"
            planQty      = 1000
            completedQty = 1
        }
    } | ConvertTo-Json -Depth 5 -Compress

    try {
        $resp = Invoke-WebRequest -Uri $endpoint -Method Post -Body $payload -ContentType "application/json" `
            -Headers $headers -UseBasicParsing -TimeoutSec 30
        $body = $resp.Content | ConvertFrom-Json
        $msg = $body.message
        if ($landed.ContainsKey($msg)) { $landed[$msg]++ } else { $landed[$msg] = 1 }
        Write-Host ("  [{0}] HTTP {1} {2} queued={3} logged={4}" -f $i, $resp.StatusCode, $msg, $body.data.queued, $body.data.logged) -ForegroundColor Green
    }
    catch {
        $failed++
        $status = "n/a"
        $body = $_.Exception.Message
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $body = $reader.ReadToEnd()
            $reader.Close()
        }
        Write-Host ("  [{0}] HTTP {1} {2}" -f $i, $status, $body) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "GET $($BaseUrl.TrimEnd('/'))/health" -ForegroundColor Cyan
try {
    $health = Invoke-WebRequest -Uri "$($BaseUrl.TrimEnd('/'))/health" -UseBasicParsing -TimeoutSec 30
    $json = $health.Content | ConvertFrom-Json
    Write-Host ("  status      : {0}" -f $json.status)
    Write-Host ("  redis       : enabled={0} connected={1} streamLength={2} error={3}" -f $json.redis.enabled, $json.redis.connected, $json.redis.streamLength, $json.redis.error)
    Write-Host ("  requestLog  : enabled={0} written={1} failed={2} file={3}" -f $json.requestLog.enabled, $json.requestLog.written, $json.requestLog.failed, $json.requestLog.currentFile)
    Write-Host ("  fallback    : pending={0} error={1}" -f $json.fallback.pending, $json.fallback.error)

    if ($json.requestLog.failed -gt 0) {
        Write-Host "  => WARNING: request log has failed writes. Data may be lost - check disk space and permissions." -ForegroundColor Red
    }
    if ($json.redis.connected) {
        Write-Host "  => messages are in Redis too. Inspect with: redis-cli -n 5 XLEN efbm:events" -ForegroundColor Green
    }
    elseif ($json.requestLog.enabled) {
        Write-Host "  => log-only mode: every request is in the text log, Redis is not used." -ForegroundColor Green
    }
    else {
        Write-Host "  => Redis unreachable and request log disabled - check the fallback table." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ("  health check failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
}

Write-Host ""
if ($landed.Count -gt 0) {
    $summary = ($landed.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ", "
    Write-Host "Landing summary: $summary"
}

if ($failed -gt 0) {
    Write-Host "$failed of $Count report(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host "All $Count report(s) returned HTTP 200." -ForegroundColor Green
exit 0
