// Live, read-only check of the enabled userscripts in every session renderer.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
async function evaluate(target, expression) {
  const socket = new WebSocket(target.webSocketDebuggerUrl);
  try {
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    const response = new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('CDP timed out')), 10000);
      socket.onmessage = event => {
        const message = JSON.parse(event.data);
        if (message.id !== 1) return;
        clearTimeout(timeout);
        if (message.error || message.result.exceptionDetails) reject(new Error(JSON.stringify(message)));
        else resolve(message.result.result.value);
      };
    });
    socket.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression, returnByValue: true, awaitPromise: true } }));
    return await response;
  } finally { socket.close(); }
}
async function main() {
  const targets = await (await fetch('http://127.0.0.1:9229/json/list')).json();
  const pages = targets.filter(target => {
    if (target.type !== 'page') return false;
    const url = new URL(target.url);
    return url.origin === 'null' && url.host === '-' && url.protocol === 'app:' && url.pathname === '/index.html'
      && (!url.search || /^\/(local|remote)\//.test(url.searchParams.get('initialRoute') || ''));
  });
  assert(pages.some(target => new URL(target.url).searchParams.has('initialRoute')), 'Open a session window before running this check');
  const status = JSON.parse(fs.readFileSync(path.join(process.env.APPDATA, 'claudex-yourself', 'autoload-status.json'), 'utf8'));
  assert.equal(status.state, 'watching');
  for (const target of pages) {
    const windowStatus = status.windows.find(window => window.target_id === target.id);
    assert(windowStatus, `Window ${target.id} has autoload status`);
    assert(windowStatus.scripts.every(script => script.state === 'completed'), JSON.stringify(windowStatus));
    const runtime = await evaluate(target, `({ title:document.title, scripts:[...(window[Symbol.for('claudex-yourself.userscript-registry')] || new Map())].map(([id, controller]) => ({id, installed:controller.installed})), settingsStyle: Boolean(document.querySelector('style[data-claudex-userscript-settings]')) })`);
    for (const id of ['sidebar_usage', 'hide_invite_a_friend', 'hide_pets_button', 'codex_updates']) {
      assert(runtime.scripts.some(script => script.id === id && script.installed), `${id} installed in ${target.id}`);
    }
    assert(runtime.settingsStyle, `Settings styles installed in ${target.id}`);
    console.log(JSON.stringify({ target: target.id, url: target.url, runtime }));
  }
  console.log('Live autoload window checks passed.');
}
if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
module.exports = { evaluate };
