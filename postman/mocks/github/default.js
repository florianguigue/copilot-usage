// Synthetic fixtures based on the pinned GHCPSpendTray schema and GitHub billing docs.
// No real credentials or user data. Only the read-only routes used by the probe exist.
const http = require('http');
const counts = new Map();
http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const [, scenario, ...parts] = url.pathname.split('/');
  const path = '/' + parts.join('/');
  const send = (code, data, headers = {}) => {
    res.writeHead(code, { 'Content-Type': 'application/json', ...headers });
    res.end(typeof data === 'string' ? data : JSON.stringify(data));
  };
  if (req.method !== 'GET') return send(501, { message: 'Not modeled' });
  if (req.headers.authorization !== 'Bearer fake-local-test-token') return send(401, { message: 'Wrong fixture credential' });
  if (!req.headers['user-agent']?.startsWith('CopilotUsage.Probe/') || req.headers['x-github-api-version'] !== '2026-03-10')
    return send(400, { message: 'Required headers missing' });
  if (scenario === 'unauthorized') return send(401, { message: 'hidden-secret' });
  if (scenario === 'forbidden') return send(403, { message: 'hidden-secret' });
  if (scenario === 'sso') return send(403, {}, { 'X-GitHub-SSO': 'required; url=https://github.com/orgs/private/sso' });
  if (scenario === 'ratelimit') return send(403, {}, { 'X-RateLimit-Remaining': '0', 'Retry-After': '60' });
  if (scenario === 'notfound') return send(404, {});
  if (scenario === 'servererror') return send(503, {});
  if (scenario === 'html') return send(200, '<html>hidden-secret</html>', { 'Content-Type': 'text/html' });
  if (scenario === 'redirect') return send(302, {}, { Location: 'https://example.com/never-follow' });
  if (scenario === 'oversized') return send(200, 'x'.repeat(1024 * 1024 + 1));
  if (scenario === 'malformed') return send(200, '{');
  if (scenario === 'slow') return setTimeout(() => send(200, {}), 1500);
  if (path === '/user') return send(200, { login: 'simulation-user', id: 42, token: 'hidden-secret' });
  const now = new Date();
  if (path === '/copilot_internal/user') {
    const count = (counts.get(scenario) || 0) + 1;
    counts.set(scenario, count);
    if ((scenario === 'refreshfail' || scenario.startsWith('tray-refresh-')) && count % 2 === 0) return send(401, { message: 'hidden-secret' });
    const reset = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + (scenario === 'stale' ? 0 : 1), 1));
    const quota = { token_based_billing: scenario !== 'legacy', credits_used: scenario === 'decrease' && count % 2 === 0 ? 800 : 854.25,
      entitlement: 1500, unlimited: false, has_quota: true, timestamp_utc: now.toISOString() };
    if (scenario === 'noallocation') delete quota.entitlement;
    if (scenario === 'missing') delete quota.credits_used;
    return send(200, { copilot_plan: 'enterprise', quota_reset_date_utc: reset.toISOString(),
      quota_snapshots: { premium_interactions: quota }, enterprise_list: [{ id: 99, name: 'hidden-secret' }] });
  }
  if (path === '/users/simulation-user/settings/billing/ai_credit/usage') {
    if (url.searchParams.get('year') !== String(now.getUTCFullYear()) || url.searchParams.get('month') !== String(now.getUTCMonth() + 1))
      return send(400, { message: 'Expected current UTC month' });
    return send(200, { timePeriod: { year: now.getUTCFullYear(), month: now.getUTCMonth() + 1 }, usageItems: [] });
  }
  return send(501, { message: 'Route not modeled' });
}).listen(process.env.PORT || 4500, '127.0.0.1');
