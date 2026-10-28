// ==ClaudexUserScript==
// @name          Codex updates
// @id            codex_updates
// @version       1.6.0
// @description   Shows Codex updates and the progress of the external claudex update worker.
// @run-at        renderer-ready
// @platform      windows
// @grant         none
// @codex-tested  26.1002.6548.0
// @codex-tested  26.1016.6573.0
// @codex-tested  26.1023.6123.0
// ==/ClaudexUserScript==
const key = Symbol.for('claudex-yourself.codex-updates');
window[key]?.uninstall?.();
const registry = Symbol.for('claudex-yourself.userscript-registry');
const id = 'codex_updates';
const optionsKey = 'claudex-yourself.codex-updates.options.v1';
let nativeHideStyle;
const getOptions = () => {
  let saved;
  try { saved = JSON.parse(localStorage.getItem(optionsKey) || '{}'); } catch {}
  return { hideUnlessReady:saved?.hideUnlessReady !== false, hideNative:saved?.hideNative === true };
};
const syncNativeVisibility = () => {
  if (!root || !getOptions().hideNative) { nativeHideStyle?.remove(); nativeHideStyle = null; return; }
  if (nativeHideStyle) return;
  nativeHideStyle = document.createElement('style');
  nativeHideStyle.dataset.claudexHideNativeUpdates = 'true';
  // Native rail ready/progress wrappers and the older footer/header button.
  // Structure constraints avoid matching chat titles or settings update actions.
  nativeHideStyle.textContent = `
    span.no-drag:has(> button.sidebar-item[aria-label="Update available"] > span.bg-blue),
    span.no-drag.sidebar-item[role="progressbar"]:has(> span.bg-blue),
    button.no-drag.bg-chart-blue:is([aria-label="Update"],[aria-label="Installing"],[aria-label^="Downloading"]) { display:none!important; }
  `;
  document.head.append(nativeHideStyle);
};
const setOption = (name, enabled) => {
  if (!['hideUnlessReady', 'hideNative'].includes(name)) return;
  localStorage.setItem(optionsKey, JSON.stringify({ ...getOptions(), [name]:Boolean(enabled) }));
  syncNativeVisibility();
  draw();
};
const renderSettings = container => {
  container.replaceChildren();
  const options = getOptions();
  for (const [name, title, description] of [
    ['hideUnlessReady', 'Hide when no update is ready', 'Show the Claudex update icon only when Update and restart now is available.'],
    ['hideNative', 'Hide native update icon', 'Hide the built-in Codex update indicator. This does not disable its updater.']
  ]) {
    const row = document.createElement('div'); row.className = 'claudex-userscript-row';
    const copy = document.createElement('div');
    const label = document.createElement('span'); label.className = 'claudex-userscript-name'; label.textContent = title;
    const help = document.createElement('p'); help.className = 'claudex-muted'; help.textContent = description;
    copy.append(label, help);
    const toggle = document.createElement('label'); toggle.className = 'claudex-userscript-switch';
    const input = document.createElement('input'); input.type = 'checkbox'; input.checked = options[name];
    input.disabled = !root; input.setAttribute('aria-label', title);
    input.addEventListener('change', () => setOption(name, input.checked));
    toggle.append(input, document.createElement('span')); row.append(copy, toggle); container.append(row);
  }
};
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
  const profile = document.querySelector('button[aria-label="Open profile menu"], button[aria-label*="profile menu" i]');
  // Shadow DOM keeps panel styles isolated; carry over the native rail button's
  // design values so size, squircle, and interaction colors follow Codex's theme.
  const nativeButton = document.querySelector('button[aria-label="Usage"]') || profile;
  if (nativeButton) {
    const native = getComputedStyle(nativeButton);
    const values = {
      size:native.width, radius:native.borderRadius, shape:native.getPropertyValue('corner-shape'),
      text:native.getPropertyValue('--button-text-color') || native.color,
      hoverText:native.getPropertyValue('--button-text-color-hover'),
      hover:native.getPropertyValue('--button-background-color'),
      active:native.getPropertyValue('--button-background-color-active'),
      focus:native.getPropertyValue('--button-ring-color'),
      focusOffset:native.getPropertyValue('--button-ring-offset')
    };
    for (const [name, value] of Object.entries(values)) {
      const property = `--claudex-update-${name}`;
      if (value && root.style.getPropertyValue(property) !== value) root.style.setProperty(property, value);
    }
  }
  const footer = profile?.parentElement?.parentElement?.parentElement?.parentElement;
  root.hidden = !footer || !profile.getClientRects().length || (getOptions().hideUnlessReady && badge.dataset.ready !== 'true');
  if (root.hidden && !panel.hidden) {
    panel.hidden = true;
    if (panel.matches(':popover-open')) panel.hidePopover();
    badge.setAttribute('aria-expanded', 'false');
  }
  const compact = footer?.parentElement?.getBoundingClientRect().width < 90;
  root.style.display = compact ? 'flex' : '';
  root.style.marginInline = compact ? 'auto' : '';
  // Set on the host: the app's global margin reset outranks shadow :host rules.
  root.style.marginBlockEnd = '8px';
  if (footer && (root.parentElement !== footer.parentElement || root.nextElementSibling !== footer)) footer.before(root);
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
  const newer = (a, b) => {
    if (!/^\d+\.\d+\.\d+\.\d+$/.test(a ?? '') || !/^\d+\.\d+\.\d+\.\d+$/.test(b ?? '')) return false;
    const left = a.split('.').map(Number), right = b.split('.').map(Number);
    const index = left.findIndex((value, i) => value !== right[i]);
    return index >= 0 && left[index] > right[index];
  };
  const announcedNewer = newer(check?.announcedVersion, check?.installed?.version);
  const storeAvailable = check?.store?.hasUpdate;
  const storeOnly = storeAvailable && !newer(check?.availableVersion, check?.installed?.version);
  const freshCheck = Date.parse(snapshot.checkFinishedAtUtc) > Date.parse(operation.updatedAtUtc);
  const failure = operation.state === 'failed';
  const currentFailure = failure && !freshCheck;
  const waitingMessage = check?.downloadBlockedReason || (operation.state === 'waiting' &&
    !(Date.parse(snapshot.checkFinishedAtUtc) >= Date.parse(operation.updatedAtUtc)) ? operation.message : null);
  const completed = operation.state === 'completed' && !freshCheck && !check?.updateAvailable && !announcedNewer && !storeAvailable &&
    (!operation.targetVersion || operation.targetVersion === check?.installed?.version);
  const label = busy || pending === 'install' ? 'Updating\u2026' : waitingMessage ? 'Update pending' : currentFailure ? 'Update failed' : check?.updateAvailable || storeAvailable ? 'Update available' : announcedNewer ? 'Update pending' : completed ? 'Updated' : 'Updates';
  badge.setAttribute('aria-label', label);
  badge.title = label;
  details.textContent = stale ? 'The update controller is disconnected. Restart Codex through claudex-yourself to reconnect.'
    : pending === 'install' ? 'Starting the updater\u2026 Codex will reopen automatically when installation finishes.'
    : busy ? operation.message
    : pending === 'check' || snapshot.checkRunning ? 'Checking for updates\u2026'
    : checkError ? `Could not check for updates: ${checkError}`
    : waitingMessage ? waitingMessage
    : currentFailure ? operation.message
    : storeOnly ? `Microsoft Store has a Codex update (installed: ${check.installed.version}). ${check.store.canSilentlyDownload ? 'Claudex will download it, verify its actual version, and compare it with direct downloads before restarting.' : 'Store downloads are currently unavailable under your Store settings or network configuration. Enable automatic Store updates or use the native updater.'}${check.sourceWarning ? ` ${check.sourceWarning}` : ''}`
    : check?.sourceWarning && !check.updateAvailable ? `Codex ${check.installed.version} is installed. ${announcedNewer ? `Codex ${check.announcedVersion} is announced, but no newer installable package was found. ` : ''}${check.sourceWarning}`
    : completed ? operation.message
    : check?.updateAvailable ? `Codex ${check.availableVersion} is available (installed: ${check.installed.version}). ${storeAvailable ? (check.store.canSilentlyDownload ? 'A Store update is also available; its verified version will be compared with direct downloads before installation. ' : 'A Store update is also reported, but Store downloads are unavailable under your current settings or network. ') : ''}The update is downloaded before Codex closes. Active work will be interrupted when it restarts.${check.sourceWarning ? ` ${check.sourceWarning}` : ''}`
    : announcedNewer ? `Codex ${check.announcedVersion} is announced (installed: ${check.installed.version}), but Claudex has not found a newer downloadable or staged package. Check again after the download sources catch up.`
    : check ? `Codex ${check.installed.version} is installed. No newer update was found in the checked sources.` : 'Checking for updates\u2026';
  if (failure && freshCheck && !busy && !pending && !snapshot.checkRunning && !checkError && !stale)
    details.textContent += ` Previous update attempt failed: ${operation.message}`;
  installButton.hidden = !check?.updateAvailable;
  installButton.disabled = stale || busy || pending || Boolean(waitingMessage) || snapshot.checkRunning;
  // Highlight only an action the user can actually take right now.
  badge.dataset.ready = String(!installButton.hidden && !installButton.disabled);
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
    :host { display:inline-flex; flex-shrink:0; width:var(--claudex-update-size,36px); height:var(--claudex-update-size,36px); font:12px system-ui,sans-serif; color:CanvasText; color-scheme:inherit; -webkit-app-region:no-drag; }
    :host([hidden]) { display:none!important; }
    button { font:inherit; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:8px; padding:6px 10px; background:Canvas; color:CanvasText; cursor:pointer; }
    button:focus-visible { outline:2px solid #3979f6; outline-offset:2px; }
    button:disabled { opacity:.55; cursor:default; }
    #badge { box-sizing:border-box; position:relative; display:flex; align-items:center; justify-content:center; width:100%; height:100%; padding:0; border:0; border-radius:var(--claudex-update-radius,12.5px); corner-shape:var(--claudex-update-shape,superellipse(1.5)); background:transparent; color:var(--claudex-update-text,var(--color-text-tertiary,GrayText)); cursor:var(--cursor-interaction,pointer); }
    #badge svg { width:28px; height:28px; }
    #badge:hover, #badge[aria-expanded="true"] { background:var(--claudex-update-hover,color-mix(in srgb,CanvasText 5%,transparent)); color:var(--claudex-update-hoverText,CanvasText); }
    #badge:active { background:var(--claudex-update-active,color-mix(in srgb,CanvasText 12%,transparent)); }
    #badge:focus-visible { outline:2px solid var(--claudex-update-focus,Highlight); outline-offset:var(--claudex-update-focusOffset,-1px); }
    #badge[data-ready="true"] .update-ring { stroke:var(--claudex-update-focus,Highlight); }
    #panel { position:fixed; inset:auto; margin:0; z-index:2147483000; width:min(320px,calc(100vw - 48px)); max-height:calc(100vh - 80px); overflow:auto; padding:16px; border:1px solid color-mix(in srgb,CanvasText 20%,transparent); border-radius:12px; background:Canvas; color:CanvasText; box-shadow:0 6px 24px #0003; }
    h2 { font-size:14px; margin:0 0 10px; } p { line-height:1.5; overflow-wrap:anywhere; } .actions { display:flex; gap:8px; flex-wrap:wrap; } #install { background:#2867dd; color:white; border-color:transparent; }
    [hidden] { display:none!important; }
  </style><button id="badge" aria-label="Updates" title="Updates" aria-controls="panel" aria-expanded="false"><svg aria-hidden="true" width="28" height="28" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><path class="update-ring" vector-effect="non-scaling-stroke" d="M26.825 9.75A12.5 12.5 0 1 0 28.5 16M27 3v7h-7"/><path vector-effect="non-scaling-stroke" d="M16 22V11m-4 4 4-4 4 4"/></svg></button>
  <section id="panel" popover="manual" aria-label="Codex updates" hidden><h2>Codex updates \u00b7 Claudex</h2><p id="details" role="status" aria-live="polite"></p><div class="actions"><button id="install">Update and restart now</button><button id="check">Check again</button><button id="close" aria-label="Close update panel">Close</button></div></section>`;
  panel = shadow.getElementById('panel'); badge = shadow.getElementById('badge'); details = shadow.getElementById('details');
  installButton = shadow.getElementById('install'); checkButton = shadow.getElementById('check');
  badge.onclick = () => { panel.hidden = !panel.hidden; draw(); };
  shadow.getElementById('close').onclick = () => { panel.hidden = true; draw(); badge.focus(); };
  shadow.addEventListener('keydown', event => { if (event.key === 'Escape') { panel.hidden = true; draw(); badge.focus(); } });
  installButton.onclick = () => { if (!installButton.disabled) { action = pending = 'install'; pendingSince = Date.now(); draw(); } };
  checkButton.onclick = () => { if (!checkButton.disabled) { action = pending = 'check'; pendingSince = Date.now(); draw(); } };
  document.body.append(root);
  syncNativeVisibility();
  observer = new MutationObserver(place);
  observer.observe(document.body, { childList:true, subtree:true });
  observer.observe(document.documentElement, { attributes:true, attributeFilter:['class', 'style', 'data-theme'] });
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
  nativeHideStyle?.remove(); nativeHideStyle = null;
  return { installed: false };
};
const controller = {
  id, install, uninstall, getOptions, setOption, renderSettings, get installed() { return Boolean(root?.isConnected); },
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
