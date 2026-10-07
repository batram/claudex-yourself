// ==ClaudexUserScript==
// @name          User script settings
// @id            userscript_settings
// @version       1.4.0
// @description   Adds reversible user-script switches to Codex Settings.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @codex-tested  26.803.10989.0
// @codex-tested  26.903.9818.0
// @codex-tested  26.908.9136.0
// @update-url    https://raw.githubusercontent.com/batram/claudex-yourself/master/scripts/userscript_settings.js
// @grant         none
// @codex-tested  26.924.2738.0
// @codex-tested  26.1002.6548.0
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.userscript-settings");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const preferencesKey = "claudex-yourself.userscripts.v1";
window[stateKey]?.uninstall?.();

const catalog = [
  { id: "sidebar_usage", name: "Sidebar usage", description: "Shows available usage limits in the sidebar rail." },
  { id: "hide_invite_a_friend", name: "Hide Invite a friend", description: "Hides the Invite a friend account-menu entry." },
  { id: "hide_pets_button", name: "Hide Mini button", description: "Hides the Show Mini / Hide Mini account-menu entry." },
  { id: "codex_updates", name: "Codex updates", description: "Checks and installs stable Windows Codex packages.", platforms:["windows"] }
];
const registry = window[registryKey] ??= new Map();
const readPreferences = () => {
  try { return JSON.parse(localStorage.getItem(preferencesKey) || "{}"); }
  catch { return {}; }
};
const platformName = navigator.userAgentData?.platform || navigator.platform || "";
const platform = /win/i.test(platformName) ? "windows" : /mac/i.test(platformName) ? "macos" : /linux/i.test(platformName) ? "linux" : "unknown";
const getCatalog = () => [...catalog, ...[...registry.keys()].filter(id =>
  id !== "userscript_settings" && !catalog.some(script => script.id === id)
).map(id => ({ id, name:registry.get(id)?.name || id, description:"Registered userscript." }))];
const scriptState = script => {
  const supported = !script.platforms || script.platforms.includes(platform);
  const controller = registry.get(script.id);
  const available = supported && typeof controller?.install === "function" && typeof controller?.uninstall === "function";
  return { available, enabled:available && controller.installed === true,
    status:!supported ? "Windows only — unavailable on this platform." : !available ? "Not loaded in this window." : controller.installed === true ? "Enabled" : "Disabled" };
};
const isEnabled = id => scriptState(getCatalog().find(script => script.id === id) || {id}).enabled;
const setEnabled = (id, enabled) => {
  const script = getCatalog().find(script => script.id === id);
  if (!script || !scriptState(script).available) return;
  const controller = registry.get(id);
  (enabled ? controller.install : controller.uninstall).call(controller);
  const preferences = readPreferences();
  preferences[id] = controller.installed === true;
  localStorage.setItem(preferencesKey, JSON.stringify(preferences));
  renderSources();
};

