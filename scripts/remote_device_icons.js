// ==ClaudexUserScript==
// @name          Remote device icons
// @id            remote_device_icons
// @version       1.0.5
// @description   Choose preset or uploaded icons for each remote device in native and grouped chat lists.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @codex-tested  26.1007.2314.0
// @codex-tested  20156
// @grant         none
// @codex-tested  20770
// @codex-tested  26.1016.6573.0
// @codex-tested  26.1021.3705.0
// @codex-tested  26.1023.6123.0
// ==/ClaudexUserScript==
const scriptId = "remote_device_icons";
const stateKey = Symbol.for("claudex-yourself.remote-device-icons");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const storageKey = "claudex-yourself.remote-device-icons.v1";
window[stateKey]?.uninstall?.();
const svg = body => `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">${body}</svg>`)}`;
const presets = [
  { id: "apple", name: "Apple", src: svg('<path fill="#8293a3" d="M20 8c2-1 4-4 3-7-3 0-6 3-6 6v1h3Zm7 15c-1 3-3 8-6 8-2 0-3-1-5-1s-3 1-5 1c-4 0-8-7-8-13 0-5 3-9 7-9 3 0 4 2 6 2s4-2 7-2c2 0 5 1 6 3-6 3-5 9-2 11Z"/>') },
  { id: "linux", name: "Linux penguin", src: svg('<ellipse cx="16" cy="17" rx="10" ry="13" fill="#263241"/><ellipse cx="16" cy="21" rx="7" ry="9" fill="#f5f5ed"/><ellipse cx="12" cy="11" rx="3" ry="4" fill="white"/><ellipse cx="20" cy="11" rx="3" ry="4" fill="white"/><circle cx="13" cy="12" r="1.3"/><circle cx="19" cy="12" r="1.3"/><path d="m11 16 5-3 5 3-5 3Z" fill="#ffbd35"/><path d="M2 29q5-7 12 0v2H2m16-2q7-7 12 0v2H18" fill="#ffbd35"/>') },
  { id: "windows", name: "Windows", src: svg('<path fill="#139ddd" d="m2 6 12-2v11H2Zm14-2 14-2v13H16ZM2 17h12v11L2 26Zm14 0h14v13l-14-2Z"/>') },
  { id: "ufo", name: "Tiny UFO", src: svg('<path d="m10 21-5 10h22l-5-10" fill="#a9df8a" opacity=".65"/><path d="M8 16a8 8 0 0 1 16 0" fill="#8be0e5" stroke="#395a71" stroke-width="1.5"/><ellipse cx="16" cy="18" rx="14" ry="5" fill="#8d82dc" stroke="#554889" stroke-width="1.5"/><ellipse cx="16" cy="9" rx="4" ry="5" fill="#91cf67"/><path d="m13 8 2 2-2 1m6-3-2 2 2 1" fill="#253b30"/><circle cx="7" cy="18" r="1.5" fill="#ffe58c"/><circle cx="16" cy="20" r="1.5" fill="#ffe58c"/><circle cx="25" cy="18" r="1.5" fill="#ffe58c"/>') },
  { id: "duck", name: "Debug duck", src: svg('<path d="M4 18q-4 12 12 12 13 0 13-11l-10-1-6-4Z" fill="#ffd34d" stroke="#c48e24"/><circle cx="12" cy="11" r="8" fill="#ffd34d" stroke="#c48e24"/><path d="m5 11-5 3 6 3" fill="#fa9143"/><circle cx="10" cy="9" r="1.5" fill="#263241"/><path d="M16 21q7-4 7 2-3 5-8 2" fill="#edb73d"/><path d="m20 4 3-3m1 7 5-1" stroke="#83b5c9" stroke-width="2" stroke-linecap="round"/>') }
];
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(storageKey) || "{}"); } catch {}
if (!preferences || typeof preferences !== "object" || Array.isArray(preferences)) preferences = {};
const devices = new Map();
const errors = new Map();
const mounts = new Set();
let observer, timer, style;
const rows = "[data-app-action-sidebar-thread-host-id],[data-claudex-host-id]";
const isRemote = id => typeof id === "string" && id !== "local" && id !== "durable";
const propsAbove = element => {
  let fiber = element?.[Object.keys(element).find(key => key.startsWith("__reactFiber$"))];
  for (let depth = 0; fiber && depth < 40; depth++, fiber = fiber.return) {
    const props = fiber.memoizedProps;
    if (props?.threadSummary || props?.displayCwd) return props;
  }
  return {};
};
const inferOs = cwd => /^\/Users\//.test(cwd || "") ? "apple" : /^\/(home|root)(\/|$)/.test(cwd || "") ? "linux" : /^[a-z]:[\\/]/i.test(cwd || "") ? "windows" : null;
const getIcon = device => {
  const saved = preferences[device.id] || {};
  const choice = saved.choice || "auto";
  if (choice === "native") return null;
  if (choice === "custom") return saved.customData || null;
  return presets.find(preset => preset.id === (choice === "auto" ? device.os : choice))?.src || null;
};
const getDevices = () => [...devices.values()].map(device => ({ ...device,
  choice: preferences[device.id]?.choice || "auto", icon: getIcon(device), error: errors.get(device.id) || "" }));
const restore = root => {
  for (const node of root.querySelectorAll("[data-claudex-device-layout]")) node.removeAttribute("data-claudex-device-layout");
  for (const node of root.querySelectorAll("[data-claudex-device-part]")) node.removeAttribute("data-claudex-device-part");
  for (const slot of root.querySelectorAll("[data-claudex-device-slot]")) slot.remove();
  for (const image of root.querySelectorAll(".claudex-device-icon-image")) image.remove();
  for (const icon of root.querySelectorAll("svg[data-claudex-device-icon]")) {
    icon.removeAttribute("data-claudex-device-icon");
    icon.style.removeProperty("--claudex-device-image");
  }
};
const repaint = () => {
  if (!observer) return;
  observer.disconnect();
  for (const row of document.querySelectorAll(rows)) {
    const id = row.dataset.appActionSidebarThreadHostId || row.dataset.claudexHostId;
    if (!isRemote(id)) { restore(row); continue; }
    const props = propsAbove(row);
    const cwd = props.threadSummary?.cwd || props.displayCwd || row.title?.split("\n").at(-1);
    const previous = devices.get(id);
    const os = inferOs(cwd) || previous?.os;
    const config = props.hoverCardHostConfig;
    const name = config?.display_name || config?.displayName ||
      ({ apple: "Mac", linux: "Linux", windows: "Windows" }[os]) || previous?.name || `Remote ${decodeURIComponent(id.split(":").at(-1)).slice(0,24)}`;
    const device = { id, name, os };
    devices.set(id, device);
    let source = row.querySelector("svg.icon-2xs.no-drag") || row.querySelector("[aria-label='Remote session'] svg");
    // Use the same explicit title/device/status columns as grouped rows.
    // Leave React's children in place; mark their roles and own only our slot.
    if (row.matches("[data-app-action-sidebar-thread-row]")) {
      const rail = row.firstElementChild;
      if (rail?.querySelector("[data-thread-title]")) {
        let slot = rail.querySelector(":scope > [data-claudex-device-slot]");
        if (!slot) {
          slot = document.createElement("span");
          slot.dataset.claudexDeviceSlot = "true";
          slot.setAttribute("aria-label", "Remote session");
          slot.innerHTML = '<svg aria-hidden="true" class="icon-2xs" width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor"><circle cx="8" cy="8" r="6"/><ellipse cx="8" cy="8" rx="2.5" ry="6"/><path d="M2 8h12"/></svg>';
          rail.append(slot);
        }
        rail.dataset.claudexDeviceLayout = "true";
        for (const child of rail.children) {
          if (child === slot) continue;
          child.dataset.claudexDevicePart = child.querySelector("[data-thread-title]") ? "title" :
            child.matches("[data-hover-card-open-immediately]") ? "status" : "spacer";
        }
        // Hide only the original environment glyph, never a status spinner.
        for (const glyph of rail.querySelectorAll("svg.icon-2xs.no-drag,[data-testid=task-row-environment-remote]")) {
          if (!slot.contains(glyph)) glyph.dataset.claudexDevicePart = "native";
        }
        source = slot.querySelector("svg");
      }
    }
    if (!source) continue;
    const src = getIcon(device);
    if (!src) {
      source.removeAttribute("data-claudex-device-icon");
      source.style.removeProperty("--claudex-device-image");
      continue;
    }
    // Paint within the native SVG's own box, independent of its parent's size.
    // React retains its SVG and children; uninstall restores the original globe.
    source.dataset.claudexDeviceIcon = id;
    source.style.setProperty("--claudex-device-image", `url(${JSON.stringify(src)})`);
  }
  for (const [id, saved] of Object.entries(preferences)) {
    if (!devices.has(id) && isRemote(id)) devices.set(id, { id, name: saved.name || id, os: saved.os });
  }
  renderMounted();
  observer.observe(document.body, { childList: true, subtree: true, attributes: true,
    attributeFilter: ["data-app-action-sidebar-thread-host-id", "data-claudex-host-id"] });
};
const schedule = records => {
  if (records?.every(record => record.target instanceof Element && record.target.closest("[data-claudex-device-options]"))) return;
  if (!timer) timer = setTimeout(() => { timer = undefined; repaint(); }, 80);
};
const saveChoice = (id, choice) => {
  if (!["auto", "native", "custom", ...presets.map(preset => preset.id)].includes(choice)) throw new Error("Unknown icon choice.");
  const device = devices.get(id);
  preferences[id] = { ...preferences[id], choice, name: device?.name || id, os: device?.os };
  localStorage.setItem(storageKey, JSON.stringify(preferences));
  errors.delete(id); repaint(); renderMounted();
};
const upload = async (id, file) => {
  if (!file || file.size > 2 * 1024 * 1024) throw new Error("Choose an image smaller than 2 MB.");
  if (!/^image\/(png|jpeg|webp|gif|svg\+xml)$/.test(file.type)) throw new Error("Use a PNG, JPEG, WebP, GIF, or SVG image.");
  const url = URL.createObjectURL(file);
  try {
    const image = new Image(); image.src = url; await image.decode();
    if (!image.naturalWidth || !image.naturalHeight) throw new Error("The image could not be read.");
    const canvas = document.createElement("canvas"); canvas.width = 128; canvas.height = 128;
    const ratio = Math.min(128 / image.naturalWidth, 128 / image.naturalHeight);
    const width = image.naturalWidth * ratio, height = image.naturalHeight * ratio;
    canvas.getContext("2d").drawImage(image, (128 - width) / 2, (128 - height) / 2, width, height);
    preferences[id] = { ...preferences[id], customData: canvas.toDataURL("image/png") };
    saveChoice(id, "custom");
  } finally { URL.revokeObjectURL(url); }
};
const optionsCss = `
    [data-claudex-device-options]{margin-top:8px;padding-top:8px;border-top:1px solid var(--color-border,var(--color-token-border,#8883));font-size:12px;container-type:inline-size}
    [data-claudex-device-options] h4{font-size:12px;font-weight:500;margin:0 0 2px}
    .claudex-device-options-list{margin-top:6px}
    .claudex-device-option{display:grid;grid-template-columns:20px minmax(80px,1fr) 144px auto;align-items:center;gap:6px 10px;padding:7px 0;font-size:12px}
    .claudex-device-preview{width:20px;height:20px;object-fit:contain;grid-column:1;grid-row:1}
    .claudex-device-copy{min-width:0;display:flex;flex-direction:column;grid-column:2;grid-row:1}
    .claudex-device-name{font-size:13px;font-weight:500;line-height:18px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .claudex-device-copy small{color:var(--color-text-secondary,var(--color-token-text-secondary,#888));font-size:11px;line-height:16px}
    .claudex-device-option select{box-sizing:border-box;width:144px;max-width:100%;min-width:0;height:28px;font-family:inherit;font-size:12px;line-height:18px;color:inherit;background-color:var(--color-background-panel,Canvas);border:1px solid var(--color-border,var(--color-token-border,#8884));border-radius:7px;padding:4px 24px 4px 8px;cursor:pointer;color-scheme:inherit}
    .claudex-device-upload{box-sizing:border-box;position:relative;display:inline-flex;align-items:center;justify-content:center;height:28px;white-space:nowrap;overflow:hidden;border:1px solid var(--color-border,var(--color-token-border,#8884));border-radius:7px;padding:4px 8px;font-size:12px;line-height:18px;cursor:pointer}
    .claudex-device-upload:hover{background:var(--color-primary-ghost-hover,#8882)}
    .claudex-device-upload:focus-within{outline:2px solid var(--color-text-info,#4f7cff);outline-offset:2px}
    .claudex-device-upload input{position:absolute;inset:0;opacity:0;width:100%;cursor:pointer}
    .claudex-device-error{grid-column:2 / -1;margin:0;color:var(--color-text-danger,#c44)}
    .claudex-device-error:empty{display:none}
    [data-claudex-device-options][data-empty=true]:after{content:'Remote devices appear here after their chats load.';opacity:.6;font-size:12px}
    @container (max-width:440px){.claudex-device-option{grid-template-columns:20px minmax(0,1fr) auto;row-gap:8px;padding:10px 0}.claudex-device-copy{grid-column:2 / -1}.claudex-device-option select{grid-column:2;grid-row:2;width:100%}.claudex-device-upload{grid-column:3;grid-row:2}}
    @container (max-width:260px){.claudex-device-option{grid-template-columns:20px minmax(0,1fr)}.claudex-device-option select{grid-column:2}.claudex-device-upload{grid-column:2;grid-row:3;justify-self:start}}`;
const renderSettings = container => {
  let optionsStyle = document.querySelector("style[data-claudex-device-options-style]");
  if (!optionsStyle) {
    optionsStyle = document.createElement("style"); optionsStyle.dataset.claudexDeviceOptionsStyle = "true";
    document.head.append(optionsStyle);
  }
  if (optionsStyle.textContent !== optionsCss) optionsStyle.textContent = optionsCss;
  container.dataset.claudexDeviceOptions = "true";
  if (!container.querySelector(".claudex-device-options-list")) {
    const heading = document.createElement("h4"); heading.textContent = "Icons by device";
    const note = document.createElement("p"); note.className = "claudex-muted";
    note.textContent = "Use the device's default icon, choose a preset, or upload an image (up to 2 MB).";
    const list = document.createElement("div"); list.className = "claudex-device-options-list";
    container.append(heading, note, list);
  }
  mounts.add(container); renderMounted();
};
function renderMounted() {
  for (const container of mounts) {
    if (!container.isConnected) { mounts.delete(container); continue; }
    const list = container.querySelector(".claudex-device-options-list");
    for (const device of getDevices()) {
      let row = [...list.children].find(element => element.dataset.deviceId === device.id);
      if (!row) {
        row = document.createElement("div"); row.className = "claudex-device-option"; row.dataset.deviceId = device.id;
        const preview = document.createElement("img"); preview.className = "claudex-device-preview"; preview.alt = "";
        const copy = document.createElement("div"); copy.className = "claudex-device-copy";
        const name = document.createElement("strong"); name.className = "claudex-device-name";
        const detail = document.createElement("small");
        copy.append(name, detail);
        const select = document.createElement("select"); select.setAttribute("aria-label", `Icon for ${device.name}`);
        select.dataset.focusKey = `device-icon-${device.id}`;
        for (const [id, name] of [["auto", "Automatic"], ["native", "Default globe"], ...presets.map(p => [p.id,p.name]), ["custom", "Uploaded image"]]) {
          const option = document.createElement("option"); option.value = id; option.textContent = name; select.append(option);
        }
        select.addEventListener("change", () => saveChoice(device.id, select.value));
        const label = document.createElement("label"); label.className = "claudex-device-upload"; label.textContent = "Upload icon";
        const input = document.createElement("input"); input.type = "file"; input.accept = "image/png,image/jpeg,image/webp,image/gif,image/svg+xml";
        input.setAttribute("aria-label", `Upload icon for ${device.name}`);
        input.addEventListener("change", async () => {
          try { await upload(device.id, input.files[0]); }
          catch (error) { errors.set(device.id, error.message); renderMounted(); }
          finally { input.value = ""; }
        });
        label.append(input);
        const error = document.createElement("p"); error.className = "claudex-device-error"; error.setAttribute("role", "status");
        row.append(preview, copy, select, label, error); list.append(row);
      }
      row.querySelector(".claudex-device-name").textContent = device.name;
      row.querySelector(".claudex-device-copy").title = device.id;
      row.querySelector(".claudex-device-copy small").textContent = ({apple:"macOS",linux:"Linux",windows:"Windows"}[device.os]) || "Remote device";
      const preview = row.querySelector("img");
      if (device.icon) { if (preview.getAttribute("src") !== device.icon) preview.src = device.icon; preview.hidden = false; }
      else { preview.removeAttribute("src"); preview.hidden = true; }
      const select = row.querySelector("select"); if (select.value !== device.choice) select.value = device.choice;
      select.querySelector("option[value=custom]").disabled = !preferences[device.id]?.customData;
      row.querySelector(".claudex-device-error").textContent = device.error;
    }
    container.dataset.empty = String(devices.size === 0);
  }
}
const uninstall = () => {
  observer?.disconnect(); observer = undefined; clearTimeout(timer); timer = undefined;
  restore(document); style?.remove(); style = undefined;
  window.removeEventListener("storage", receivePreferences);
  return { installed: false };
};
const install = () => {
  uninstall();
  style = document.createElement("style");
  style.textContent = `svg[data-claudex-device-icon]{background:var(--claudex-device-image) center/contain no-repeat}
    svg[data-claudex-device-icon]>*{visibility:hidden}
    [data-claudex-device-layout]{display:grid!important;grid-template-columns:minmax(0,1fr) 20px 60px;align-items:center;column-gap:0}
    [data-claudex-device-part=title]{grid-column:1;grid-row:1;min-width:0}
    [data-claudex-device-part=spacer],[data-claudex-device-part=native]{display:none!important}
    [data-claudex-device-part=status]{grid-column:3;grid-row:1;position:static!important;min-width:0!important;width:60px;height:20px!important;padding:0 6px!important;gap:4px!important}
    [data-claudex-device-slot]{display:flex;grid-column:2;grid-row:1;align-items:center;justify-content:center;width:20px;height:20px;pointer-events:none}
    [data-claudex-device-slot]>svg{width:14px;height:14px}`;
  document.head.append(style);
  observer = new MutationObserver(schedule); repaint();
  window.addEventListener("storage", receivePreferences);
  return { installed: true, devices: getDevices().map(({id,name,os}) => ({id,name,os})) };
};
const receivePreferences = event => {
  if (event.key !== storageKey) return;
  try { const value = JSON.parse(event.newValue || "{}"); if (value && typeof value === "object" && !Array.isArray(value)) preferences = value; } catch {}
  repaint(); renderMounted();
};
const controller = { id: scriptId, name: "Remote device icons", description: "Choose an icon for each remote device.", install, uninstall, renderSettings, getDevices,
  setChoice: saveChoice, upload, presets, inferOs, get installed() { return Boolean(observer); } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail: { id: scriptId } }));
let enabled = {};
try { enabled = JSON.parse(localStorage.getItem("claudex-yourself.userscripts.v1") || "{}"); } catch {}
return enabled[scriptId] === false ? uninstall() : install();
