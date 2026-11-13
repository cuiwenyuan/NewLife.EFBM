// 用 Node 复现魔方后台登录（前端 jsencrypt：RSA PKCS#1 v1.5 + base64），拿到认证 Cookie。
// 只用于本地验证 MySQL 方言下后台页面能否正常渲染。
const http = require('http');
const crypto = require('crypto');

const BASE = process.argv[2] || 'http://127.0.0.1:5002';
const USER = process.argv[3] || 'admin';
const PASS = process.argv[4] || '123456';

function req(url, options = {}, body = null) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    const r = http.request(
      { hostname: u.hostname, port: u.port, path: u.pathname + u.search, method: options.method || 'GET', headers: options.headers || {} },
      (res) => {
        let data = '';
        res.on('data', (c) => (data += c));
        res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
      }
    );
    r.on('error', reject);
    if (body) r.write(body);
    r.end();
  });
}

function unescapeAttr(s) {
  return s.replace(/&#xD;/g, '\r').replace(/&#xA;/g, '\n').replace(/&#x2B;/g, '+').replace(/&amp;/g, '&').replace(/&quot;/g, '"');
}

(async () => {
  const page = await req(`${BASE}/Admin/User/Login`);
  const html = page.body;

  const keyMatch = html.match(/id="pkey_container"[^>]*value="([^"]*)"/);
  const pkeyMatch = html.match(/name="pkey"\s+value="([^"]*)"/);
  if (!keyMatch || !pkeyMatch) {
    console.error('未找到公钥或 pkey 字段');
    process.exit(1);
  }

  const pubKey = unescapeAttr(keyMatch[1]);
  const pkey = pkeyMatch[1];

  const encrypted = crypto
    .publicEncrypt({ key: pubKey, padding: crypto.constants.RSA_PKCS1_PADDING }, Buffer.from(PASS, 'utf8'))
    .toString('base64');

  const form = new URLSearchParams({
    username: USER,
    pkey,
    pubKey,
    password: encrypted,
    remember: 'true',
  }).toString();

  const res = await req(`${BASE}/Admin/User/Login`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Content-Length': Buffer.byteLength(form),
      Cookie: (page.headers['set-cookie'] || []).map((c) => c.split(';')[0]).join('; '),
    },
  }, form);

  const cookies = (res.headers['set-cookie'] || []).map((c) => c.split(';')[0]);
  console.log('LOGIN_STATUS=' + res.status);
  console.log('LOCATION=' + (res.headers.location || ''));
  console.log('COOKIES=' + cookies.join('; '));
})();
