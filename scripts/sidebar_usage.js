// ==ClaudexUserScript==
// @name          Sidebar usage
// @id            sidebar_usage
// @version       1.3.3
// @description   Shows all available Codex usage limits above the profile row.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @codex-tested  26.903.9818.0
// @codex-tested  26.908.9136.0
// @grant         none
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.sidebar-usage");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const preferencesKey = "claudex-yourself.userscripts.v1";
const cacheKey = "claudex-yourself.sidebar-usage.v2";
const visibilityKey = "claudex-yourself.sidebar-usage.visibility.v1";
window[stateKey]?.uninstall?.();

const scriptId = "sidebar_usage";
const usageLink = '<a class="claudex-usage-link" href="codex://settings/usage" title="Open usage settings">Usage</a>';
let observer;
let refreshTimer;
let widget;
let style;
let apiClient;
let mountQueued = false;
let navigationPending = false;
let navigationError = "";

const openUsageSettings = () => new Promise((resolve, reject) => {
  // Deliver to the renderer's message listener without importing private bundle exports.
  // Navigation is renderer-local; sendMessageFromView sends in the opposite direction.
  const isOpen = () => Boolean(document.querySelector("[data-settings-panel-slug='usage'][aria-current='page']"));
  if (isOpen()) { resolve(); return; }
  let timeout;
  const navigationObserver = new MutationObserver(() => {
    if (isOpen()) finish();
  });
  const finish = error => {
    navigationObserver.disconnect();
    clearTimeout(timeout);
    if (error) reject(error); else resolve();
  };
  navigationObserver.observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:["aria-current"] });
  timeout = setTimeout(() => finish(new Error("Codex did not open usage settings within 5 seconds.")), 5000);
  try {
    window.postMessage({ type:"navigate-to-route", path:"/settings/usage" }, window.location.origin);
  } catch (error) { finish(error); }
});
const renderNavigationStatus = () => {
  if (!widget) return;
  const link = widget.querySelector(".claudex-usage-link");
  link?.setAttribute("aria-busy", String(navigationPending));
  widget.querySelector(".claudex-usage-error")?.remove();
  if (navigationError) {
    const message = document.createElement("div");
    message.className = "claudex-usage-error";
    message.setAttribute("role", "alert");
    message.textContent = navigationError;
    widget.appendChild(message);
  }
};

