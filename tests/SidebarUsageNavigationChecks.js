// Run with: node tests/SidebarUsageNavigationChecks.js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../scripts/sidebar_usage.js'), 'utf8');
new Function(source);
const navigation = source.slice(source.indexOf('const openUsageSettings ='), source.indexOf('const renderNavigationStatus ='));

async function check(mode) {
  let open = mode === 'already-open';
  let observed, timedOut, disconnected = false, cleared = false;
  const messages = [];
  const context = {
    document: { body: {}, querySelector: () => open ? {} : null },
    window: {
      location: { origin: 'app://-' },
      postMessage(message, origin) {
        messages.push({ message, origin });
        if (mode === 'throw') throw new Error('Transport failed');
      }
    },
    MutationObserver: class {
      constructor(callback) { observed = callback; }
      observe() {}
      disconnect() { disconnected = true; }
    },
    setTimeout(callback) { timedOut = callback; return 1; },
    clearTimeout() { cleared = true; }
  };
  const openSettings = vm.runInNewContext(navigation + '\nopenUsageSettings', context);
  const result = openSettings();
  if (mode === 'success') { open = true; observed(); }
  if (mode === 'timeout') timedOut();
  if (mode === 'throw' || mode === 'timeout') {
    await assert.rejects(result, mode === 'throw' ? /Transport failed/ : /within 5 seconds/);
  } else await result;
  if (mode === 'already-open') {
    assert.equal(messages.length, 0);
  } else {
    assert.equal(JSON.stringify(messages), JSON.stringify([{ message: { type: 'navigate-to-route', path: '/settings/usage' }, origin: 'app://-' }]));
    assert.ok(disconnected && cleared, 'Observer and timeout must be cleaned up');
  }
}

(async () => {
  for (const mode of ['success', 'already-open', 'timeout', 'throw']) await check(mode);
  console.log('Sidebar usage navigation: 4 scenarios passed (no bundle exports or preload bridge required).');
})().catch(error => { console.error(error); process.exitCode = 1; });
