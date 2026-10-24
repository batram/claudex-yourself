// ==ClaudexUserScript==
// @name          Notification router
// @id            notification_router
// @version       1.2.3
// @description   Opens completion and question notifications in the controlled Codex window.
// @run-at        renderer-ready
// @platform      windows
// @grant         none
// ==/ClaudexUserScript==
const scriptId = "notification_router";
const stateKey = Symbol.for("claudex-yourself.notification-router");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
const preferencesKey = "claudex-yourself.userscripts.v1";
window[stateKey]?.uninstall?.();

const state = {
  installed: false,
  api: null,
  original: null,
  replacement: null,
  watchdog: null,
  heartbeat: 0,
  pending: new Map(),
  queue: []
};

const controller = {
  id: scriptId,
  get installed() { return state.installed; },
  beat() { state.heartbeat = Date.now(); return state.installed; },
  drain() { return state.queue.splice(0).filter(item => state.pending.has(item.id)); },
  delivered(id) {
    const entry = state.pending.get(id);
    if (!entry) return false;
    entry.delivered = true;
    return true;
  },
  fallback(id) {
    const entry = state.pending.get(id);
    if (!entry) return false;
    state.pending.delete(id);
    state.queue = state.queue.filter(item => item.id !== id);
    state.original.show(entry.payload, entry.onAction);
    return true;
  },
  async activate(id) {
    const entry = state.pending.get(id);
    if (!entry) return null;
    state.pending.delete(id);
    const route = await entry.onAction?.({ type: "open" }) ?? entry.route;
    if (!route?.path) return null;
    window.postMessage({ type: "navigate-to-route", path: route.path, state: route.state }, window.location.origin);
    return route.path;
  },
  async install() {
    if (state.installed) return { installed: true };
    // The desktop notification service is exported by Codex's current renderer bundle.
    // Keep failures visible across Codex upgrades and leave native notifications intact.
    const manifest = [...document.querySelectorAll('script[src]')]
      .map(script => script.src).find(source => source.includes("app-initial-"));
    const root = [...document.scripts].map(script => script.src)
      .find(source => source.startsWith("app://-/assets/index-"));
    if (!root) throw new Error("Codex application module is unavailable.");
    const source = await (await fetch(root, { signal: AbortSignal.timeout(6000) })).text();
    const dependency = source.match(/["'`]([^"'`]*app-shared-[\w-]+\.js)["'`]/)?.[1];
    if (!dependency) throw new Error("Codex shared notification module is unavailable.");
    const shared = await import(new URL(dependency, root).href);
    const services = Object.values(shared).filter(value => value && typeof value === "object" &&
      typeof value.notifications?.show === "function");
    if (services.length !== 1) throw new Error("Codex notification service is ambiguous or unavailable.");
    const api = services[0];
    if (typeof api?.notifications?.show !== "function") throw new Error("Codex notification service changed; router was not installed.");
    state.api = api;
    state.original = api.notifications;
    state.replacement = new Proxy(state.original, {
      get(target, property, receiver) {
        if (property !== "show") return Reflect.get(target, property, receiver);
        return (payload, onAction) => {
          if (!state.installed || !["turn-complete", "question"].includes(payload?.kind) ||
              Date.now() - state.heartbeat > 3000 || state.pending.size >= 100)
            return target.show(payload, onAction);
          // Codex resolves this callback at click time, and async questions
          // return a Promise containing the question's navigation state.
          const route = { path: payload.navigationPath };
          if (typeof route?.path !== "string" || !route.path.startsWith("/") || route.path.startsWith("//"))
            return target.show(payload, onAction);
          const id = crypto.randomUUID();
          state.pending.set(id, { payload, onAction, route, delivered: false, queuedAt: Date.now() });
          state.queue.push({ id, title: String(payload.title ?? "Codex"), body: String(payload.body ?? ""), kind: payload.kind, route });
          // The caller disposes its RPC result immediately after show().
          // Match that contract without cancelling the queued notification.
          return { [Symbol.dispose]() {} };
        };
      }
    });
    api.notifications = state.replacement;
    state.installed = true;
    state.watchdog = setInterval(() => {
      const now = Date.now();
      for (const [id, entry] of state.pending) {
        if (!entry.delivered && (now - state.heartbeat > 3000 || now - entry.queuedAt > 15000)) controller.fallback(id);
        // Clicks carry a route token, so old delivered entries need not occupy
        // the bounded pending map forever when the user dismisses a toast.
        else if (entry.delivered && now - entry.queuedAt > 300000) state.pending.delete(id);
      }
    }, 1000);
    return { installed: true, bridge: Boolean(manifest) };
  },
  uninstall() {
    if (state.api?.notifications === state.replacement) state.api.notifications = state.original;
    clearInterval(state.watchdog);
    state.watchdog = null;
    for (const [id, entry] of state.pending) if (!entry.delivered) controller.fallback(id);
    state.pending.clear();
    state.queue.length = 0;
    state.installed = false;
    state.heartbeat = 0;
    return { installed: false };
  }
};

window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail: { id: scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(preferencesKey) || "{}"); } catch {}
return preferences[scriptId] === false ? controller.uninstall() : await controller.install();

