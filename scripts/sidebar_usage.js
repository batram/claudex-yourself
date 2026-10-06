// ==ClaudexUserScript==
// @name          Sidebar usage
// @id            sidebar_usage
// @version       1.4.8
// @description   Shows all available Codex usage limits in the sidebar or compact rail.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @codex-tested  26.803.10989.0
// @codex-tested  26.903.9818.0
// @codex-tested  26.908.9136.0
// @grant         none
// @codex-tested  26.924.2738.0
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
let retryTimer;
let refreshRequest;
let refreshPending = false;
let refreshError = "";
let retryAttempt = 0;
let widget;
let style;
let apiClient;
let mountQueued = false;
let mountGeneration = 0;
let navigationPending = false;
let navigationError = "";
let compact = false;

const openUsageSettings = () => new Promise((resolve, reject) => {
  // Deliver to the renderer's message listener without importing private bundle exports.
  // Navigation is renderer-local; sendMessageFromView sends in the opposite direction.
  window.dispatchEvent?.(new Event("claudex-close-userscript-settings"));
  const isOpen = () => Boolean(document.querySelector("[data-settings-panel-slug='usage'][aria-current='page']")) ||
    window.location.pathname?.endsWith("/settings/usage") ||
    [...(document.querySelectorAll?.("h1, h2") || [])].some(heading => heading.textContent?.trim() === "Usage & billing");
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
  widget.querySelector(".claudex-usage-navigation-error")?.remove();
  if (navigationError) {
    const message = document.createElement("div");
    message.className = "claudex-usage-error claudex-usage-navigation-error";
    message.setAttribute("role", "alert");
    message.textContent = navigationError;
    (widget.querySelector(".claudex-usage-popover") ?? widget).appendChild(message);
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
  const sources = ["app-initial-", "app-shared-"].map(name =>
    document.querySelector(`link[rel='modulepreload'][href*='/${name}'][href$='.js']`)?.href).filter(Boolean);
  if (!sources.length) throw new Error("Codex application modules were not found.");
  for (const source of sources) {
    const module = await import(source);
    apiClient = Object.values(module).find(value => value && typeof value === "object" && typeof value.safeGet === "function");
    if (apiClient) break;
  }
  if (!apiClient) throw new Error("Codex usage client was not found.");
  return apiClient;
};
const render = (data, stale = false) => {
  if (!widget) return;
  const wasOpen = widget.querySelector?.(".claudex-usage-popover:not([hidden])") != null;
  const remaining = data?.limits?.find(isLimitVisible)?.windows?.[0]?.remaining;
  const summary = Number.isFinite(remaining) ? `${remaining}% left` : "Usage limits";
  const toggle = `<button type="button" class="claudex-usage-toggle" aria-label="Show usage limits, ${escapeHtml(summary)}" aria-expanded="false" title="${escapeHtml(summary)}"><svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><path d="M4 17a8 8 0 1 1 16 0"/><path d="m12 17 4-6"/><circle cx="12" cy="17" r="1"/></svg>${Number.isFinite(remaining) ? `<span>${remaining}%</span>` : ""}</button>`;
  const errorMessage = refreshError ? '<div class="claudex-usage-error" role="status">Could not refresh usage. Retrying automatically. <button type="button" class="claudex-usage-retry">Retry now</button></div>' : "";
  if (!data) {
    widget.innerHTML = `${toggle}<div class="claudex-usage-popover" hidden><div class="claudex-usage-heading">${usageLink}<span>${refreshError ? "Unavailable" : "Loading…"}</span></div>${errorMessage}</div>`;
    if (wasOpen && compact) openPopover();
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
  widget.innerHTML = `${toggle}<div class="claudex-usage-popover" hidden>
    <div class="claudex-usage-heading">${usageLink}<span>${escapeHtml(plan)}${stale ? " &middot; cached" : ""}</span></div>
    ${rows || '<div class="claudex-usage-empty">No usage limits selected.</div>'}
    ${data.credits != null ? `<div class="claudex-usage-credits"><span>Credits</span><strong>${escapeHtml(data.credits)}</strong></div>` : ""}${errorMessage}</div>`;
  if (wasOpen && compact) openPopover();
  renderNavigationStatus();
};
const closePopover = () => {
  const panel = widget?.querySelector(".claudex-usage-popover");
  if (panel) panel.hidden = true;
  widget?.querySelector(".claudex-usage-toggle")?.setAttribute("aria-expanded", "false");
};
const openPopover = () => {
  const panel = widget?.querySelector(".claudex-usage-popover");
  const toggle = widget?.querySelector(".claudex-usage-toggle");
  if (!panel || !toggle) return;
  panel.hidden = false;
  toggle.setAttribute("aria-expanded", "true");
  const rect = toggle.getBoundingClientRect();
  panel.style.left = `${Math.max(8, Math.min(rect.right + 10, innerWidth - panel.offsetWidth - 8))}px`;
  panel.style.bottom = `${Math.max(8, Math.min(innerHeight - rect.bottom, innerHeight - panel.offsetHeight - 8))}px`;
};
const dismissPopover = event => {
  if (widget && !widget.contains(event.target)) closePopover();
};
const ensureWidget = () => {
  const profile = document.querySelector("button[aria-label='Open profile menu'], button[aria-label*='profile menu' i]");
  const footer = profile?.parentElement?.parentElement?.parentElement?.parentElement;
  if (!footer?.parentElement) return;
  compact = footer.parentElement.getBoundingClientRect().width < 90;
  if (!widget) {
    widget = document.createElement("section");
    widget.className = "claudex-sidebar-usage";
    widget.setAttribute("aria-label", "Usage remaining");
    widget.addEventListener("click", async event => {
      if (event.target.closest(".claudex-usage-retry")) {
        event.preventDefault();
        await refresh();
        return;
      }
      const toggle = event.target.closest(".claudex-usage-toggle");
      if (toggle) {
        event.preventDefault();
        if (widget.querySelector(".claudex-usage-popover")?.hidden) openPopover();
        else closePopover();
        return;
      }
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
  widget.classList.toggle("claudex-usage-compact", compact);
  const updates = document.querySelector("#claudex-codex-updates");
  const insertionPoint = updates?.parentElement === footer.parentElement ? updates : footer;
  if (widget.parentElement !== insertionPoint.parentElement || widget.nextElementSibling !== insertionPoint) {
    closePopover();
    insertionPoint.before(widget);
  }
};
const queueMount = () => {
  if (mountQueued) return;
  mountQueued = true;
  const generation = mountGeneration;
  queueMicrotask(() => {
    if (generation !== mountGeneration) return;
    mountQueued = false;
    if (observer) ensureWidget();
  });
};
const refresh = async () => {
  if (refreshPending) return;
  refreshPending = true;
  clearTimeout(retryTimer);
  const generation = mountGeneration;
  const request = new AbortController();
  refreshRequest = request;
  let timeout;
  try {
    const response = await Promise.race([
      (async () => {
        const client = await getApiClient();
        if (request.signal.aborted) throw new Error("Usage request cancelled.");
        return client.safeGet("/wham/usage", { signal:request.signal });
      })(),
      new Promise((_, reject) => {
        timeout = setTimeout(() => {
          reject(new Error("Usage request timed out after 15 seconds."));
          request.abort();
        }, 15000);
      })
    ]);
    if (generation !== mountGeneration) return;
    const data = normalizeUsage(response);
    localStorage.setItem(cacheKey, JSON.stringify(data));
    refreshError = "";
    retryAttempt = 0;
    render(data);
    notifyLimits();
  } catch (error) {
    if (generation !== mountGeneration) return;
    apiClient = undefined;
    refreshError = String(error);
    render(readCache(), true);
    console.warn("Sidebar usage refresh failed", error);
    retryTimer = setTimeout(refresh, Math.min(60000, 5000 * 2 ** Math.min(retryAttempt++, 4)));
  } finally {
    clearTimeout(timeout);
    if (generation === mountGeneration) {
      refreshPending = false;
      refreshRequest = undefined;
    }
  }
};
const install = () => {
  observer?.disconnect();
  mountGeneration++;
  refreshRequest?.abort();
  refreshPending = false;
  clearTimeout(retryTimer);
  mountQueued = false;
  document.removeEventListener("pointerdown", dismissPopover, true);
  clearInterval(refreshTimer);
  style?.remove();
  style = document.createElement("style");
  style.dataset.claudexSidebarUsage = "true";
  style.textContent = `
    .claudex-sidebar-usage { box-sizing:border-box; width:100%; flex:none; padding:12px 14px 14px; border-top:1px solid var(--color-token-border, rgba(127,127,127,.15)); color:var(--color-token-text-secondary); }
    .claudex-usage-toggle { display:none; align-items:center; flex-direction:column; justify-content:center; gap:1px; width:36px; height:38px; padding:0; border:0; border-radius:8px; background:transparent; color:var(--color-token-text-secondary, currentColor); cursor:pointer; }
    .claudex-usage-toggle span { font-size:9px; font-weight:600; line-height:10px; font-variant-numeric:tabular-nums; }
    .claudex-usage-toggle:hover,.claudex-usage-toggle[aria-expanded='true'] { background:var(--color-primary-ghost-hover, rgba(127,127,127,.12)); }
    .claudex-usage-toggle:focus-visible { outline:2px solid currentColor; outline-offset:2px; }
    .claudex-usage-popover[hidden] { display:none!important; }
    .claudex-sidebar-usage:not(.claudex-usage-compact) .claudex-usage-popover { display:block!important; }
    .claudex-usage-compact { width:auto; padding:0; border:0; display:flex; justify-content:center; }
    .claudex-usage-compact .claudex-usage-toggle { display:flex; }
    .claudex-usage-compact .claudex-usage-popover { position:fixed; z-index:2147483000; box-sizing:border-box; width:min(280px,calc(100vw - 24px)); max-height:calc(100vh - 24px); overflow:auto; padding:16px; border:1px solid var(--color-token-border, rgba(127,127,127,.2)); border-radius:14px; background:var(--color-background-panel, Canvas); color:var(--color-token-text-secondary, CanvasText); box-shadow:0 8px 28px #0003; }
    .claudex-usage-compact .claudex-usage-heading { font-size:12px; letter-spacing:0; text-transform:none; opacity:1; }
    .claudex-usage-heading,.claudex-usage-meta,.claudex-usage-credits { display:flex; align-items:center; justify-content:space-between; gap:8px; }
    .claudex-usage-heading { margin-bottom:10px; font-size:10px; font-weight:600; letter-spacing:.05em; text-transform:uppercase; opacity:.6; }
    .claudex-usage-link { color:inherit; text-decoration:none; cursor:pointer; border-radius:2px; }
    .claudex-usage-link:hover { text-decoration:underline; }
    .claudex-usage-link:focus-visible { outline:2px solid currentColor; outline-offset:3px; }
    .claudex-usage-error { margin-top:6px; font-size:11px; line-height:1.4; color:var(--color-token-text-primary, currentColor); }
    .claudex-usage-group + .claudex-usage-group { margin-top:12px; }
    .claudex-usage-title { display:flex; align-items:center; gap:8px; margin-bottom:5px; min-height:20px; }
    .claudex-usage-name { flex:1; min-width:0; overflow:hidden; font-size:12px; font-weight:600; line-height:16px; text-overflow:ellipsis; white-space:nowrap; color:var(--color-token-text-primary, currentColor); }
    .claudex-usage-hide { display:flex; align-items:center; justify-content:center; flex:none; width:20px; height:20px; padding:0; border:0; border-radius:4px; background:transparent; color:inherit; font:16px/1 sans-serif; opacity:0; pointer-events:none; cursor:pointer; }
    .claudex-sidebar-usage:hover .claudex-usage-hide,.claudex-usage-compact .claudex-usage-hide { opacity:.5; pointer-events:auto; }
    .claudex-usage-hide:focus-visible { pointer-events:auto; }
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
  document.querySelectorAll(".claudex-sidebar-usage").forEach(element => element.remove());
  widget = undefined;
  ensureWidget();
  observer = new MutationObserver(queueMount);
  observer.observe(document.body, { childList:true, subtree:true });
  document.addEventListener("pointerdown", dismissPopover, true);
  refresh();
  refreshTimer = setInterval(refresh, 60000);
  return { installed:true, cached:Boolean(readCache()) };
};
const uninstall = () => {
  observer?.disconnect();
  mountGeneration++;
  refreshRequest?.abort();
  refreshRequest = undefined;
  refreshPending = false;
  clearTimeout(retryTimer);
  retryTimer = undefined;
  mountQueued = false;
  document.removeEventListener("pointerdown", dismissPopover, true);
  observer = undefined;
  clearInterval(refreshTimer);
  refreshTimer = undefined;
  widget?.remove();
  document.querySelectorAll(".claudex-sidebar-usage").forEach(element => element.remove());
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