const readCache = () => {
  try { return JSON.parse(localStorage.getItem(cacheKey) || "null"); }
  catch { return null; }
};
const readVisibility = () => {
  try { return JSON.parse(localStorage.getItem(visibilityKey) || "{}"); }
  catch { return {}; }
};
const limitId = limit => limit.id || limit.name;
const isLimitVisible = limit => readVisibility()[limitId(limit)] !== false;
const getAvailableLimits = () => (readCache()?.limits || []).map(limit => ({
  id:limitId(limit), name:limit.name, visible:isLimitVisible(limit)
}));
const notifyLimits = () => window.dispatchEvent(new CustomEvent("claudex-usage-limits-changed"));
const setLimitVisible = (id, visible) => {
  const preferences = readVisibility();
  Object.defineProperty(preferences, id, { value:Boolean(visible), enumerable:true, configurable:true, writable:true });
  localStorage.setItem(visibilityKey, JSON.stringify(preferences));
  render(readCache());
  notifyLimits();
};
const windowLabel = seconds => {
  const days = seconds / 86400;
  if (days === 7) return "Weekly";
  if (days === 1) return "Daily";
  if (Number.isInteger(days) && days > 1) return `${days}-day`;
  const hours = seconds / 3600;
  if (Number.isInteger(hours)) return `${hours}-hour`;
  return "Limit";
};
const resetLabel = timestamp => {
  if (!timestamp) return "";
  const date = new Date(timestamp * 1000);
  const now = new Date();
  const sameDay = date.toDateString() === now.toDateString();
  if (sameDay) return new Intl.DateTimeFormat(undefined, { hour:"numeric", minute:"2-digit" }).format(date);
  const resetDate = new Intl.DateTimeFormat(undefined, { month:"short", day:"numeric" }).format(date);
  const weekday = new Intl.DateTimeFormat(undefined, { weekday:"short" }).format(date);
  return `${resetDate}, ${weekday}`;
};
const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
  "&":"&amp;", "<":"&lt;", ">":"&gt;", "\"":"&quot;", "'":"&#39;"
})[character]);
const normalizeLimit = (name, limit, id = name) => {
  if (!limit) return null;
  const windows = [limit.primary_window, limit.secondary_window].filter(Boolean).map(value => ({
    label:windowLabel(value.limit_window_seconds),
    remaining:Math.max(0, Math.min(100, 100 - (value.used_percent || 0))),
    reset:resetLabel(value.reset_at),
    resetAt:value.reset_at,
    reached:Boolean(limit.limit_reached)
  }));
  return windows.length ? { id, name, windows } : null;
};
const normalizeUsage = data => {
  const limits = Object.entries(data).filter(([key]) => /(?:^|_)rate_limit$/.test(key)).map(([key, value]) =>
    normalizeLimit(key === "rate_limit" ? "General" : key.replace(/_rate_limit$/, "").replaceAll("_", " ").replace(/^./, c => c.toUpperCase()), value, key));
  for (const item of data.additional_rate_limits || []) {
    if (item.limit_name) limits.push(normalizeLimit(item.limit_name, item.rate_limit, `additional:${item.limit_name}`));
  }
  const credits = data.credits?.has_credits ? data.credits.balance : null;
  return { plan:data.plan_type || "", limits:limits.filter(Boolean), credits, updatedAt:Date.now() };
};
const getApiClient = async () => {
  if (apiClient) return apiClient;
  const source = document.querySelector("link[rel='modulepreload'][href*='/app-initial-'][href$='.js']")?.href;
  if (!source) throw new Error("Codex application module was not found.");
  const module = await import(source);
  apiClient = Object.values(module).find(value => value && typeof value === "object" && typeof value.safeGet === "function");
  if (!apiClient) throw new Error("Codex usage client was not found.");
  return apiClient;
};
const render = (data, stale = false) => {
  if (!widget) return;
  if (!data) {
    widget.innerHTML = `<div class="claudex-usage-heading">${usageLink}<span>Loading…</span></div>`;
    renderNavigationStatus();
    return;
  }
  const plan = data.plan ? data.plan.replace(/lite$/i, "").replace(/^./, value => value.toUpperCase()) : "";
  const rows = data.limits.filter(isLimitVisible).map(limit => `
    <div class="claudex-usage-group">
      <div class="claudex-usage-title"><div class="claudex-usage-name">${escapeHtml(limit.name)}</div><button type="button" class="claudex-usage-hide" data-limit-id="${escapeHtml(limitId(limit))}" aria-label="Hide ${escapeHtml(limit.name)} usage" title="Hide from sidebar (restore in User scripts settings)">&times;</button></div>
      ${limit.windows.map(value => `
        <div class="claudex-usage-window" title="${value.remaining}% remaining${value.reset ? ` &middot; resets ${value.reset}` : ""}">
          <div class="claudex-usage-meta"><span>${escapeHtml(value.label)}${value.reset ? ` &middot; ${escapeHtml(value.reset)}` : ""}</span><strong>${value.remaining}%</strong></div>
          <div class="claudex-usage-track"><span class="${value.reached ? "reached" : ""}" style="width:${value.remaining}%"></span></div>
        </div>`).join("")}
    </div>`).join("");
  widget.innerHTML = `
    <div class="claudex-usage-heading">${usageLink}<span>${escapeHtml(plan)}${stale ? " &middot; cached" : ""}</span></div>
    ${rows || '<div class="claudex-usage-empty">No usage limits selected.</div>'}
    ${data.credits != null ? `<div class="claudex-usage-credits"><span>Credits</span><strong>${escapeHtml(data.credits)}</strong></div>` : ""}`;
  renderNavigationStatus();
};
const ensureWidget = () => {
  const profile = document.querySelector("button[aria-label='Open profile menu']");
  const footer = profile?.parentElement?.parentElement?.parentElement?.parentElement;
  if (!footer?.parentElement) return;
  if (!widget) {
    widget = document.createElement("section");
    widget.className = "claudex-sidebar-usage";
    widget.setAttribute("aria-label", "Usage remaining");
    widget.addEventListener("click", async event => {
      const hide = event.target.closest(".claudex-usage-hide");
      if (hide) {
        event.preventDefault();
        event.stopPropagation();
        const buttons = [...widget.querySelectorAll(".claudex-usage-hide")];
        const index = buttons.indexOf(hide);
        const hadFocus = document.activeElement === hide;
        setLimitVisible(hide.dataset.limitId, false);
        if (hadFocus) {
          const remaining = widget.querySelectorAll(".claudex-usage-hide");
          (remaining[Math.min(index, remaining.length - 1)] || widget.querySelector(".claudex-usage-link"))?.focus();
        }
        return;
      }
      if (!event.target.closest(".claudex-usage-link")) return;
      event.preventDefault();
      if (navigationPending) return;
      navigationPending = true;
      navigationError = "";
      renderNavigationStatus();
      try {
        await openUsageSettings();
      } catch (error) {
        navigationError = "Could not open usage settings. Try again or use the profile menu.";
        console.error("Sidebar usage navigation failed", error);
      } finally {
        navigationPending = false;
        renderNavigationStatus();
      }
    });
    render(readCache(), true);
  }
  if (widget.parentElement !== footer.parentElement || widget.nextElementSibling !== footer) footer.before(widget);
};
const queueMount = () => {
  if (mountQueued) return;
  mountQueued = true;
  requestAnimationFrame(() => { mountQueued = false; ensureWidget(); });
};
const refresh = async () => {
  try {
    const client = await getApiClient();
    const data = normalizeUsage(await client.safeGet("/wham/usage"));
    localStorage.setItem(cacheKey, JSON.stringify(data));
    render(data);
    notifyLimits();
  } catch (error) {
    render(readCache(), true);
    console.warn("Sidebar usage refresh failed", error);
  }
};
const install = () => {
  observer?.disconnect();
  clearInterval(refreshTimer);
  style?.remove();
  style = document.createElement("style");
  style.dataset.claudexSidebarUsage = "true";
  style.textContent = `
    .claudex-sidebar-usage { box-sizing:border-box; width:100%; flex:none; padding:12px 14px 14px; border-top:1px solid var(--color-token-border, rgba(127,127,127,.15)); color:var(--color-token-text-secondary); }
    .claudex-usage-heading,.claudex-usage-meta,.claudex-usage-credits { display:flex; align-items:center; justify-content:space-between; gap:8px; }
    .claudex-usage-heading { margin-bottom:10px; font-size:10px; font-weight:600; letter-spacing:.05em; text-transform:uppercase; opacity:.6; }
    .claudex-usage-link { color:inherit; text-decoration:none; cursor:pointer; border-radius:2px; }
    .claudex-usage-link:hover { text-decoration:underline; }
    .claudex-usage-link:focus-visible { outline:2px solid currentColor; outline-offset:3px; }
    .claudex-usage-error { margin-top:6px; font-size:11px; line-height:1.4; color:var(--color-token-text-primary, currentColor); }
    .claudex-usage-group + .claudex-usage-group { margin-top:12px; }
    .claudex-usage-title { display:flex; align-items:center; gap:8px; margin-bottom:5px; min-height:20px; }
    .claudex-usage-name { flex:1; min-width:0; overflow:hidden; font-size:12px; font-weight:600; line-height:16px; text-overflow:ellipsis; white-space:nowrap; color:var(--color-token-text-primary, currentColor); }
    .claudex-usage-hide { display:flex; align-items:center; justify-content:center; flex:none; width:20px; height:20px; padding:0; border:0; border-radius:4px; background:transparent; color:inherit; font:16px/1 sans-serif; opacity:.5; cursor:pointer; }
    .claudex-usage-hide:hover,.claudex-usage-hide:focus-visible { opacity:1; background:var(--color-primary-ghost-hover, rgba(127,127,127,.12)); }
    .claudex-usage-hide:focus-visible { outline:2px solid currentColor; outline-offset:2px; }
    .claudex-usage-window + .claudex-usage-window { margin-top:8px; }
    .claudex-usage-meta { margin-bottom:4px; font-size:10px; line-height:13px; opacity:.72; }
    .claudex-usage-empty { font-size:11px; line-height:16px; opacity:.72; }
    .claudex-usage-meta strong,.claudex-usage-credits strong { font-weight:500; font-variant-numeric:tabular-nums; }
    .claudex-usage-track { width:100%; height:3px; overflow:hidden; border-radius:99px; background:color-mix(in srgb, var(--color-token-text-primary, currentColor) 16%, transparent); }
    .claudex-usage-track span { display:block; height:100%; border-radius:inherit; background:var(--color-token-text-primary, currentColor); transition:width .2s ease; }
    .claudex-usage-track span.reached { background:#e05252; }
    .claudex-usage-credits { margin-top:8px; padding-top:7px; border-top:1px solid var(--color-token-border, rgba(127,127,127,.15)); font-size:10px; }
  `;
  document.head.appendChild(style);
  ensureWidget();
  observer = new MutationObserver(queueMount);
  observer.observe(document.body, { childList:true, subtree:true });
  refresh();
  refreshTimer = setInterval(refresh, 60000);
  return { installed:true, cached:Boolean(readCache()) };
};
const uninstall = () => {
  observer?.disconnect();
  observer = undefined;
  clearInterval(refreshTimer);
  refreshTimer = undefined;
  widget?.remove();
  widget = undefined;
  style?.remove();
  style = undefined;
  apiClient = undefined;
  navigationError = "";
  return { installed:false };
};

const controller = { id:scriptId, install, uninstall, refresh, getAvailableLimits, setLimitVisible, get installed() { return Boolean(observer); } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail:{ id:scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(preferencesKey) || "{}"); } catch {}
return preferences[scriptId] === false ? uninstall() : install();