const style = document.createElement("style");
style.dataset.claudexUserscriptSettings = "true";
style.textContent = `
  .claudex-userscript-nav[hidden] { display:none !important; }
  [data-claudex-userscript-panel-open] button:not(.claudex-userscript-nav):not(:hover),
  .claudex-userscript-nav:not([aria-current='page']):not(:hover) { background-color:transparent !important; }
  .claudex-userscript-nav[aria-current='page'] { background-color:var(--color-primary-ghost-hover,var(--claudex-settings-selection,#8882)) !important; }
  .claudex-userscript-native-hidden { display:none !important; }
  .claudex-settings-scroll { flex:1 1 0%; min-height:0; min-width:0; overflow-y:auto; width:100%; }
  .claudex-userscript-panel { box-sizing:border-box; margin:0 auto; width:100%; max-width:800px; padding:24px 20px 36px; color:inherit; }
  .claudex-userscript-panel * { box-sizing:border-box; }
  .claudex-userscript-panel header { display:flex; align-items:flex-start; justify-content:space-between; gap:16px; margin-bottom:16px; }
  .claudex-userscript-panel h1 { margin:0; font-size:22px; line-height:1.3; font-weight:600; letter-spacing:-.5px; }
  .claudex-userscript-panel p { margin:3px 0 0; font-size:12px; line-height:1.5; overflow-wrap:anywhere; }
  .claudex-muted { color:var(--color-text-secondary,var(--color-token-text-secondary,#888)); }
  .claudex-source-manager { min-width:0; }
  .claudex-source-manager h2 { margin:16px 0 8px; font-size:16px; font-weight:600; }
  .claudex-source-manager h3, .claudex-source-manager h4 { margin:0; font-size:14px; line-height:1.5; font-weight:600; overflow-wrap:anywhere; }
  .claudex-script-card { border:1px solid var(--color-border,var(--color-token-border,#8884)); border-radius:10px; margin:0 0 8px; background:var(--color-background-panel,transparent); overflow:hidden; min-width:0; }
  .claudex-card-body { padding:10px 14px; }
  .claudex-card-heading { display:flex; align-items:flex-start; justify-content:space-between; gap:12px; }
  .claudex-card-copy { min-width:0; flex:1; }
  .claudex-card-heading .claudex-userscript-switch { margin-top:2px; }
  .claudex-card-meta { display:flex; flex-wrap:wrap; align-items:center; gap:8px; font-size:12px; }
  .claudex-version { font-variant-numeric:tabular-nums; padding:0; background:transparent; color:var(--color-text-secondary,var(--color-token-text-secondary,#888)); font-size:11px; white-space:nowrap; }
  .claudex-state { font-size:12px; color:var(--color-text-secondary,var(--color-token-text-secondary,#888)); }
  .claudex-update-available { color:var(--color-text-primary,#4f7cff); font-weight:500; }
  .claudex-card-footer { display:grid; grid-template-columns:auto minmax(0,1fr); align-items:start; border-top:1px solid var(--color-border,var(--color-token-border,#8883)); padding:6px 14px; }
  .claudex-card-footer > .claudex-update-row { grid-column:2; grid-row:1; justify-self:end; max-width:calc(100% - 120px); justify-content:flex-end; position:relative; z-index:1; }
  .claudex-userscript-panel .claudex-card-footer > details { grid-column:1 / -1; grid-row:1; margin:0; min-width:0; }
  .claudex-card-footer > details > summary { line-height:28px; }
  .claudex-card-footer > :is(.claudex-source-status,.claudex-source-review) { grid-column:1 / -1; }
  .claudex-script-card { container-type:inline-size; }
  .claudex-script-card[data-disabled=true] { background:transparent; }
  .claudex-script-card[data-disabled=true] .claudex-card-copy { opacity:.65; }
  .claudex-schedule-row { display:flex; align-items:center; flex-wrap:wrap; gap:6px 12px; margin-top:8px; font-size:12px; }
  .claudex-userscript-panel select { max-width:100%; min-width:0; border:1px solid var(--color-border,var(--color-token-border,#8884)); border-radius:6px; padding:4px 24px 4px 8px; font:inherit; color:inherit; background-color:var(--color-background-panel,Canvas); cursor:pointer; color-scheme:inherit; }
  .claudex-source-editor { padding:4px 0; }
  .claudex-source-editor > .claudex-schedule-row { margin-top:0; }
  @container (max-width:430px) {
    .claudex-card-footer { display:flex; flex-direction:column; gap:4px; }
    .claudex-card-footer > .claudex-update-row { order:1; width:100%; max-width:none; justify-content:flex-start; }
    .claudex-card-footer > details { order:2; width:100%; }
    .claudex-card-footer > :is(.claudex-source-status,.claudex-source-review) { order:3; }
  }
  .claudex-update-row { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:6px 10px; }
  .claudex-update-row p { margin:0; font-size:12px; }
  .claudex-source-actions { display:flex; flex-wrap:wrap; align-items:center; gap:6px; }
  .claudex-userscript-panel button, .claudex-userscript-panel a { font:inherit; font-size:12px; line-height:18px; padding:4px 8px; border:1px solid var(--color-border,var(--color-token-border,#8884)); border-radius:7px; color:inherit; background:transparent; cursor:pointer; text-decoration:none; }
  .claudex-userscript-panel button:hover, .claudex-userscript-panel a:hover { background:var(--color-primary-ghost-hover,#8882); }
  .claudex-userscript-panel button:disabled { opacity:.45; cursor:default; }
  .claudex-userscript-panel .claudex-primary { background:#3973e6; border-color:transparent; color:#fff; }
  .claudex-userscript-panel .claudex-primary:hover { background:#2c61c7; }
  .claudex-userscript-panel .claudex-add-toggle { white-space:nowrap; flex:none; }
  .claudex-userscript-panel input[type=url] { flex:1 1 220px; min-width:0; max-width:100%; width:100%; border:1px solid var(--color-border,var(--color-token-border,#8884)); border-radius:7px; padding:8px 10px; color:inherit; background:transparent; font-size:12px; }
  .claudex-userscript-panel :is(button,input,select,a,summary,pre):focus-visible { outline:2px solid #4f7cff; outline-offset:3px; }
  .claudex-userscript-panel details { margin-top:6px; font-size:12px; }
  .claudex-userscript-panel summary { cursor:pointer; color:var(--color-text-secondary,var(--color-token-text-secondary,#888)); width:fit-content; }
  .claudex-userscript-panel details .claudex-source-actions { margin-top:8px; }
  .claudex-userscript-panel pre { max-height:260px; overflow:auto; padding:12px; background:var(--color-background-panel,#8881); border:1px solid var(--color-border,var(--color-token-border,#8884)); border-radius:8px; font-size:11px; white-space:pre; }
  .claudex-source-meta { white-space:pre-wrap; }
  .claudex-source-autoload { display:flex; align-items:center; gap:8px; margin:14px 0; font-size:12px; }
  .claudex-source-review { margin-top:14px; padding-top:14px; border-top:1px solid var(--color-border,var(--color-token-border,#8884)); }
  .claudex-source-status:empty { display:none; }
  .claudex-source-status { font-size:12px !important; }
  .claudex-source-status[data-error=true] { color:var(--color-text-danger,#d35b56); }
  .claudex-connection { margin:0 0 16px !important; }
  .claudex-usage-options { margin-top:8px; padding-top:8px; border-top:1px solid var(--color-border,var(--color-token-border,#8883)); }
  .claudex-usage-options h4 { margin:0 0 2px; font-size:12px; font-weight:500; }
  .claudex-userscript-row { display:flex; align-items:center; justify-content:space-between; gap:16px; min-height:28px; padding:3px 0; font-size:13px; }
  .claudex-userscript-switch { position:relative; display:inline-block; width:34px; height:20px; flex:none; cursor:pointer; }
  .claudex-userscript-switch input { position:absolute; inset:0; margin:0; opacity:0; width:100%; height:100%; cursor:inherit; z-index:1; }
  .claudex-userscript-switch span { position:absolute; inset:0; border-radius:20px; background:#777; transition:background .15s; pointer-events:none; }
  .claudex-userscript-switch span:before { content:''; position:absolute; width:16px; height:16px; left:2px; top:2px; border-radius:50%; background:white; transition:transform .15s; }
  .claudex-userscript-switch input:disabled + span { opacity:.35; }
  .claudex-userscript-switch input:checked + span { background:#4f7cff; }
  .claudex-userscript-switch input:checked + span:before { transform:translateX(14px); }
  .claudex-userscript-switch input:focus-visible + span { outline:2px solid #4f7cff; outline-offset:3px; }
  @media (max-width:600px) {
    .claudex-userscript-panel { padding:16px 12px 28px; }
    .claudex-userscript-panel header { flex-wrap:wrap; gap:12px; }
    .claudex-userscript-panel h1 { font-size:22px; }
    .claudex-card-body { padding:10px 12px; } .claudex-card-footer { padding:6px 12px; }
    .claudex-source-actions input[type=url] { flex-basis:100%; }
  }
`;
document.head.appendChild(style);

