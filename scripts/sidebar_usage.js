// ==ClaudexUserScript==
// @name          Sidebar usage
// @id            sidebar_usage
// @version       1.2.0
// @description   Shows all available Codex usage limits above the profile row.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @grant         none
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.sidebar-usage");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const preferencesKey = "claudex-yourself.userscripts.v1";
const cacheKey = "claudex-yourself.sidebar-usage.v2";
window[stateKey]?.uninstall?.();

const scriptId = "sidebar_usage";
let observer;
let refreshTimer;
let widget;
let style;
let apiClient;
let mountQueued = false;

const readCache = () => {
  try { return JSON.parse(localStorage.getItem(cacheKey) || "null"); }
  catch { return null; }
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
const shortName = name => name === "GPT-5.3-Codex-Spark" ? "Codex Spark" : name;
const isSparkLimit = name => name === "GPT-5.3-Codex-Spark" || name === "Codex Spark";
const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
  "&":"&amp;", "<":"&lt;", ">":"&gt;", "\"":"&quot;", "'":"&#39;"
})[character]);
const normalizeLimit = (name, limit) => {
  if (!limit) return null;
  const windows = [limit.primary_window, limit.secondary_window].filter(Boolean).map(value => ({
    label:windowLabel(value.limit_window_seconds),
    remaining:Math.max(0, Math.min(100, 100 - (value.used_percent || 0))),
    reset:resetLabel(value.reset_at),
    resetAt:value.reset_at,
    reached:Boolean(limit.limit_reached)
  }));
  return windows.length ? { name:shortName(name), windows } : null;
};
const normalizeUsage = data => {
  const limits = [normalizeLimit("General", data.rate_limit)];
  if (data.code_review_rate_limit) limits.push(normalizeLimit("Code review", data.code_review_rate_limit));
  for (const item of data.additional_rate_limits || []) {
    if (!isSparkLimit(item.limit_name)) limits.push(normalizeLimit(item.limit_name, item.rate_limit));
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
  if (!data?.limits?.length) {
    widget.innerHTML = `<div class="claudex-usage-heading"><span>Usage</span><span>Loading…</span></div>`;
    return;
  }
  const plan = data.plan ? data.plan.replace(/lite$/i, "").replace(/^./, value => value.toUpperCase()) : "";
  const rows = data.limits.filter(limit => !isSparkLimit(limit.name)).map(limit => `
    <div class="claudex-usage-group">
      <div class="claudex-usage-name">${escapeHtml(limit.name)}</div>
      ${limit.windows.map(value => `
        <div class="claudex-usage-window" title="${value.remaining}% remaining${value.reset ? ` &middot; resets ${value.reset}` : ""}">
          <div class="claudex-usage-meta"><span>${escapeHtml(value.label)}${value.reset ? ` &middot; ${escapeHtml(value.reset)}` : ""}</span><strong>${value.remaining}%</strong></div>
          <div class="claudex-usage-track"><span class="${value.reached ? "reached" : ""}" style="width:${value.remaining}%"></span></div>
        </div>`).join("")}
    </div>`).join("");
  widget.innerHTML = `
    <div class="claudex-usage-heading"><span>Usage</span><span>${escapeHtml(plan)}${stale ? " &middot; cached" : ""}</span></div>
    ${rows}
    ${data.credits != null ? `<div class="claudex-usage-credits"><span>Credits</span><strong>${escapeHtml(data.credits)}</strong></div>` : ""}`;
};
const ensureWidget = () => {
  const profile = document.querySelector("button[aria-label='Open profile menu']");
  const footer = profile?.parentElement?.parentElement?.parentElement?.parentElement;
  if (!footer?.parentElement) return;
  if (!widget) {
    widget = document.createElement("section");
    widget.className = "claudex-sidebar-usage";
    widget.setAttribute("aria-label", "Usage remaining");
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
    .claudex-sidebar-usage { box-sizing:border-box; width:100%; flex:none; padding:8px 12px 9px; border-top:1px solid var(--color-token-border, rgba(127,127,127,.15)); color:var(--color-token-text-secondary); }
    .claudex-usage-heading,.claudex-usage-meta,.claudex-usage-credits { display:flex; align-items:center; justify-content:space-between; gap:8px; }
    .claudex-usage-heading { margin-bottom:7px; font-size:10px; font-weight:600; letter-spacing:.05em; text-transform:uppercase; opacity:.6; }
    .claudex-usage-group + .claudex-usage-group { margin-top:8px; }
    .claudex-usage-name { margin-bottom:4px; overflow:hidden; font-size:12px; font-weight:600; line-height:14px; text-overflow:ellipsis; white-space:nowrap; color:var(--color-token-text-primary, currentColor); }
    .claudex-usage-window + .claudex-usage-window { margin-top:5px; }
    .claudex-usage-meta { margin-bottom:3px; font-size:10px; line-height:12px; opacity:.72; }
    .claudex-usage-meta strong,.claudex-usage-credits strong { font-weight:500; font-variant-numeric:tabular-nums; }
    .claudex-usage-track { height:3px; overflow:hidden; border-radius:99px; background:var(--color-token-bg-tertiary, rgba(127,127,127,.22)); }
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
  return { installed:false };
};

const controller = { id:scriptId, install, uninstall, refresh, get installed() { return Boolean(observer); } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail:{ id:scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(preferencesKey) || "{}"); } catch {}
return preferences[scriptId] === false ? uninstall() : install();
