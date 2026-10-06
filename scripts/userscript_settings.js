// ==ClaudexUserScript==
// @name          User script settings
// @id            userscript_settings
// @version       1.2.6
// @description   Adds reversible user-script switches to Codex Settings.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @codex-tested  26.803.10989.0
// @codex-tested  26.903.9818.0
// @codex-tested  26.908.9136.0
// @grant         none
// @codex-tested  26.924.2738.0
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
  (enabled ? controller.install : controller.uninstall)();
  const preferences = readPreferences();
  preferences[id] = controller.installed === true;
  localStorage.setItem(preferencesKey, JSON.stringify(preferences));
  renderScriptOptions();
  renderUsageOptions();
};

const style = document.createElement("style");
style.dataset.claudexUserscriptSettings = "true";
style.textContent = `
  .claudex-userscript-nav[hidden] { display:none !important; }
  [data-claudex-userscript-panel-open] button:not(.claudex-userscript-nav):not(:hover),
  .claudex-userscript-nav:not([aria-current='page']):not(:hover) { background-color:transparent !important; }
  .claudex-userscript-nav[aria-current='page'] { background-color:var(--color-primary-ghost-hover, var(--claudex-settings-selection, rgba(127,127,127,.12))) !important; }
  .claudex-userscript-native-hidden { display:none !important; }
  .claudex-userscript-panel { margin:0 auto; display:flex; width:100%; max-width:48rem; flex-direction:column; color:inherit; }
  .claudex-userscript-panel header { padding-bottom:2rem; }
  .claudex-userscript-panel h1 { margin:0; font-size:20px; font-weight:400; }
  .claudex-userscript-panel header p { margin:6px 0 0; color:var(--color-token-text-secondary); font-size:13px; }
  .claudex-usage-options { margin-top:24px; }
  .claudex-usage-options h2 { margin:0 0 6px; font-size:16px; font-weight:600; }
  .claudex-usage-options > p { margin:0 0 14px; font-size:13px; opacity:.65; }
  .claudex-userscript-switch input:focus-visible + span { outline:2px solid currentColor; outline-offset:3px; }
  .claudex-userscript-list { overflow:hidden; border:1px solid var(--color-token-border); border-radius:16px;
    background:var(--color-background-panel, var(--color-token-bg-fog)); }
  .claudex-userscript-row { display:flex; align-items:center; justify-content:space-between; gap:20px;
    min-height:52px; padding:12px 16px; border-bottom:1px solid var(--color-token-border); }
  .claudex-userscript-row:last-child { border-bottom:0; }
  .claudex-userscript-name { font-size:14px; font-weight:600; }
  .claudex-userscript-description { margin-top:3px; opacity:.65; font-size:12px; }
  .claudex-userscript-switch { position:relative; width:36px; height:20px; flex:none; }
  .claudex-userscript-switch input { position:absolute; opacity:0; pointer-events:none; }
  .claudex-userscript-switch span { position:absolute; inset:0; cursor:pointer; border-radius:20px; background:#666; transition:.15s; }
  .claudex-userscript-switch span:before { content:""; position:absolute; width:16px; height:16px; left:2px; top:2px;
    border-radius:50%; background:white; transition:.15s; }
  .claudex-userscript-switch input:disabled + span { opacity:.35; cursor:not-allowed; }
  .claudex-userscript-switch input:checked + span { background:#4f7cff; }
  .claudex-userscript-switch input:checked + span:before { transform:translateX(16px); }
`;
document.head.appendChild(style);

let activePanel;
let hiddenNativeContent = [];
let activeButton;
let activeNav;
let nativeSelections = [];
let usageOptions;
let scriptOptions;
const renderScriptOptions = () => {
  if (!scriptOptions) return;
  for (const script of getCatalog()) {
    let row = [...scriptOptions.children].find(element => element.dataset.scriptId === script.id);
    if (!row) {
      row = document.createElement("div");
      row.className = "claudex-userscript-row";
      row.dataset.scriptId = script.id;
      const copy = document.createElement("div");
      const name = document.createElement("div");
      name.className = "claudex-userscript-name";
      name.textContent = script.name;
      const description = document.createElement("div");
      description.className = "claudex-userscript-description";
      copy.append(name, description);
      const label = document.createElement("label");
      label.className = "claudex-userscript-switch";
      const input = document.createElement("input");
      input.type = "checkbox";
      input.setAttribute("aria-label", `Enable ${script.name}`);
      input.addEventListener("change", () => setEnabled(script.id, input.checked));
      label.append(input, document.createElement("span"));
      row.append(copy, label);
      scriptOptions.appendChild(row);
    }
    const state = scriptState(script);
    const description = row.querySelector(".claudex-userscript-description");
    const text = `${script.description} ${state.status}`;
    if (description.textContent !== text) description.textContent = text;
    const input = row.querySelector("input");
    input.checked = state.enabled;
    input.disabled = !state.available;
  }
};
const refreshOptions = () => { renderScriptOptions(); renderUsageOptions(); };
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
  usageOptions = undefined;
  scriptOptions = undefined;
  activePanel?.remove();
  activePanel = undefined;
  for (const entry of hiddenNativeContent) {
    entry.element.classList.remove("claudex-userscript-native-hidden");
    entry.element.hidden = entry.hidden;
  }
  hiddenNativeContent = [];
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
const openPanel = (settingsNav, button) => {
  closePanel();
  // Find the content beside this settings navigation, regardless of the active page.
  let scroller;
  for (let container = settingsNav.parentElement; container && !scroller; container = container.parentElement) {
    scroller = [...container.querySelectorAll("[class*='overflow-y-auto'], main")]
      .find(element => !element.contains(settingsNav) && !settingsNav.contains(element) &&
        (element.querySelector("h1") || element.querySelector("[role='heading']")));
  }
  if (!scroller) return;
  hiddenNativeContent = [...scroller.children].map(element => ({ element, hidden:element.hidden }));
  for (const entry of hiddenNativeContent) {
    entry.element.hidden = true;
    entry.element.classList.add("claudex-userscript-native-hidden");
  }
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
  const intro = document.createElement("p");
  intro.textContent = "Choose which reversible userscripts are active in Codex.";
  header.append(heading, intro);
  const list = document.createElement("div");
  list.className = "claudex-userscript-list";
  activePanel.append(header, list);
  scriptOptions = list;
  renderScriptOptions();
  scroller.appendChild(activePanel);
  usageOptions = document.createElement("section");
  usageOptions.className = "claudex-usage-options";
  usageOptions.innerHTML = '<h2>Usage shown in sidebar</h2><p>Choose which available limits to display. New limits are shown by default.</p><div class="claudex-userscript-list"></div><p class="claudex-usage-options-empty"></p>';
  activePanel.appendChild(usageOptions);
  renderUsageOptions();
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
window[stateKey] = { observer, uninstall, openPanel, setEnabled };
return { installed:true, scripts:getCatalog().map(script => ({ ...script, ...scriptState(script) })) };