// The local controller polls this queue. Downloaded code is never fetched/evaluated by the panel.
const sourceStateKey = Symbol.for("claudex-yourself.source-settings-state");
const sourceState = window[sourceStateKey] ??= { snapshot:null, connectedAt:0, queued:null, pending:null, checks:{}, drafts:{}, message:"", preview:null };
sourceState.openDetails ??= {};
sourceState.feedback ??= {};
sourceState.scheduleDrafts ??= {};
sourceState.checkErrors ??= {};
if (!sourceState.pending) for (const [key,message] of Object.entries(sourceState.feedback)) {
  if (!message.error && (!message.expiresAt || message.expiresAt < Date.now())) delete sourceState.feedback[key];
}
let sourceManager;
let sourceSignature = "";
const sourceConnected = () => Date.now() - sourceState.connectedAt < 7000;
const requestSource = (action, values = {}) => new Promise((resolve, reject) => {
  if (!sourceConnected()) return reject(new Error("The Claudex controller is disconnected. Reopen Codex through Claudex to reconnect."));
  if (sourceState.pending) return reject(new Error("Wait for the current operation to finish."));
  const id = crypto.randomUUID();
  const timer = setTimeout(() => {
    if (sourceState.pending?.id !== id) return;
    sourceState.pending = null;
    sourceState.queued = null;
    reject(new Error("The operation timed out. Check the installed version before retrying."));
  }, 180000);
  sourceState.pending = { id, resolve, reject, timer };
  sourceState.queued = { id, action, ...values };
});
const performSource = async (action, values, done, key = "claudex") => {
  sourceState.feedback[key] = {text:action === "check_script" || action === "check_claudex" ? "Checking…" : action === "preview" ? "Downloading preview…" : action === "install" ? "Installing…" : "Saving…"};
  const operation = requestSource(action, values);
  renderSources();
  try {
    const value = await operation;
    done(value);
    if (action.startsWith("check_")) delete sourceState.feedback[key];
    else sourceState.feedback[key] = {text:sourceState.message,expiresAt:Date.now()+4000};
  } catch (error) { sourceState.feedback[key] = {text:error.message,error:true,action,at:Date.now()}; }
  renderSources();
  if (action === "preview" && sourceState.preview && !sourceState.feedback[key].error) sourceManager?.querySelector(".claudex-source-review")?.scrollIntoView({block:"nearest"});
};
const sourceElement = (tag, text, className) => {
  const element = document.createElement(tag);
  if (text !== undefined) element.textContent = text;
  if (className) element.className = className;
  return element;
};
const sourceButton = (text, label, action, primary = false) => {
  const button = sourceElement("button", text, primary ? "claudex-primary" : undefined);
  button.type = "button";
  button.setAttribute("aria-label", label);
  button.dataset.focusKey = label;
  button.disabled = !sourceConnected() || Boolean(sourceState.pending);
  button.onclick = action;
  return button;
};
const disclosure = (key, title) => {
  const element = sourceElement("details");
  element.dataset.detailKey = key;
  element.open = Boolean(sourceState.openDetails[key]);
  element.append(sourceElement("summary", title));
  element.addEventListener("toggle", () => { if (element.isConnected) sourceState.openDetails[key] = element.open; });
  return element;
};
const feedback = (container, key) => {
  const message = sourceState.feedback[key] || (sourceState.checkErrors[key] ? {text:"Couldn’t check for updates.",error:true} : null);
  const status = sourceElement("p", message?.text || "", "claudex-source-status");
  status.dataset.feedbackKey = key;
  if (sourceState.checkErrors[key]) status.title = sourceState.checkErrors[key];
  status.dataset.error = String(Boolean(message?.error));
  status.setAttribute("role", "status"); status.setAttribute("aria-live", "polite");
  container.append(status);
};
const scheduleChoices = [
  {value:"startup",label:"On startup",onStartup:true,intervalMinutes:0},
  {value:"hourly",label:"Startup + hourly",onStartup:true,intervalMinutes:60},
  {value:"six_hours",label:"Startup + every 6 hours",onStartup:true,intervalMinutes:360},
  {value:"daily",label:"Startup + daily",onStartup:true,intervalMinutes:1440},
  {value:"weekly",label:"Startup + weekly",onStartup:true,intervalMinutes:10080},
  {value:"manual",label:"Manually",onStartup:false,intervalMinutes:0}
];
const scheduleChoice = schedule => scheduleChoices.find(choice => choice.onStartup === schedule?.onStartup && choice.intervalMinutes === schedule?.intervalMinutes);
const scheduleEditor = (container, name) => {
  const key = name || "claudex";
  const snapshot = sourceState.snapshot;
  const disk = snapshot?.scripts.find(s => s.id === name);
  const current = name ? disk?.schedule : snapshot?.defaultSchedule;
  const inherited = name && disk?.scheduleInherited !== false;
  const label = sourceElement("label",undefined,"claudex-schedule-row");
  label.append(sourceElement("span",name ? "Check updates" : "Automatic checks","claudex-muted"));
  const select = sourceElement("select");
  select.setAttribute("aria-label", name ? `${name} update check schedule` : "Default update check schedule");
  select.dataset.focusKey = "schedule:" + key;
  select.title = name ? "Override the default for this script. Repeating checks run while Codex is open." : "Default for Claudex and all scripts. Repeating checks run while Codex is open.";
  select.disabled = !sourceConnected() || !snapshot?.defaultSchedule || Boolean(sourceState.pending);
  if (name) {
    const option = sourceElement("option",`Use default (${scheduleChoice(snapshot?.defaultSchedule)?.label || "custom"})`);
    option.value = "inherit"; select.append(option);
  }
  for (const choice of scheduleChoices) { const option = sourceElement("option",choice.label); option.value=choice.value; select.append(option); }
  let selected = inherited ? "inherit" : scheduleChoice(current)?.value;
  if (!selected && current) {
    const option = sourceElement("option",`${current.onStartup ? "Startup + " : ""}every ${current.intervalMinutes} minutes`);
    option.value="custom";select.append(option);selected="custom";
  }
  select.value = sourceState.scheduleDrafts[key] ?? selected ?? "startup";
  select.onchange = async () => {
    const value = select.value;
    const choice = scheduleChoices.find(item=>item.value===value) || scheduleChoices[0];
    sourceState.scheduleDrafts[key] = value;
    await performSource("set_schedule",{name:name || null,onStartup:choice.onStartup,intervalMinutes:choice.intervalMinutes,inherit:value==="inherit"},result=>{
      if (name) {
        const target=sourceState.snapshot?.scripts.find(s=>s.id===name);
        if (target) {target.schedule=result.schedule;target.scheduleInherited=result.inherited;}
      } else if (sourceState.snapshot) {
        sourceState.snapshot.defaultSchedule=result.schedule;
        for(const script of sourceState.snapshot.scripts) if(script.scheduleInherited)script.schedule=result.schedule;
      }
      sourceState.message="Check schedule saved.";
    },key);
    delete sourceState.scheduleDrafts[key];renderSources();
  };
  label.append(select);container.append(label);
};
const sourceUrlEditor = (container, key, value, name) => {
  const details = disclosure("source:" + key, "Update settings");
  const editor = sourceElement("div",undefined,"claudex-source-editor");
  if (name) scheduleEditor(editor,name);
  const field = sourceElement("input");
  field.type = "url";
  field.setAttribute("aria-label", `${name || "Claudex"} update source URL`);
  field.dataset.focusKey = "url:" + key;
  field.placeholder = "https://…";
  field.value = sourceState.drafts[key] ?? value ?? "";
  field.oninput = () => { sourceState.drafts[key] = field.value; };
  const row = sourceElement("div", undefined, "claudex-source-actions");
  row.append(field, sourceButton("Save URL", `Save ${name || "Claudex"} update source`, () => performSource("set_source", { name:name || null, url:field.value }, result => {
    sourceState.message = "Update source saved.";
    delete sourceState.checks[key];
    if (name) {
      const script = sourceState.snapshot?.scripts.find(item => item.id === name);
      if (script) {script.url = result.url;script.check=null;}
    } else if (sourceState.snapshot) {sourceState.snapshot.claudexUrl = result.url;sourceState.snapshot.claudexCheck=null;}
    delete sourceState.checkErrors[key];
  }, key)));
  editor.append(row); details.append(editor); container.append(details);
};
const reviewSource = (url, name) => performSource("preview", { url, name }, value => {
  sourceState.preview = value;
  sourceState.previewTarget = name || "add";
  sourceState.previewAutoload = value.installedVersion ? Boolean(sourceState.snapshot?.scripts.find(s => s.id === value.metadata.id)?.autoload) : true;
  sourceState.message = "Review this version before installing.";
}, name || "add");
const renderReview = (container, key) => {
  const preview = sourceState.preview;
  if (!preview || sourceState.previewTarget !== key) return;
  const review = sourceElement("section", undefined, "claudex-source-review");
  review.setAttribute("aria-label", "Userscript source review");
  review.append(sourceElement("h4", `Review ${preview.metadata.name} · ${preview.metadata.version}`));
  review.append(sourceElement("p", preview.metadata.description, "claudex-muted"));
  review.append(sourceElement("p", `${preview.url}\nDeclared grants: ${preview.metadata.grants.join(", ")}\n${preview.compatibility.tested ? "Tested on this Codex build." : preview.compatibility.platformSupported ? "Not yet tested on this Codex build." : "Not supported on this platform."}`, "claudex-source-meta claudex-muted"));
  const code = disclosure("review:" + preview.previewId, "Read complete source");
  const pre = sourceElement("pre", preview.source); pre.tabIndex = 0; code.append(pre); review.append(code);
  review.append(sourceElement("p", "Scripts can access your signed-in Codex session. Install only source you trust.", "claudex-muted"));
  const label = sourceElement("label", undefined, "claudex-source-autoload");
  const autoload = sourceElement("input"); autoload.type = "checkbox";
  autoload.dataset.focusKey = "preview-autoload";
  autoload.checked = sourceState.previewAutoload;
  autoload.onchange = () => { sourceState.previewAutoload = autoload.checked; };
  label.append(autoload, document.createTextNode("Enable on future controlled launches")); review.append(label);
  const actions = sourceElement("div", undefined, "claudex-source-actions");
  const install = sourceButton(preview.installedVersion ? "Update and run" : "Install and run", "Install reviewed userscript and run now", () => performSource("install", { previewId:preview.previewId, autoload:sourceState.previewAutoload }, result => {
    sourceState.preview = null;
    delete sourceState.checks[result.name];
    const failures = (Array.isArray(result.activation) ? result.activation : []).filter(item => !item.activated);
    sourceState.message = `${result.name} ${result.version} saved.${result.activationError ? " Activation failed: " + result.activationError : failures.length ? " Activation failed: " + failures.map(item => item.error).join("; ") : " Activated in open windows."}`;
  }, key), true);
  install.disabled ||= !preview.compatibility.platformSupported;
  const cancel = sourceButton("Cancel", "Cancel userscript preview", () => {
    sourceState.preview = null; delete sourceState.feedback[key]; renderSources();
    const card = [...sourceManager.querySelectorAll("[data-source-id]")].find(e => e.dataset.sourceId === key);
    card?.querySelector("button[aria-label^='Review '], button[aria-label='Preview userscript URL']")?.focus({preventScroll:true});
  });
  cancel.disabled = Boolean(sourceState.pending);
  actions.append(install, cancel); review.append(actions); container.append(review);
};
const makeSwitch = (label, checked, disabled, change) => {
  const wrapper = sourceElement("label", undefined, "claudex-userscript-switch");
  const input = sourceElement("input"); input.type = "checkbox";
  input.setAttribute("aria-label", label); input.dataset.focusKey = label;
  input.checked = checked; input.disabled = disabled;
  input.onchange = () => change(input.checked);
  wrapper.append(input, sourceElement("span")); return wrapper;
};
const renderSources = () => {
  if (!sourceManager) return;
  // Preserve editing, open disclosures and scroll position across controller heartbeats/results.
  const focused = sourceManager.contains(document.activeElement) ? document.activeElement.dataset.focusKey : null;
  const scrollTop = activeScroller?.scrollTop;
  for (const details of sourceManager.querySelectorAll("details[data-detail-key]")) sourceState.openDetails[details.dataset.detailKey] = details.open;
  usageOptions = undefined;
  const snapshot = sourceState.snapshot;
  sourceManager.replaceChildren();
  if (!sourceConnected()) sourceManager.append(sourceElement("p", "The update controller is disconnected. Reopen through Claudex to check versions or install scripts. Your switches still work.", "claudex-connection claudex-muted"));
  if (sourceState.addOpen) {
    const add = sourceElement("section", undefined, "claudex-script-card claudex-card-body");
    add.dataset.sourceId = "add";
    add.append(sourceElement("h3", "Add a userscript"), sourceElement("p", "Paste a GitHub file link or a direct script URL to review it before installing.", "claudex-muted"));
    const url = sourceElement("input"); url.type = "url"; url.placeholder = "https://…/my-script.js";
    url.setAttribute("aria-label", "New userscript URL"); url.dataset.focusKey = "url:add";
    url.value = sourceState.drafts.add || "";
    url.oninput = () => { sourceState.drafts.add = url.value; };
    const actions = sourceElement("div", undefined, "claudex-source-actions"); actions.style.marginTop = "12px";
    actions.append(url, sourceButton("Preview script", "Preview userscript URL", () => reviewSource(url.value), true));
    add.append(actions); feedback(add,"add"); renderReview(add,"add"); sourceManager.append(add);
  }
  const global = sourceElement("section", undefined, "claudex-script-card claudex-card-body"); global.dataset.sourceId = "claudex";
  const release = sourceState.checks.claudex;
  const globalRow = sourceElement("div", undefined, "claudex-update-row");
  const copy = sourceElement("div");
  const globalHeading = sourceElement("div", undefined, "claudex-card-meta"); globalHeading.style.marginTop = "0";
  globalHeading.append(sourceElement("h3", "Claudex Yourself"));
  if (snapshot) globalHeading.append(sourceElement("span", "v" + snapshot.version, "claudex-version"));
  copy.append(globalHeading);
  if (release?.updateAvailable) copy.append(sourceElement("p",`Version ${release.availableVersion} is available.`,"claudex-update-available"));
  const actions = sourceElement("div", undefined, "claudex-source-actions");
  actions.append(sourceButton("Check now", "Check Claudex release", () => performSource("check_claudex", {}, result => {
    sourceState.checks.claudex = result; sourceState.message = result.updateAvailable ? `Version ${result.availableVersion} is available.` : "You have the latest version at this source.";
  }, "claudex")));
  if (release?.updateAvailable && /^https?:\/\//i.test(release.downloadUrl)) {
    const link = sourceElement("a", "Open release"); link.href = release.downloadUrl; link.target = "_blank"; link.rel = "noopener noreferrer"; actions.append(link);
  }
  globalRow.append(copy, actions); global.append(globalRow); scheduleEditor(global); feedback(global,"claudex"); sourceUrlEditor(global,"claudex",snapshot?.claudexUrl); sourceManager.append(global);
  sourceManager.append(sourceElement("h2", "Your scripts"));
  const installed = new Map((snapshot?.scripts || []).map(s => [s.id,s]));
  const scripts = getCatalog().filter(s => !s.platforms || s.platforms.includes(platform) || installed.has(s.id));
  for (const s of installed.values()) if (!scripts.some(item => item.id === s.id)) scripts.push({id:s.id,name:s.name,description:s.id === "userscript_settings" ? "Manages your scripts, sources and updates." : "Custom userscript."});
  for (const script of scripts) {
    const disk = installed.get(script.id);
    const state = scriptState(script);
    const managed = script.id === "userscript_settings";
    const check = sourceState.checks[script.id];
    const card = sourceElement("article", undefined, "claudex-script-card"); card.dataset.sourceId = script.id;
    card.setAttribute("aria-label", script.name);
    const body = sourceElement("div", undefined, "claudex-card-body");
    const header = sourceElement("div", undefined, "claudex-card-heading");
    card.dataset.disabled = String(!managed && !state.enabled);
    const copy = sourceElement("div",undefined,"claudex-card-copy");
    const title = sourceElement("div",undefined,"claudex-card-meta");
    title.append(sourceElement("h3",script.name));
    if (disk?.version) title.append(sourceElement("span","v" + disk.version,"claudex-version"));
    copy.append(title,sourceElement("p",script.description,"claudex-muted"));
    if (!managed && !state.available) copy.append(sourceElement("p",state.status,"claudex-state"));
    header.append(copy);
    if (!managed) header.append(makeSwitch(`Enable ${script.name}`,state.enabled,!state.available,enabled=>setEnabled(script.id,enabled)));
    body.append(header);
    if (script.id === "sidebar_usage") {
      usageOptions = sourceElement("section",undefined,"claudex-usage-options");
      usageOptions.append(sourceElement("h4","Show in sidebar"),sourceElement("div",undefined,"claudex-userscript-list"),sourceElement("p",undefined,"claudex-usage-options-empty claudex-muted"));
      body.append(usageOptions);
    }
    card.append(body);
    if (disk) {
      const footer = sourceElement("div",undefined,"claudex-card-footer");
      const row = sourceElement("div",undefined,"claudex-update-row");
      if (disk.error) row.append(sourceElement("p",disk.error,"claudex-muted"));
      else if (check && !check.updateAvailable) row.append(sourceElement("p","Up to date","claudex-muted"));
      const actions = sourceElement("div",undefined,"claudex-source-actions");
      const checkButton = sourceButton("Check now",`Check ${script.name} for updates`,()=>performSource("check_script",{name:script.id},result=>{
        sourceState.checks[script.id]=result; sourceState.message=result.updateAvailable ? `Version ${result.availableVersion} is available.` : "You have the latest version at this source.";
      },script.id));
      checkButton.disabled ||= !disk.url; actions.append(checkButton);
      if (check?.updateAvailable) actions.append(sourceButton(`Update to v${check.availableVersion}`,`Review ${script.name} update`,()=>reviewSource(disk.url,script.id),true));
      row.append(actions);footer.append(row);feedback(footer,script.id);sourceUrlEditor(footer,script.id,disk.url,script.id);renderReview(footer,script.id);card.append(footer);
    }
    sourceManager.append(card);
  }
  renderUsageOptions();
  const last = snapshot?.lastInstall;
  if (last) {
    const errors = last.activationError || (Array.isArray(last.activation) ? last.activation.filter(x=>!x.activated).map(x=>x.error).join("; ") : "");
    if (errors) sourceManager.append(sourceElement("p",`Activation failed for ${last.name} ${last.version}: ${errors}`,"claudex-muted"));
  }
  if (activeScroller && scrollTop !== undefined) activeScroller.scrollTop = scrollTop;
  if (focused) [...sourceManager.querySelectorAll("[data-focus-key]")].find(e=>e.dataset.focusKey===focused)?.focus({preventScroll:true});
};

