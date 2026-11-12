#!/usr/bin/env node
// EFBM 上报压测工具（P6 / TST-4a）
//
// 只用 Node 内置模块，不需要 npm install。默认打本机 5000 端口的 POST /api/events，
// 按设备数分摊并发，统计 QPS、P50 / P95 / P99、状态码分布与 5xx 数量。
//
// 判据（开发计划 §7）：多设备并发下 0 个 5xx，P99 在目标值以内。
//
// 用法：
//   node tools/loadtest/index.js --devices 20 --concurrency 20 --duration 30
//   node tools/loadtest/index.js --url http://127.0.0.1:5000 --key K --secret S --count 5000
//   node tools/loadtest/index.js --anonymous          # 内网 Anonymous 模式
//
// 退出码：0 全部成功且无 5xx；1 存在失败请求；2 参数或环境错误。

'use strict';

const http = require('http');
const https = require('https');
const { URL } = require('url');

function parseArgs(argv) {
  const args = {
    url: 'http://127.0.0.1:5000',
    path: '/api/events',
    key: '',
    secret: '',
    anonymous: false,
    devices: 10,
    concurrency: 10,
    duration: 20,
    count: 0,
    p99: 500,
    token: '',
  };

  for (let i = 0; i < argv.length; i++) {
    const name = argv[i];
    const value = argv[i + 1];
    switch (name) {
      case '--url': args.url = value; i++; break;
      case '--path': args.path = value; i++; break;
      case '--key': args.key = value; i++; break;
      case '--secret': args.secret = value; i++; break;
      case '--token': args.token = value; i++; break;
      case '--anonymous': args.anonymous = true; break;
      case '--devices': args.devices = Number(value); i++; break;
      case '--concurrency': args.concurrency = Number(value); i++; break;
      case '--duration': args.duration = Number(value); i++; break;
      case '--count': args.count = Number(value); i++; break;
      case '--p99': args.p99 = Number(value); i++; break;
      case '--help':
      case '-h': printHelp(); process.exit(0); break;
      default:
        console.error(`未知参数：${name}`);
        printHelp();
        process.exit(2);
    }
  }

  if (!args.anonymous && (!args.key || !args.secret)) {
    console.error('缺少 --key / --secret（或显式使用 --anonymous）');
    process.exit(2);
  }

  return args;
}

function printHelp() {
  console.log(`EFBM 上报压测

  --url <地址>          默认 http://127.0.0.1:5000
  --path <路径>         默认 /api/events
  --key <AppKey>        设备凭据（Anonymous 模式可省）
  --secret <AppSecret>  设备密钥
  --token <查询密钥>    仅压测查询接口时使用，作为 X-Query-Key
  --anonymous           不带凭据（内网 Anonymous 模式）
  --devices <N>         设备数量，默认 10；每台设备编号 FBOX-LT-001...
  --concurrency <N>     并发请求数，默认 10
  --duration <秒>       压测时长，默认 20；与 --count 二选一
  --count <N>           固定请求总数，设置后忽略 --duration
  --p99 <毫秒>          P99 目标，默认 500，超出时提示但不改变退出码
`);
}

const EVENTS = [
  { name: 'ORDER_STARTED', weight: 1 },
  { name: 'ORDER_PROGRESS', weight: 6 },
  { name: 'ORDER_COMPLETED', weight: 3 },
  { name: 'DEVICE_STATUS_CHANGED', weight: 2 },
  { name: 'FAULT_OCCURRED', weight: 1 },
  { name: 'FAULT_CLEARED', weight: 1 },
];

function pickEvent() {
  const total = EVENTS.reduce((sum, e) => sum + e.weight, 0);
  let roll = Math.random() * total;
  for (const e of EVENTS) {
    roll -= e.weight;
    if (roll <= 0) return e.name;
  }
  return EVENTS[0].name;
}

function deviceId(index) {
  return 'FBOX-LT-' + String(index + 1).padStart(3, '0');
}

function buildPayload(index, seq) {
  const event = pickEvent();
  const device = deviceId(index);
  const orderId = 'WO-LT-' + String(1 + (seq % 5)).padStart(4, '0');

  const data = {};
  if (event.startsWith('ORDER_')) {
    data.orderId = orderId;
    data.boxId = 'BOX-LT-' + String(seq % 20).padStart(3, '0');
    data.planQty = 1000;
    data.completedQty = seq % 1000;
  }
  if (event === 'DEVICE_STATUS_CHANGED') data.status = seq % 2 ? 'RUNNING' : 'IDLE';
  if (event === 'FAULT_OCCURRED') data.message = '压测：模拟故障 ' + seq;
  if (event === 'FAULT_CLEARED') data.message = '压测：故障恢复 ' + seq;

  return JSON.stringify({
    event,
    deviceId: device,
    timestamp: new Date(Date.now() - (seq % 100) * 1000).toISOString(),
    data,
  });
}

