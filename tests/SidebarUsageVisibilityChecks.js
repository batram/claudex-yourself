// Run with: node tests/SidebarUsageVisibilityChecks.js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../scripts/sidebar_usage.js'), 'utf8');
const storage = new Map();
const context = vm.createContext({
  window: { dispatchEvent() {} }, CustomEvent: class {},
  localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
  console
});
vm.runInContext(source.slice(0, source.indexOf('const controller =')), context);
const api = vm.runInContext('({ normalizeUsage, getAvailableLimits, setLimitVisible, isLimitVisible, render, cacheKey })', context);
const limit = { primary_window: { limit_window_seconds: 604800, used_percent: 12 } };
const response = { rate_limit: limit, future_rate_limit: limit, additional_rate_limits: [
  { limit_name: 'gpt-reserve', rate_limit: limit },
  { limit_name: 'Future model <unsafe>', rate_limit: limit },
  { limit_name: 'GPT-5.3-Codex-Spark', rate_limit: limit }
] };
const data = api.normalizeUsage(response);
assert.equal(data.limits.length, 5, 'Discover new top-level and additional limits without exclusions');
storage.set(api.cacheKey, JSON.stringify(data));
api.setLimitVisible('additional:gpt-reserve', false);
assert.equal(api.getAvailableLimits().find(item => item.name === 'gpt-reserve').visible, false);
assert.equal(api.getAvailableLimits().find(item => item.name.startsWith('Future model')).visible, true);
const later = api.normalizeUsage({ ...response, additional_rate_limits: [] });
storage.set(api.cacheKey, JSON.stringify(later));
assert.equal(api.getAvailableLimits().length, 2, 'Only currently available limits are listed');
storage.set(api.cacheKey, JSON.stringify(data));
assert.equal(api.getAvailableLimits().find(item => item.name === 'gpt-reserve').visible, false, 'A returning limit retains its preference');
vm.runInContext('widget = { innerHTML:"", querySelector: () => null }', context);
api.render(data);
let html = vm.runInContext('widget.innerHTML', context);
assert.ok(!html.includes('gpt-reserve'));
assert.ok(html.includes('Future model &lt;unsafe&gt;'), 'API labels must be escaped');
for (const item of api.getAvailableLimits()) api.setLimitVisible(item.id, false);
html = vm.runInContext('widget.innerHTML', context);
assert.ok(html.includes('No usage limits selected.') && !html.includes('Loading'), 'All hidden is not a loading state');
api.setLimitVisible('additional:gpt-reserve', true);
assert.ok(vm.runInContext('widget.innerHTML', context).includes('gpt-reserve'));
console.log('Usage visibility passed: discovery, preferences, returning limits, escaping, empty selection, and immediate rendering.');
