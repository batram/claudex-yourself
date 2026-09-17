// ==ClaudexUserScript==
// @name          Codex updates
// @id            codex_updates
// @version       1.4.1
// @description   Shows Codex updates and the progress of the external claudex update worker.
// @run-at        renderer-ready
// @platform      windows
// @grant         none
// ==/ClaudexUserScript==
const key = Symbol.for('claudex-yourself.codex-updates');
window[key]?.uninstall?.();
const registry = Symbol.for('claudex-yourself.userscript-registry');
const id = 'codex_updates';
let root, action = null, pending = null, pendingSince = 0, lastSeen = Date.now();
let snapshot = { check: null, checkError: null, operation: { state: 'idle' } };
const busyStates = new Set(['checking', 'downloading', 'staging', 'closing', 'installing', 'restarting']);
let panel, badge, details, installButton, checkButton, timer, observer;
const dismiss = () => {
  if (!root || panel.hidden) return;
  panel.hidden = true;
  draw();
};
const dismissOutside = event => {
  // composedPath includes shadow contents, so interacting with the popup or its
  // trigger stays inside. Leave focus on whatever the user selects outside.
  if (!event.composedPath().includes(root)) dismiss();
};
const place = () => {
  if (!root) return;
  const help = document.querySelector('button[aria-label="Open help menu"]');
  root.hidden = !help || !help.getClientRects().length;
  if (help && (root.parentElement !== help.parentElement || root.nextElementSibling !== help)) help.before(root);
  if (!panel.hidden && !root.hidden) {
    const rect = badge.getBoundingClientRect();
    panel.style.left = `${Math.max(8, Math.min(rect.left, innerWidth - panel.offsetWidth - 8))}px`;
    panel.style.bottom = `${Math.max(8, innerHeight - rect.top + 8)}px`;
  }
};
const draw = () => {
  if (!root) return;
  const { check, checkError, operation } = snapshot;
  const stale = Date.now() - lastSeen > 45000;
  const busy = busyStates.has(operation.state);
  const waitingMessage = check?.downloadBlockedReason || (operation.state === 'waiting' &&
    !(Date.parse(snapshot.checkFinishedAtUtc) >= Date.parse(operation.updatedAtUtc)) ? operation.message : null);
  const completed = operation.state === 'completed' && !check?.updateAvailable;
  const label = busy || pending === 'install' ? 'Updating\u2026' : waitingMessage ? 'Update pending' : operation.state === 'failed' ? 'Update failed' : check?.updateAvailable ? 'Update available' : completed ? 'Updated' : 'Updates';
  badge.setAttribute('aria-label', label);
  badge.title = label;
  badge.dataset.attention = String(Boolean(check?.updateAvailable || waitingMessage || operation.state === 'failed'));
  details.textContent = stale ? 'The update controller is disconnected. Restart Codex through claudex-yourself to reconnect.'
    : pending === 'install' ? 'Starting the updater\u2026 Codex will reopen automatically when installation finishes.'
    : busy ? operation.message
    : pending === 'check' || snapshot.checkRunning ? 'Checking for updates\u2026'
    : checkError ? `Could not check for updates: ${checkError}`
    : waitingMessage ? waitingMessage
    : check?.sourceWarning && !check.updateAvailable ? `Codex ${check.installed.version} is installed. ${check.sourceWarning}`
    : operation.state === 'failed' ? operation.message
    : completed ? operation.message
    : check?.updateAvailable ? `Codex ${check.availableVersion} is available (installed: ${check.installed.version}). The update is downloaded before Codex closes. Active work will be interrupted when it restarts.`
    : check ? `You have the latest available download: Codex ${check.installed.version}.` : 'Checking for updates\u2026';
  installButton.hidden = !check?.updateAvailable;
  installButton.disabled = stale || busy || pending || Boolean(waitingMessage) || snapshot.checkRunning;
  checkButton.disabled = stale || busy || Boolean(pending) || snapshot.checkRunning;
  installButton.textContent = busy || pending === 'install' ? 'Updating\u2026' : 'Update and restart now';
  checkButton.textContent = pending === 'check' || snapshot.checkRunning ? 'Checking\u2026' : 'Check again';
  badge.setAttribute('aria-expanded', String(!panel.hidden));
  if (!panel.hidden && !panel.matches(':popover-open')) panel.showPopover();
  if (panel.hidden && panel.matches(':popover-open')) panel.hidePopover();
  place();
};
const install = () => {
  if (root) return { installed: true };
  root = document.createElement('div');
  root.id = 'claudex-codex-updates';
  const shadow = root.attachShadow({ mode: 'open' });
  // Isolated styles and textContent keep update messages out of the app's markup/style rules.
  shadow.innerHTML = `<style>
    :host { display:inline-flex; flex-shrink:0; width:32px; height:32px; font:12px system-ui,sans-serif; color:CanvasText; color-scheme:light dark; -webkit-app-region:no-drag; }
    :host([hidden]) { display:none!important; }
    button { font:inherit; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:8px; padding:6px 10px; background:Canvas; color:CanvasText; cursor:pointer; }
    button:focus-visible { outline:2px solid #3979f6; outline-offset:2px; }
    button:disabled { opacity:.55; cursor:default; }
    #badge { position:relative; display:flex; align-items:center; justify-content:center; width:32px; height:32px; padding:0; border:0; border-radius:6px; background:transparent; color:var(--color-text-tertiary, color-mix(in srgb,CanvasText 55%,transparent)); }
    #badge:hover, #badge[aria-expanded="true"] { background:color-mix(in srgb,CanvasText 7%,transparent); }
    #badge[data-attention="true"]::after { content:''; position:absolute; right:4px; top:4px; width:5px; height:5px; border-radius:50%; background:#3979f6; }
    #panel { position:fixed; inset:auto; margin:0; z-index:2147483000; width:min(320px,calc(100vw - 48px)); max-height:calc(100vh - 80px); overflow:auto; padding:16px; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:12px; background:Canvas; color:CanvasText; box-shadow:0 6px 24px #0003; }
    h2 { font-size:14px; margin:0 0 10px; } p { line-height:1.5; overflow-wrap:anywhere; } .actions { display:flex; gap:8px; flex-wrap:wrap; } #install { background:#2867dd; color:white; border-color:transparent; }
    [hidden] { display:none!important; }
  </style><button id="badge" aria-label="Updates" title="Updates" aria-controls="panel" aria-expanded="false"><svg aria-hidden="true" width="16" height="16" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M16.5 8a6.75 6.75 0 1 0-1.2 6.3M16.5 3.5V8H12M10 13V7m-2.5 2.5L10 7l2.5 2.5"/></svg></button>
  <section id="panel" popover="manual" aria-label="Codex updates" hidden><h2>Codex updates \u00b7 Claudex</h2><p id="details" role="status" aria-live="polite"></p><div class="actions"><button id="install">Update and restart now</button><button id="check">Check again</button><button id="close" aria-label="Close update panel">Close</button></div></section>`;
  panel = shadow.getElementById('panel'); badge = shadow.getElementById('badge'); details = shadow.getElementById('details');
  installButton = shadow.getElementById('install'); checkButton = shadow.getElementById('check');
  badge.onclick = () => { panel.hidden = !panel.hidden; draw(); };
  shadow.getElementById('close').onclick = () => { panel.hidden = true; draw(); badge.focus(); };
  shadow.addEventListener('keydown', event => { if (event.key === 'Escape') { panel.hidden = true; draw(); badge.focus(); } });
  installButton.onclick = () => { if (!installButton.disabled) { action = pending = 'install'; pendingSince = Date.now(); draw(); } };
  checkButton.onclick = () => { if (!checkButton.disabled) { action = pending = 'check'; pendingSince = Date.now(); draw(); } };
  document.body.append(root);
  observer = new MutationObserver(place);
  observer.observe(document.body, { childList:true, subtree:true });
  window.addEventListener('resize', place);
  window.addEventListener('scroll', place, true);
  document.addEventListener('pointerdown', dismissOutside, true);
  document.addEventListener('focusin', dismissOutside, true);
  window.addEventListener('blur', dismiss);
  timer = setInterval(draw, 5000);
  draw();
  return { installed: true };
};
const uninstall = () => {
  clearInterval(timer); observer?.disconnect();
  window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true);
  document.removeEventListener('pointerdown', dismissOutside, true);
  document.removeEventListener('focusin', dismissOutside, true);
  window.removeEventListener('blur', dismiss);
  root?.remove(); root = null; action = pending = null;
  return { installed: false };
};
const controller = {
  id, install, uninstall, get installed() { return Boolean(root?.isConnected); },
  takeAction() { const value = action; action = null; return value ?? 'none'; },
  update(value) {
    snapshot = value;
    // An old status poll is not an acknowledgement from the detached updater.
    const acknowledgedAt = pending === 'install' ? value.operation?.updatedAtUtc : value.checkFinishedAtUtc;
    const acknowledged = pending === 'check' || busyStates.has(value.operation?.state) || ['current', 'completed', 'failed', 'waiting'].includes(value.operation?.state);
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