function percentile(sorted, p) {
  if (sorted.length === 0) return 0;
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return sorted[index];
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const target = new URL(args.path, args.url);
  const lib = target.protocol === 'https:' ? https : http;
  const agent = new lib.Agent({ keepAlive: true, maxSockets: args.concurrency });

  const latencies = [];
  const statusCounts = new Map();
  let sent = 0;
  let seq = 0;
  let inflight = 0;
  let finished = 0;
  let firstError = null;

  // 固定条数模式不设时间上限，否则会被默认时长提前截断
  const deadline = args.count > 0 ? Number.MAX_SAFE_INTEGER : Date.now() + args.duration * 1000;
  const total = args.count > 0 ? args.count : Number.MAX_SAFE_INTEGER;

  function post(payload, index, startedAt) {
    return new Promise((resolve) => {
      const body = Buffer.from(payload, 'utf8');
      const options = {
        method: 'POST',
        hostname: target.hostname,
        port: target.port || (target.protocol === 'https:' ? 443 : 80),
        path: target.pathname + target.search,
        agent,
        headers: {
          'Content-Type': 'application/json; charset=utf-8',
          'Content-Length': body.length,
        },
      };

      if (args.token) options.headers['X-Query-Key'] = args.token;
      if (!args.anonymous) {
        options.headers['X-App-Key'] = args.key;
        options.headers['X-App-Secret'] = args.secret;
      }

      const req = lib.request(options, (res) => {
        res.resume();
        res.on('end', () => {
          const cost = Date.now() - startedAt;
          latencies.push(cost);
          statusCounts.set(res.statusCode, (statusCounts.get(res.statusCode) || 0) + 1);
          if (res.statusCode >= 500 && !firstError) firstError = `HTTP ${res.statusCode}`;
          resolve();
        });
      });

      req.on('error', (err) => {
        latencies.push(Date.now() - startedAt);
        statusCounts.set('ERR', (statusCounts.get('ERR') || 0) + 1);
        if (!firstError) firstError = err.code || err.message;
        resolve();
      });

      req.write(body);
      req.end();
    });
  }

  async function worker(index) {
    while (Date.now() < deadline) {
      if (finished >= total) return;
      if (finished + inflight >= total) return;

      const localSeq = seq++;
      inflight++;
      sent++;
      await post(buildPayload(index, localSeq), index, Date.now());
      inflight--;
      finished++;
    }
  }

  console.log(`压测开始：${target.href} 设备=${args.devices} 并发=${args.concurrency} ${args.count > 0 ? '总请求=' + args.count : '时长=' + args.duration + 's'}`);
  const startedAt = Date.now();

  await Promise.all(Array.from({ length: args.concurrency }, (_, i) => worker(i % args.devices)));

  const elapsed = (Date.now() - startedAt) / 1000;
  latencies.sort((a, b) => a - b);

  const failed = [...statusCounts.entries()].filter(([code]) => code === 'ERR' || Number(code) >= 400)
    .reduce((sum, [, n]) => sum + n, 0);
  const fiveXX = [...statusCounts.entries()].filter(([code]) => Number(code) >= 500).reduce((sum, [, n]) => sum + n, 0);

  console.log('');
  console.log('==== 结果 ====');
  console.log(`请求总数：${sent}`);
  console.log(`耗时：${elapsed.toFixed(2)}s    QPS：${(sent / elapsed).toFixed(1)}`);
  console.log(`延迟 P50=${percentile(latencies, 50)}ms  P95=${percentile(latencies, 95)}ms  P99=${percentile(latencies, 99)}ms  max=${latencies[latencies.length - 1] || 0}ms`);
  console.log('状态码分布：' + [...statusCounts.entries()].sort().map(([c, n]) => `${c}×${n}`).join('  '));
  console.log(`失败：${failed}    5xx：${fiveXX}`);
  if (firstError) console.log(`首个错误：${firstError}`);

  const p99 = percentile(latencies, 99);
  if (p99 > args.p99) console.log(`⚠️ P99 ${p99}ms 超过目标 ${args.p99}ms`);

  agent.destroy();
  process.exit(failed > 0 || fiveXX > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  process.exit(2);
});