const updateSources = snapshot => {
  const wasConnected = sourceConnected();
  sourceState.connectedAt = Date.now();
  sourceState.snapshot = snapshot;
  if (snapshot.defaultSchedule) {
    const apply = (key,check) => {
      if (check?.result) sourceState.checks[key]=check.result; else delete sourceState.checks[key];
      const message=sourceState.feedback[key];
      if (check?.result && message?.error && message.action?.startsWith("check_") && Date.parse(check.checkedAtUtc)>=message.at) delete sourceState.feedback[key];
      if (check?.error) sourceState.checkErrors[key]=check.error; else delete sourceState.checkErrors[key];
    };
    apply("claudex",snapshot.claudexCheck);
    for (const script of snapshot.scripts) apply(script.id,script.check);
  }
  const signature = JSON.stringify(snapshot);
  if (!wasConnected || signature !== sourceSignature) {
    sourceSignature = signature;
    renderSources();
  }
  return true;
};
const receiveSourceResult = result => {
  if (sourceState.pending?.id !== result.id) return false;
  const pending = sourceState.pending;
  clearTimeout(pending.timer);
  sourceState.pending = null;
  result.error ? pending.reject(new Error(result.error)) : pending.resolve(result.value);
  return true;
};
let sourceWasConnected = sourceConnected();
const sourceTimer = setInterval(() => {
  const connected = sourceConnected();
  let expired = false;
  for (const [key,message] of Object.entries(sourceState.feedback)) if (message.expiresAt && message.expiresAt < Date.now()) {delete sourceState.feedback[key];expired=true;}
  if (sourceWasConnected !== connected || expired) { sourceWasConnected = connected; renderSources(); }
}, 2000);

