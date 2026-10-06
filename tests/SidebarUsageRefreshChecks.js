// Run with: node tests/SidebarUsageRefreshChecks.js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../scripts/sidebar_usage.js'), 'utf8');
async function check(mode) {
  const timers = new Map();
  const storage = new Map();
  let nextTimer = 0, calls = 0, aborted = false;
  const context = vm.createContext({
    window: { dispatchEvent() {} }, CustomEvent: class {}, AbortController,
    localStorage: { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v) },
    console: { warn() {} },
    setTimeout(fn, delay) { const id = ++nextTimer; timers.set(id, { fn, delay }); return id; },
    clearTimeout(id) { timers.delete(id); },
    client: { safeGet(url, options) {
      calls++;
      options.signal.addEventListener('abort', () => { aborted = true; });
      if (mode === 'timeout' && calls === 1) return new Promise(() => {});
      if (mode === 'error' && calls === 1) return Promise.reject(new Error('Offline'));
      return Promise.resolve({ rate_limit: { primary_window: { used_percent: 18, limit_window_seconds: 604800 } } });
    } }
  });
  vm.runInContext(source.slice(0, source.indexOf('const controller =')), context);
  vm.runInContext('apiClient = client; widget = {innerHTML:"", querySelector:()=>null}', context);
  const api = vm.runInContext('({refresh, render, cacheKey})', context);
  const first = api.refresh();
  await api.refresh();
  assert.equal(calls, 1, 'Overlapping refreshes must share one request');
  if (mode === 'timeout') {
    [...timers.values()].find(t => t.delay === 15000).fn();
  }
  await first;
  if (mode !== 'success') {
    assert.ok(vm.runInContext('widget.innerHTML', context).includes('Unavailable'), 'Initial failure is visible');
    assert.ok(!vm.runInContext('widget.innerHTML', context).includes('Loading…'), 'Failure must not remain loading');
    const retry = [...timers.values()].find(t => t.delay === 5000);
    assert.ok(retry, 'First retry is scheduled in five seconds');
    if (mode === 'timeout') assert.ok(aborted, 'Timed out request is aborted');
    vm.runInContext('apiClient = client', context);
    await retry.fn();
  }
  const data = JSON.parse(storage.get(api.cacheKey));
  assert.equal(data.limits[0].windows[0].remaining, 82);
  assert.ok(!vm.runInContext('widget.innerHTML', context).includes('Unavailable'), 'Recovery clears the error');
  assert.equal(timers.size, 0, 'Success clears retry and deadline timers');
}
(async () => {
  for (const mode of ['success', 'error', 'timeout']) await check(mode);
  console.log('Usage refresh checks passed: success, deduplication, failure/retry, timeout/abort, and recovery.');
})().catch(error => { console.error(error); process.exitCode = 1; });
