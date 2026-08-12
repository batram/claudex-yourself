// ==ClaudexUserScript==
// @name          User script settings
// @id            userscript_settings
// @version       1.0.0
// @description   Adds reversible user-script switches to Codex Settings.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @grant         none
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.userscript-settings");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const preferencesKey = "claudex-yourself.userscripts.v1";
window[stateKey]?.uninstall?.();

const catalog = [
  { id: "sidebar_usage", name: "Sidebar usage", description: "Shows all available usage limits above the profile row." },
  { id: "hide_invite_a_friend", name: "Hide Invite a friend", description: "Hides the Invite a friend account-menu entry." },
  { id: "hide_pets_button", name: "Hide pets button", description: "Hides the Show pet / Hide pet account-menu entry." }
];
const registry = window[registryKey] ??= new Map();
const readPreferences = () => {
  try { return JSON.parse(localStorage.getItem(preferencesKey) || "{}"); }
  catch { return {}; }
};
const isEnabled = id => readPreferences()[id] !== false;
const setEnabled = (id, enabled) => {
  const preferences = readPreferences();
  preferences[id] = enabled;
  localStorage.setItem(preferencesKey, JSON.stringify(preferences));
  const controller = registry.get(id);
  if (controller) (enabled ? controller.install : controller.uninstall)();
};

const style = document.createElement("style");
style.dataset.claudexUserscriptSettings = "true";
style.textContent = `
  .claudex-userscript-panel { margin:0 auto; display:flex; width:100%; max-width:48rem; flex-direction:column; color:inherit; }
  .claudex-userscript-panel header { padding-bottom:2rem; }
  .claudex-userscript-panel h1 { margin:0; font-size:20px; font-weight:400; }
  .claudex-userscript-panel header p { margin:6px 0 0; color:var(--color-token-text-secondary); font-size:13px; }
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
  .claudex-userscript-switch input:checked + span { background:#4f7cff; }
  .claudex-userscript-switch input:checked + span:before { transform:translateX(16px); }
`;
document.head.appendChild(style);

let activePanel;
let hiddenNativeContent;
let activeButton;
const closePanel = () => {
  activePanel?.remove();
  activePanel = undefined;
  if (hiddenNativeContent) hiddenNativeContent.hidden = false;
  hiddenNativeContent = undefined;
  activeButton?.removeAttribute("aria-current");
  activeButton?.classList.remove("bg-token-list-hover-background");
  activeButton = undefined;
};
const openPanel = (settingsNav, button) => {
  closePanel();
  const nativeHeading = [...document.querySelectorAll("h1")].find(element => element.textContent?.trim() === "General");
  const scroller = nativeHeading?.closest("[class*='scrollbar-stable'][class*='overflow-y-auto']");
  const nativeContent = scroller?.firstElementChild;
  if (!scroller || !nativeContent) return;
  hiddenNativeContent = nativeContent;
  nativeContent.hidden = true;
  activeButton = button;
  for (const item of settingsNav.querySelectorAll("button[aria-current='page']")) item.removeAttribute("aria-current");
  button.setAttribute("aria-current", "page");
  button.classList.add("bg-token-list-hover-background");
  activePanel = document.createElement("section");
  activePanel.className = "claudex-userscript-panel";
  const header = document.createElement("header");
  const heading = document.createElement("h1");
  heading.textContent = "User scripts";
  const intro = document.createElement("p");
  intro.textContent = "Choose which reversible userscripts are active in Codex.";
  header.append(heading, intro);
  const list = document.createElement("div");
  list.className = "claudex-userscript-list";
  activePanel.append(header, list);
  for (const script of catalog) {
    const row = document.createElement("div");
    row.className = "claudex-userscript-row";
    const copy = document.createElement("div");
    const name = document.createElement("div");
    name.className = "claudex-userscript-name";
    name.textContent = script.name;
    const description = document.createElement("div");
    description.className = "claudex-userscript-description";
    description.textContent = script.description;
    copy.append(name, description);
    const label = document.createElement("label");
    label.className = "claudex-userscript-switch";
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = isEnabled(script.id);
    input.setAttribute("aria-label", `Enable ${script.name}`);
    input.addEventListener("change", () => setEnabled(script.id, input.checked));
    label.append(input, document.createElement("span"));
    row.append(copy, label);
    list.appendChild(row);
  }
  scroller.appendChild(activePanel);
};

const installInto = settingsNav => {
  if (settingsNav.dataset.claudexUserscriptSettings === "true") return;
  const anchor = settingsNav.querySelector("button[aria-label='Appearance']")
    ?? settingsNav.querySelector("button[data-settings-panel-slug='general-settings']");
  if (!anchor?.parentElement) return;
  settingsNav.dataset.claudexUserscriptSettings = "true";
  const button = anchor.cloneNode(true);
  button.removeAttribute("aria-current");
  button.removeAttribute("data-settings-panel-slug");
  button.classList.remove("bg-token-list-hover-background");
  for (const element of button.querySelectorAll("*")) {
    element.classList.remove("text-token-list-active-selection-foreground");
    element.classList.remove("text-token-list-active-selection-icon-foreground");
  }
  button.classList.add("claudex-userscript-nav");
  button.setAttribute("aria-label", "User scripts settings");
  const label = button.querySelector(".text-fade-truncate") ?? button.querySelector("span:last-child");
  if (label) label.textContent = "User scripts";
  button.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); openPanel(settingsNav, button); });
  anchor.parentElement.appendChild(button);
  for (const other of settingsNav.querySelectorAll("button")) if (other !== button) other.addEventListener("click", closePanel);
};
const scan = root => {
  const navs = root instanceof Element && root.matches("nav[aria-label='Settings']")
    ? [root]
    : [...(root.querySelectorAll?.("nav[aria-label='Settings']") || [])];
  for (const settingsNav of navs) installInto(settingsNav);
};
scan(document);
const observer = new MutationObserver(records => {
  for (const record of records) for (const node of record.addedNodes) if (node instanceof Element) scan(node);
});
observer.observe(document.body, { childList:true, subtree:true });
const uninstall = () => {
  observer.disconnect();
  closePanel();
  style.remove();
  document.querySelectorAll("nav[data-claudex-userscript-settings='true']").forEach(settingsNav => {
    settingsNav.querySelector("[aria-label='User scripts settings']")?.remove();
    delete settingsNav.dataset.claudexUserscriptSettings;
  });
};
window[stateKey] = { observer, uninstall, openPanel, setEnabled };
return { installed:true, scripts:catalog.map(script => ({ ...script, enabled:isEnabled(script.id) })) };