let activePanel;
let hiddenNativeContent = [];
let activeButton;
let activeNav;
let nativeSelections = [];
let usageOptions;
let activeScroller;
let nativeScrollTop = 0;
let fallbackScroller;
const refreshOptions = () => renderSources();
const renderUsageOptions = () => {
  if (!usageOptions) return;
  const controller = registry.get("sidebar_usage");
  const limits = controller?.getAvailableLimits?.() || [];
  const list = usageOptions.querySelector(".claudex-userscript-list");
  const existing = new Map([...list.children].map(row => [row.dataset.limitId, row]));
  for (const limit of limits) {
    let row = existing.get(limit.id);
    if (!row) {
      row = document.createElement("div");
      row.className = "claudex-userscript-row";
      row.dataset.limitId = limit.id;
      const name = document.createElement("span");
      name.className = "claudex-userscript-name";
      const label = document.createElement("label");
      label.className = "claudex-userscript-switch";
      const input = document.createElement("input");
      input.type = "checkbox";
      input.addEventListener("change", () => registry.get("sidebar_usage")?.setLimitVisible(limit.id, input.checked));
      label.append(input, document.createElement("span"));
      row.append(name, label);
      list.appendChild(row);
    }
    existing.delete(limit.id);
    const name = row.querySelector(".claudex-userscript-name");
    if (name.textContent !== limit.name) name.textContent = limit.name;
    const input = row.querySelector("input");
    input.setAttribute("aria-label", `Show ${limit.name} usage`);
    input.checked = limit.visible;
    input.disabled = controller?.installed !== true;
  }
  for (const row of existing.values()) row.remove();
  const empty = usageOptions.querySelector(".claudex-usage-options-empty");
  empty.hidden = limits.length > 0 && controller?.installed === true;
  empty.textContent = controller?.installed === false ? "Enable Sidebar usage to change which limits are displayed." : controller ? "No usage limits available yet. They will appear after usage loads." : "Load the Sidebar usage script to choose which limits to display.";
};
window.addEventListener("claudex-usage-limits-changed", renderUsageOptions);
window.addEventListener("claudex-userscript-registered", refreshOptions);
window.addEventListener("storage", refreshOptions);
const closePanel = () => {
  sourceManager = undefined;
  usageOptions = undefined;

  activePanel?.remove();
  activePanel = undefined;
  fallbackScroller?.remove();
  fallbackScroller = undefined;
  for (const entry of hiddenNativeContent) {
    entry.element.classList.remove("claudex-userscript-native-hidden");
    entry.element.hidden = entry.hidden;
  }
  hiddenNativeContent = [];
  if (activeScroller) activeScroller.scrollTop = nativeScrollTop;
  activeScroller = undefined;
  activeButton?.removeAttribute("aria-current");
  activeButton?.classList.remove("bg-token-list-hover-background");
  activeButton = undefined;
  activeNav?.removeAttribute("data-claudex-userscript-panel-open");
  activeNav = undefined;
  for (const item of nativeSelections) {
    if (item.isConnected && !item.hasAttribute("aria-current")) item.setAttribute("aria-current", "page");
  }
  nativeSelections = [];
};
window.addEventListener("claudex-close-userscript-settings", closePanel);
// Prefer the actual scrolling descendant; Codex's outer main clips overflowing content.
const findSettingsScroller = settingsNav => {
  for (let container = settingsNav.parentElement; container; container = container.parentElement) {
    const candidates = [...container.querySelectorAll("[class*='overflow-y'], main")].filter(element =>
      !element.contains(settingsNav) && !settingsNav.contains(element) && element.getBoundingClientRect().height > 0 &&
      (element.querySelector("h1") || element.querySelector("[role='heading']")));
    const scrollable = candidates.filter(element => /^(auto|scroll)$/.test(getComputedStyle(element).overflowY));
    if (scrollable.length) return scrollable.find(element => !scrollable.some(other => other !== element && element.contains(other)));
    if (candidates.length) return candidates[0];
  }
};
const openPanel = (settingsNav, button) => {
  closePanel();
  const scroller = findSettingsScroller(settingsNav);
  if (!scroller) return;
  hiddenNativeContent = [...scroller.children].map(element => ({ element, hidden:element.hidden }));
  for (const entry of hiddenNativeContent) {
    entry.element.hidden = true;
    entry.element.classList.add("claudex-userscript-native-hidden");
  }
  activeScroller = scroller;
  nativeScrollTop = scroller.scrollTop;
  activeButton = button;
  activeNav = settingsNav;
  nativeSelections = [...settingsNav.querySelectorAll("button[aria-current='page']")];
  const nativeSelection = nativeSelections[0];
  if (nativeSelection) button.style.setProperty("--claudex-settings-selection", getComputedStyle(nativeSelection).backgroundColor);
  settingsNav.setAttribute("data-claudex-userscript-panel-open", "true");
  for (const item of nativeSelections) item.removeAttribute("aria-current");
  button.setAttribute("aria-current", "page");
  activePanel = document.createElement("section");
  activePanel.className = "claudex-userscript-panel";
  const header = document.createElement("header");
  const heading = document.createElement("h1");
  heading.textContent = "Claudex - User scripts";
  const intro = sourceElement("p", "Manage your scripts and keep them up to date.", "claudex-muted");
  const title = sourceElement("div"); title.append(heading, intro);
  const addButton = sourceElement("button", "Add script", "claudex-primary claudex-add-toggle");
  addButton.type = "button"; addButton.setAttribute("aria-expanded",String(Boolean(sourceState.addOpen)));
  addButton.onclick = () => {
    sourceState.addOpen = !sourceState.addOpen;
    addButton.setAttribute("aria-expanded",String(sourceState.addOpen));
    renderSources();
    if (sourceState.addOpen) sourceManager.querySelector("input[aria-label='New userscript URL']")?.focus();
  };
  header.append(title,addButton); activePanel.append(header);
  if (!/^(auto|scroll)$/.test(getComputedStyle(scroller).overflowY)) {
    fallbackScroller = sourceElement("div",undefined,"claudex-settings-scroll");
    scroller.append(fallbackScroller); fallbackScroller.append(activePanel); activeScroller = fallbackScroller;
  } else scroller.append(activePanel);
  activeScroller.scrollTop = 0;
  sourceManager = sourceElement("section", undefined, "claudex-source-manager");
  activePanel.append(sourceManager);
  renderSources();
  registry.get("sidebar_usage")?.refresh?.();
};

