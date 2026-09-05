// ==ClaudexUserScript==
// @name          Codex updates
// @id            codex_updates
// @version       1.1.0
// @description   Shows Codex updates and the progress of the external claudex update worker.
// @run-at        renderer-ready
// @platform      windows
// ==/ClaudexUserScript==
const key = Symbol.for('claudex-yourself.codex-updates');
window[key]?.uninstall?.();
const registry = Symbol.for('claudex-yourself.userscript-registry');
const id = 'codex_updates';
let root, action = null, pending = null, pendingSince = 0, lastSeen = Date.now();
let snapshot = { check: null, checkError: null, operation: { state: 'idle' } };
const busyStates = new Set(['checking', 'downloading', 'staging', 'closing', 'installing', 'restarting']);
let panel, badge, details, installButton, checkButton, timer;
const draw = () => {
  if (!root) return;
  const { check, checkError, operation } = snapshot;
  const stale = Date.now() - lastSeen > 45000;
  const busy = busyStates.has(operation.state);
  const completed = operation.state === 'completed' && !check?.updateAvailable;
  badge.textContent = busy || pending === 'install' ? 'Updating…' : operation.state === 'failed' ? 'Update failed' : check?.updateAvailable ? 'Update available' : completed ? 'Updated' : 'Updates';
  details.textContent = stale ? 'The update controller is disconnected. Restart Codex through claudex-yourself to reconnect.'
    : pending === 'install' ? 'Starting the updater… Codex will reopen automatically when installation finishes.'
    : operation.state === 'failed' || busy ? operation.message
    : pending === 'check' || snapshot.checkRunning ? 'Checking for updates…'
    : checkError ? `Could not check for updates: ${checkError}`
    : completed ? operation.message
    : check?.updateAvailable ? `Codex ${check.availableVersion} is available (installed: ${check.installed.version}). The update is downloaded before Codex closes. Active work will be interrupted when it restarts.`
    : check ? `Codex ${check.installed.version} is up to date.` : 'Checking for updates…';
  installButton.hidden = !check?.updateAvailable;
  installButton.disabled = stale || busy || pending;
  checkButton.disabled = stale || busy || Boolean(pending) || snapshot.checkRunning;
  installButton.textContent = busy || pending === 'install' ? 'Updating…' : 'Update and restart now';
  checkButton.textContent = pending === 'check' || snapshot.checkRunning ? 'Checking…' : 'Check again';
  badge.setAttribute('aria-expanded', String(!panel.hidden));
};
const install = () => {
  if (root) return { installed: true };
  root = document.createElement('div');
  root.id = 'claudex-codex-updates';
  const shadow = root.attachShadow({ mode: 'open' });
  // Isolated styles and textContent keep update messages out of the app's markup/style rules.
  shadow.innerHTML = `<style>
    :host { position:fixed; right:18px; bottom:12px; z-index:2147483000; font:12px system-ui,sans-serif; color:CanvasText; color-scheme:light dark; }
    button { font:inherit; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:8px; padding:6px 10px; background:Canvas; color:CanvasText; cursor:pointer; }
    button:focus-visible { outline:2px solid #3979f6; outline-offset:2px; }
    button:disabled { opacity:.55; cursor:default; }
    #panel { position:absolute; bottom:38px; right:0; width:min(320px,calc(100vw - 48px)); padding:16px; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:12px; background:Canvas; box-shadow:0 6px 24px #0003; }
    h2 { font-size:14px; margin:0 0 10px; } p { line-height:1.5; overflow-wrap:anywhere; } .actions { display:flex; gap:8px; flex-wrap:wrap; } #install { background:#2867dd; color:white; border-color:transparent; }
    [hidden] { display:none!important; }
  </style><button id="badge" aria-controls="panel" aria-expanded="false">Updates</button>
  <section id="panel" aria-label="Codex updates" hidden><h2>Codex updates · Claudex</h2><p id="details" role="status" aria-live="polite"></p><div class="actions"><button id="install">Update and restart now</button><button id="check">Check again</button><button id="close" aria-label="Close update panel">Close</button></div></section>`;
  panel = shadow.getElementById('panel'); badge = shadow.getElementById('badge'); details = shadow.getElementById('details');
  installButton = shadow.getElementById('install'); checkButton = shadow.getElementById('check');
  badge.onclick = () => { panel.hidden = !panel.hidden; draw(); };
  shadow.getElementById('close').onclick = () => { panel.hidden = true; draw(); badge.focus(); };
  shadow.addEventListener('keydown', event => { if (event.key === 'Escape') { panel.hidden = true; draw(); badge.focus(); } });
  installButton.onclick = () => { if (!installButton.disabled) { action = pending = 'install'; pendingSince = Date.now(); draw(); } };
  checkButton.onclick = () => { if (!checkButton.disabled) { action = pending = 'check'; pendingSince = Date.now(); draw(); } };
  document.body.append(root);
  timer = setInterval(draw, 5000);
  draw();
  return { installed: true };
};
const uninstall = () => { clearInterval(timer); root?.remove(); root = null; action = pending = null; return { installed: false }; };
const controller = {
  id, install, uninstall, get installed() { return Boolean(root?.isConnected); },
  takeAction() { const value = action; action = null; return value ?? 'none'; },
  update(value) {
    snapshot = value;
    // An old status poll is not an acknowledgement from the detached updater.
    const acknowledgedAt = pending === 'install' ? value.operation?.updatedAtUtc : value.checkFinishedAtUtc;
    const acknowledged = pending === 'check' || busyStates.has(value.operation?.state) || ['current', 'completed', 'failed'].includes(value.operation?.state);
    if (pending && acknowledged && Date.parse(acknowledgedAt) >= pendingSince) pending = null;
    lastSeen = Date.now(); draw(); return { installed: Boolean(root?.isConnected) };
  }
};
window[key] = controller;
(window[registry] ??= new Map()).set(id, controller);
window.dispatchEvent(new CustomEvent('claudex-userscript-registered', { detail: { id } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem('claudex-yourself.userscripts.v1') || '{}'); } catch {}
return preferences[id] === false ? uninstall() : install();