const navBindings = new Map();
let buttonTemplate;
const installInto = settingsNav => {
  if (!navBindings.has(settingsNav)) {
    const onClick = event => {
      const button = event.target.closest("button, a, [role='link']");
      if (button && !button.classList.contains("claudex-userscript-nav") &&
          settingsNav.contains(button)) closePanel();
    };
    settingsNav.addEventListener("click", onClick, true);
    settingsNav.addEventListener("input", queueScan);
    navBindings.set(settingsNav, onClick);
  }
  const nativeItems = [...settingsNav.querySelectorAll("button, a, [role='link']")]
    .filter(item => !item.classList.contains("claudex-userscript-nav"));
  const anchor = nativeItems.find(item => item.getAttribute("aria-label") === "Appearance" ||
    item.getAttribute("data-settings-panel-slug") === "appearance" ||
    item.textContent?.trim() === "Appearance") ??
    nativeItems.find(item => item.hasAttribute("data-settings-panel-slug"));
  if (anchor) buttonTemplate = anchor.cloneNode(true);
  const query = (settingsNav.querySelector("input[role='searchbox'], input[placeholder='Search']")?.value || "").trim().toLowerCase();
  const target = query ? (settingsNav.querySelector("div[class*='overflow-y-auto']") ?? anchor?.parentElement) : anchor?.parentElement;
  if (!target) return;
  let button = settingsNav.querySelector(".claudex-userscript-nav");
  if (!button) {
  settingsNav.dataset.claudexUserscriptSettings = "true";
  button = document.createElement("button");
  if (buttonTemplate) {
    button.className = buttonTemplate.className;
    button.append(...[...buttonTemplate.childNodes].map(node => node.cloneNode(true)));
  }
  button.type = "button";
  if (!buttonTemplate) button.className = "sidebar-item flex w-full items-center px-row-x py-row-y text-start hover:bg-primary-ghost-hover";
  button.removeAttribute("aria-current");
  button.removeAttribute("data-settings-panel-slug");
  button.removeAttribute("data-list-navigation-item");
  button.removeAttribute("href");
  button.classList.remove("bg-token-list-hover-background");
  for (const element of button.querySelectorAll("*")) {
    element.classList.remove("text-token-list-active-selection-foreground");
    element.classList.remove("text-token-list-active-selection-icon-foreground");
  }
  button.classList.add("claudex-userscript-nav");
  button.setAttribute("aria-label", "Claudex - User scripts");
  const label = [...button.querySelectorAll("span")].find(span => span.textContent?.trim() === "Appearance")
    ?? button.querySelector(".text-fade-truncate") ?? button.querySelector("span:last-child");
  if (label) label.textContent = "Claudex - User scripts";
  else button.textContent = "Claudex - User scripts";
  // A script sheet with code brackets, drawn for this entry in the native line style.
  const inheritedIcons = [...button.querySelectorAll("svg")];
  const inheritedIcon = inheritedIcons[0];
  const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("width", "16");
  icon.setAttribute("height", "16");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-width", "1.5");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("class", inheritedIcon?.getAttribute("class") || "shrink-0");
  icon.dataset.claudexUserscriptIcon = "true";
  icon.innerHTML = '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9l-6-6Z"/><path d="M14 3v6h6M9 12l-2 3 2 3m6-6 2 3-2 3"/>';
  // Native rows carry separate compact and leading icons; replace both variants.
  if (inheritedIcons.length) {
    for (const original of inheritedIcons) {
      const replacement = icon.cloneNode(true);
      replacement.setAttribute("width", original.getAttribute("width") || "16");
      replacement.setAttribute("height", original.getAttribute("height") || "16");
      replacement.setAttribute("class", original.getAttribute("class") || "shrink-0");
      original.replaceWith(replacement);
    }
  } else button.prepend(icon);
  button.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); openPanel(settingsNav, button); });
  }
  // The new settings rows are wrapped by a tooltip. Put our row beside the
  // native button instead of inside the tooltip trigger.
  if (!query && anchor) {
    if (button.previousElementSibling !== anchor) anchor.after(button);
  } else if (button.parentElement !== target) target.appendChild(button);
  const searchText = "claudex user scripts userscripts sidebar usage limits " + catalog.map(item => item.name).join(" ") + " " + (registry.get("sidebar_usage")?.getAvailableLimits?.() || []).map(item => item.name).join(" ");
  button.hidden = Boolean(query) && !query.split(/\s+/).every(word => searchText.toLowerCase().includes(word));
  if (activePanel?.isConnected && activeNav === settingsNav) {
    for (const item of settingsNav.querySelectorAll("button[aria-current='page']:not(.claudex-userscript-nav)")) {
      if (!nativeSelections.includes(item)) nativeSelections.push(item);
      item.removeAttribute("aria-current");
    }
    activeButton = button;
    button.setAttribute("aria-current", "page");
  }
};
const scan = root => {
  const navs = new Set(root.querySelectorAll?.("nav[aria-label='Settings']") || []);
  for (const item of root.querySelectorAll?.("[data-settings-panel-slug='appearance'], button[aria-label='Appearance']") || []) {
    const container = item.closest("nav, aside") ?? item.parentElement?.parentElement;
    if (container) navs.add(container);
  }
  // The newer settings sidebar has no named nav. Its search field and Appearance
  // entry still identify it without depending on generated layout class names.
  for (const field of root.querySelectorAll?.("input[role='searchbox'], input[placeholder='Search']") || []) {
    for (let container = field.parentElement; container && container !== document.body; container = container.parentElement) {
      if ([...container.querySelectorAll("button, a, [role='link']")].some(item =>
        item.getAttribute("aria-label") === "Appearance" || item.textContent?.trim() === "Appearance")) {
        navs.add(container.closest("nav, aside") ?? container);
        break;
      }
    }
  }
  for (const settingsNav of navs) installInto(settingsNav);
};
let scanQueued = false;
let scanActive = true;
const queueScan = () => {
  if (scanQueued || !scanActive) return;
  scanQueued = true;
  queueMicrotask(() => {
    scanQueued = false;
    if (!scanActive) return;
    if (activePanel && !activePanel.isConnected) closePanel();
    for (const [nav, onClick] of navBindings) if (!nav.isConnected) {
      nav.removeEventListener("click", onClick, true);
      nav.removeEventListener("input", queueScan);
      navBindings.delete(nav);
    }
    scan(document);
  });
};
scan(document);
const observer = new MutationObserver(queueScan);
observer.observe(document.body, { childList:true, subtree:true });
const uninstall = () => {
  clearInterval(sourceTimer);
  observer.disconnect();
  scanActive = false;
  for (const [nav, onClick] of navBindings) {
    nav.removeEventListener("click", onClick, true);
    nav.removeEventListener("input", queueScan);
  }
  navBindings.clear();
  window.removeEventListener("claudex-usage-limits-changed", renderUsageOptions);
  window.removeEventListener("claudex-userscript-registered", refreshOptions);
  window.removeEventListener("storage", refreshOptions);
  window.removeEventListener("claudex-close-userscript-settings", closePanel);
  closePanel();
  style.remove();
  document.querySelectorAll("[data-claudex-userscript-settings='true']").forEach(settingsNav => {
    settingsNav.querySelector(".claudex-userscript-nav")?.remove();
    delete settingsNav.dataset.claudexUserscriptSettings;
  });
};
window[stateKey] = { observer, uninstall, openPanel, setEnabled, updateSources, receiveSourceResult,
  takeSourceAction() { const action = sourceState.queued; sourceState.queued = null; return action; } };
return { installed:true, scripts:getCatalog().map(script => ({ ...script, ...scriptState(script) })) };
