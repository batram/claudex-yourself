// ==ClaudexUserScript==
// @name          Merge remote projects
// @id            merge_remote_projects
// @version       1.4.4
// @description   Groups Codex's loaded remote session catalog under matching local projects.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @codex-tested  26.1007.2314.0
// @codex-tested  26.1007.9446.0
// @codex-tested  20156
// @grant         none
// @codex-tested  20770
// ==/ClaudexUserScript==
const scriptId = "merge_remote_projects";
const stateKey = Symbol.for("claudex-yourself.merge-remote-projects");
const registryKey = Symbol.for("claudex-yourself.userscript-registry");
window[stateKey]?.uninstall?.();
const projectSelector = "[data-app-action-sidebar-project-row]";
const threadSelector = "[data-app-action-sidebar-thread-row]";
const ownedSelector = "[data-claudex-merged-remote]";
const normalize = value => String(value || "").trim().toLowerCase();
const folder = value => normalize(String(value || "").replace(/[\\/]+$/, "").split(/[\\/]/).pop());
const repo = value => {
  if (!value) return null;
  if (value.owner && value.repoName) return normalize(`${value.owner}/${value.repoName}`);
  // Native project metadata supplies owner/repo without a host; compare that same identity.
  const path = String(value).replace(/[?#].*$/, "").replace(/\.git\/?$/i, "")
    .replace(/^[^/@]+@[^/:]+:/, "").replace(/^[a-z]+:\/\/[^/]+\//i, "");
  const parts = path.split("/").filter(Boolean);
  return parts.length >= 2 ? normalize(parts.slice(-2).join("/")) : null;
};
const repositories = value => new Set([
  repo(value.gitInfo?.originUrl),
  ...(value.gitRepos || []).map(entry => repo(entry.ownerRepo) || repo(entry.originUrl))
].filter(Boolean));
const score = (remote, local) => {
  const a = repositories(remote), b = repositories(local);
  if (a.size && b.size) return [...a].some(id => b.has(id)) ? 100 : 0;
  const localNames = new Set([normalize(local.label), ...(local.rootPaths || [local.path]).map(folder)].filter(Boolean));
  const remoteNames = [normalize(remote.label), folder(remote.cwd || remote.path)];
  return remoteNames.some(name => name && localNames.has(name)) ? 10 : 0;
};
const match = (remote, locals) => {
  const ranked = locals.map(local => ({ local, score: score(remote, local) })).filter(entry => entry.score)
    .sort((a, b) => b.score - a.score);
  return ranked.length && (ranked.length === 1 || ranked[0].score > ranked[1].score) ? ranked[0].local : null;
};
const propsAbove = (element, predicate) => {
  let fiber = element?.[Object.keys(element).find(key => key.startsWith("__reactFiber$"))];
  for (let depth = 0; fiber && depth < 45; depth++, fiber = fiber.return) {
    if (predicate(fiber.memoizedProps || {})) return fiber.memoizedProps;
  }
  return null;
};
const groupOf = row => propsAbove(row, props => Boolean(props.group?.projectId))?.group;
const threadKey = row => `${row.dataset.appActionSidebarThreadHostId || "local"}:${row.dataset.appActionSidebarThreadId}`;
const threadInfo = row => propsAbove(row, props => Boolean(props.threadSummary) ||
  Boolean(props.conversationId && Object.hasOwn(props, "displayCwd")));
let nativeCatalog, catalogSubscription;
const loadNativeCatalog = async () => {
  // App upgrades can reach renderer-ready before the sidebar has mounted.
  // Wait for its scope instead of permanently disabling grouping on launch.
  let chain;
  for (let attempt = 0; attempt < 100 && !chain; attempt++) {
    if (!observer) return;
    const row = document.querySelector(threadSelector) || document.querySelector(projectSelector);
    let fiber = row?.[Object.keys(row).find(key => key.startsWith("__reactFiber$"))];
    for (; fiber && !chain; fiber = fiber.return) {
      for (let dep = fiber.dependencies?.firstContext; dep; dep = dep.next) {
        if (dep.memoizedValue instanceof Map && [...dep.memoizedValue.values()].some(value => value?.store && value?.queryClient)) {
          chain = dep.memoizedValue; break;
        }
      }
    }
    if (!chain) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!chain) throw new Error("Codex sidebar session scope was not found within 10 seconds.");
  const url = [...document.querySelectorAll("script[src],link[href]")].map(element => element.src || element.href)
    .concat(performance.getEntriesByType("resource").map(entry => entry.name))
    .find(name => /\/app-initial-[^/]+\.js(?:\?|$)/.test(name));
  if (!url) throw new Error("Codex sidebar module was not found.");
  const source = await (await fetch(url)).text();
  const exports = source.slice(source.lastIndexOf("export{"));
  const alias = symbol => {
    const name = exports.match(new RegExp(`(?:,|\\{)${symbol.replaceAll("$", "\\$")} as ([^,}]+)`))?.[1];
    if (!name) throw new Error(`Codex sidebar catalog export ${symbol} is unavailable.`);
    return name;
  };
  // These private symbols are behaviorally checked against the declared Codex
  // build. Resolve the generated export names rather than pinning asset hashes.
  const main = await import(url);
  const sharedUrl = new URL(source.match(/from\s*["']([^"']*app-shared-[^"']+)["']/)?.[1] || "", url).href;
  if (sharedUrl === url) throw new Error("Codex shared session module was not found.");
  const shared = await import(sharedUrl);
  const imported = symbol => {
    const name = source.match(new RegExp(`[,\\{]([\\w$]+) as ${symbol}[,\\}]`))?.[1];
    if (!name || !shared[name]) throw new Error(`Codex session export ${symbol} is unavailable.`);
    return shared[name];
  };
  const get = (node, ...args) => {
    const scope = chain.get(node.scope.id);
    if (!scope) throw new Error("Codex session scope changed.");
    return args.length ? node.read(scope, chain, args[0]) : scope.store.get(node.resolve(scope, chain));
  };
  // Build 20770 moved navigation atoms into app-initial and renamed the
  // catalog symbols. Validate the old adapter before choosing it: a reused
  // minifier name can still be exported while referring to an unrelated function.
  const legacyKeys = main[alias("Gtr")];
  // Preserve the separately tested Windows 26.1007.9446 adapter as well.
  const windowsList = exports.match(/(?:,|\{)_F as ([^,}]+)/)?.[1];
  const windows = typeof main[windowsList] === "function" &&
    /conversationId/.test(String(main[windowsList])) && /workspaceKind/.test(String(main[windowsList]));
  const legacy = !windows && Boolean(legacyKeys?.scope && legacyKeys?.resolve);
  const keys = windows ? main[alias("EF")] : legacy ? legacyKeys : main[alias("$Ki")];
  const list = main[alias(windows ? "_F" : legacy ? "yF" : "qB")];
  const conversation = windows ? imported("fc") : legacy ? imported("mc") : main[alias("ek")];
  const router = windows ? imported("jf") : legacy ? imported("Nf") : main[alias("eh")];
  const location = windows ? imported("Ug") : legacy ? imported("Jg") : main[alias("ch")];
  if (!keys?.scope || typeof list !== "function" || !router?.scope || !location?.scope)
    throw new Error("Codex sidebar catalog adapter is incompatible with this build.");
  nativeCatalog = {
    read: () => list(get, get(keys)).filter(item => item.kind === "local").map(item => {
      const summary = item.summary || get(conversation, { hostId: item.hostId || "local", threadId: item.conversationId })?.summary;
      return { ...item, summary, title: summary?.title || item.conversationId };
    }),
    navigate: (id, hostId, projectId) => get(router).navigate(`/local/${encodeURIComponent(id)}?hostId=${encodeURIComponent(hostId)}`, {
      state: { ...get(location)?.state, sidebarThreadLocationId: `unified-project:${projectId}` }
    }),
    active: () => {
      const current = get(location);
      return { id: current?.pathname?.match(/^\/local\/([^/]+)/)?.[1],
        hostId: new URLSearchParams(current?.search || "").get("hostId") || "local" };
    },
    subscribe: () => {
      const scope = chain.get(keys.scope.id);
      const offKeys = scope.store.sub(keys.resolve(scope, chain), schedule);
      const locationScope = chain.get(location.scope.id);
      const offLocation = locationScope.store.sub(location.resolve(locationScope, chain), schedule);
      return () => { offKeys(); offLocation(); };
    }
  };
};
const collapsedKey = "claudex-yourself.merge-remote-projects.collapsed.v1";
let collapsed = {};
try { collapsed = JSON.parse(localStorage.getItem(collapsedKey) || "{}"); } catch {}
if (!collapsed || typeof collapsed !== "object" || Array.isArray(collapsed)) collapsed = {};
const expandedKey = "claudex-yourself.merge-remote-projects.expanded.v1";
let expanded = {};
try { expanded = JSON.parse(localStorage.getItem(expandedKey) || "{}"); } catch {}
if (!expanded || typeof expanded !== "object" || Array.isArray(expanded)) expanded = {};
const previewCount = 5;
const updateLimit = (panel, projectId) => {
  const rows = [...panel.body.querySelectorAll(":scope > [data-claudex-merged-remote='row']")];
  const showAll = expanded[projectId] === true;
  rows.forEach((row, index) => { row.hidden = !showAll && index >= previewCount; });
  panel.more.hidden = panel.body.hidden || rows.length <= previewCount;
  panel.moreLabel.textContent = showAll ? "Show less" : "Show more";
  panel.moreContainer.hidden = panel.more.hidden;
  panel.more.setAttribute("aria-expanded", String(showAll));
  panel.more.setAttribute("aria-label", showAll ? "Show fewer remote chats" : `Show all ${rows.length} remote chats`);
};
let observer, timer, style;
let lastStatus = { projects: 0, sessions: 0, hiddenRemoteProjects: 0 };
const panels = new Map();
const hidden = new Set();
const hostIcons = new Map();
const observe = () => observer?.observe(document.body, {
  childList: true, subtree: true, characterData: true, attributes: true,
  attributeFilter: ["data-app-action-sidebar-project-collapsed", "data-app-action-sidebar-thread-active", "data-app-action-sidebar-thread-title", "data-app-action-sidebar-thread-selected", "class"]
});
const removePanel = (id, panel) => { panel.element.remove(); panels.delete(id); };
const makeCopy = (source, session, projectId) => {
  const copy = source.cloneNode(true);
  copy.dataset.claudexMergedRemote = "row";
  copy.dataset.claudexSessionId = session.conversationId;
  copy.dataset.claudexHostId = session.hostId;
  // Cloned native DOM must not masquerade as a second native sidebar/drop target.
  for (const element of [copy, ...copy.querySelectorAll("*")]) {
    for (const attribute of [...element.attributes]) {
      if (attribute.name === "id" || attribute.name.startsWith("data-app-action-") ||
          attribute.name.startsWith("data-sidebar-") || attribute.name.startsWith("aria-describedby") ||
          attribute.name === "aria-labelledby" || attribute.name === "draggable") element.removeAttribute(attribute.name);
    }
  }
  const hostId = source.dataset.appActionSidebarThreadHostId || "remote";
  const info = threadInfo(source);
  copy.title = `${source.dataset.appActionSidebarThreadTitle || source.textContent.trim()}\nRemote: ${hostId}\n${info?.threadSummary?.cwd || info?.displayCwd || ""}`;
  // Dispatch on the still-mounted original so React retains navigation, archive,
  // multi-selection, and context menu handlers with the original host identity.
  for (const type of ["click", "dblclick", "contextmenu"]) copy.addEventListener(type, event => {
    event.preventDefault(); event.stopPropagation();
    if (type === "click" && !event.target.closest("button") && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
      nativeCatalog.navigate(session.conversationId, session.hostId, projectId);
      return;
    }
    if (!source.isConnected) {
      if (type === "click" && !event.target.closest("button")) nativeCatalog.navigate(session.conversationId, session.hostId, projectId);
      schedule(); return;
    }
    const indices = [];
    let node = event.target;
    while (node && node !== copy) { indices.unshift([...node.parentNode.childNodes].indexOf(node)); node = node.parentNode; }
    let target = source;
    for (const index of indices) target = target.childNodes[index] || target;
    target.dispatchEvent(new MouseEvent(type, {
      bubbles: true, cancelable: true, view: window, button: event.button, buttons: event.buttons,
      clientX: event.clientX, clientY: event.clientY, ctrlKey: event.ctrlKey, metaKey: event.metaKey,
      shiftKey: event.shiftKey, altKey: event.altKey, detail: event.detail
    }));
  }, true);
  copy.addEventListener("keydown", event => {
    if (event.key === "Enter" || event.key === " ") { event.preventDefault(); event.stopPropagation(); event.target.click(); }
    else if (event.key === "ContextMenu" || event.key === "F10" && event.shiftKey) {
      event.preventDefault(); copy.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
    }
  });
  return copy;
};
const makeCatalogRow = (session, projectId) => {
  const row = document.createElement("button");
  row.type = "button";
  row.dataset.claudexMergedRemote = "row";
  row.dataset.claudexSessionId = session.conversationId;
  row.dataset.claudexHostId = session.hostId;
  row.className = "claudex-merged-catalog-row sidebar-item group relative flex items-center gap-2 w-full cursor-interaction text-sm hover:bg-primary-ghost-hover h-[var(--height-token-row)] py-row-y pe-row-y ps-[var(--padding-row-cell-x,var(--padding-row-x))] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring";
  row.title = `${session.title}\nRemote: ${session.hostId}\n${session.cwd || ""}`;
  const title = document.createElement("span");
  title.className = "claudex-merged-catalog-title leading-4";
  title.textContent = session.title;
  const icon = document.createElement("span");
  icon.className = "inline-flex shrink-0 text-tertiary";
  const nativeIcon = hostIcons.get(session.hostId);
  if (nativeIcon) icon.append(nativeIcon.cloneNode(true));
  else icon.innerHTML = '<svg aria-hidden="true" class="icon-2xs" width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor"><circle cx="8" cy="8" r="6"/><ellipse cx="8" cy="8" rx="2.5" ry="6"/><path d="M2 8h12"/></svg>';
  icon.setAttribute("aria-label", "Remote session");
  const iconSlot = document.createElement("span");
  iconSlot.className = "claudex-merged-device-slot";
  iconSlot.append(icon);
  row.append(title, iconSlot);
  row.addEventListener("click", event => {
    event.preventDefault(); event.stopPropagation();
    nativeCatalog.navigate(session.conversationId, session.hostId, projectId);
  });
  return row;
};
const refresh = () => {
  if (!observer) return;
  observer.disconnect();
  try {
    const sidebar = document.querySelector("[data-app-action-sidebar-scroll]");
    const locals = [], remotes = [];
    for (const row of sidebar?.querySelectorAll(projectSelector) || []) {
      const group = groupOf(row);
      const container = row.closest("[role='listitem'][data-sidebar-project-kind]");
      if (!group || !container) continue;
      const entry = { ...group, row, container };
      (group.projectKind === "local" ? locals : remotes).push(entry);
    }
    if (!nativeCatalog) return;
    const buckets = new Map(), mounted = new Map();
    for (const row of sidebar?.querySelectorAll(threadSelector) || []) {
      if (!row.closest(ownedSelector)) {
        mounted.set(threadKey(row), row);
        const icon = row.querySelector("svg.icon-2xs.no-drag");
        if (icon) hostIcons.set(row.dataset.appActionSidebarThreadHostId, icon.cloneNode(true));
      }
    }
    for (const session of nativeCatalog.read()) {
      if (!session.hostId || session.hostId === "local" || session.hostId === "durable" || session.workspaceKind === "projectless") continue;
      const group = remotes.find(group => group.hostId === session.hostId && folder(group.path) === folder(session.cwd));
      const remote = { ...session.summary, cwd: session.cwd, label: group?.label, gitRepos: group?.gitRepos };
      const local = match(remote, locals);
      if (!local) continue;
      const key = `${session.hostId}:local:${session.conversationId}`;
      const entries = buckets.get(local.projectId) || [];
      entries.push({ row: mounted.get(key), session, key,
        updated: session.summary?.recencyAt || session.summary?.updatedAt || 0 });
      buckets.set(local.projectId, entries);
    }
    for (const element of hidden) element.removeAttribute("data-claudex-merged-hidden");
    hidden.clear();
    for (const [id, panel] of panels) if (!buckets.has(id) || !panel.element.isConnected) removePanel(id, panel);
    let sessions = 0;
    for (const local of locals) {
      const entries = buckets.get(local.projectId);
      if (!entries) continue;
      entries.sort((a, b) => b.updated - a.updated);
      let panel = panels.get(local.projectId);
      if (!panel) {
        const element = document.createElement("div");
        element.dataset.claudexMergedRemote = "panel";
        element.className = "claudex-merged-remote";
        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "claudex-merged-remote-label hover:bg-primary-ghost-hover";
        toggle.innerHTML = '<svg class="claudex-remote-chevron" aria-hidden="true" width="10" height="10" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="m6 3 5 5-5 5"/></svg><svg aria-hidden="true" width="13" height="13" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2"><circle cx="8" cy="8" r="6"/><ellipse cx="8" cy="8" rx="2.5" ry="6"/><path d="M2 8h12"/></svg><span class="claudex-remote-heading-text"></span>';
        const body = document.createElement("div");
        body.id = `claudex-remote-${encodeURIComponent(local.projectId)}`;
        body.className = "claudex-merged-remote-entries";
        const nativeMore = [...document.querySelectorAll("[data-app-action-sidebar-scroll] button")]
          .find(button => !button.closest(ownedSelector) && /^(Show more|Show less)$/.test(button.textContent.trim()));
        const more = nativeMore ? nativeMore.cloneNode(true) : document.createElement("button");
        more.type = "button";
        more.classList.add("claudex-merged-remote-more");
        if (!nativeMore) more.classList.add("text-sm", "text-tertiary");
        const moreLabel = more.querySelector(".text-base") || more;
        const moreContainer = document.createElement("div");
        moreContainer.className = "claudex-merged-remote-more-container flex gap-1 py-1 ps-2";
        moreContainer.append(more);
        more.setAttribute("aria-controls", body.id);
        more.addEventListener("click", event => {
          event.stopPropagation();
          expanded[local.projectId] = expanded[local.projectId] !== true;
          localStorage.setItem(expandedKey, JSON.stringify(expanded));
          updateLimit(panels.get(local.projectId), local.projectId);
        });
        toggle.setAttribute("aria-controls", body.id);
        toggle.addEventListener("click", event => {
          event.stopPropagation();
          collapsed[local.projectId] = collapsed[local.projectId] !== true;
          localStorage.setItem(collapsedKey, JSON.stringify(collapsed));
          body.hidden = collapsed[local.projectId];
          toggle.setAttribute("aria-expanded", String(!body.hidden));
          updateLimit(panels.get(local.projectId), local.projectId);
        });
        element.append(toggle, body, moreContainer);
        local.container.append(element);
        panels.set(local.projectId, panel = { element, toggle, body, more, moreLabel, moreContainer, signature: "" });
      }
      if (panel.element.parentElement !== local.container) local.container.append(panel.element);
      panel.element.hidden = local.row.dataset.appActionSidebarProjectCollapsed === "true";
      panel.body.hidden = collapsed[local.projectId] === true;
      panel.toggle.setAttribute("aria-expanded", String(!panel.body.hidden));
      panel.toggle.querySelector(".claudex-remote-heading-text").textContent = `Remote (${entries.length})`;
      const signature = entries.map(entry => `${entry.key}:${entry.row?.outerHTML || entry.session.title}`).join("\n");
      const sourcesChanged = !panel.sources || entries.some((entry, index) => panel.sources[index] !== entry.row);
      if (signature !== panel.signature || sourcesChanged) {
        // Keep the pagination button outside the replaceable list. Never remove
        // the clicked control or let it become a native sibling's click target.
        panel.body.replaceChildren(...entries.map(entry => entry.row ? makeCopy(entry.row, entry.session, local.projectId) : makeCatalogRow(entry.session, local.projectId)));
        panel.signature = signature;
        panel.sources = entries.map(entry => entry.row);
      }
      const active = nativeCatalog.active();
      [...panel.body.children].forEach((row, index) => {
        const session = entries[index]?.session;
        const selected = session?.conversationId === active.id && session.hostId === active.hostId;
        row.classList.toggle("bg-primary-ghost-hover", selected);
        row.dataset.claudexMergedActive = String(selected);
        if (selected) row.setAttribute("aria-current", "page"); else row.removeAttribute("aria-current");
      });
      updateLimit(panel, local.projectId);
      sessions += entries.length;
    }
    // Hide duplicate remote folders only when every known session is represented.
    // A collapsed/unloaded remote group stays available instead of losing its chats.
    for (const remote of remotes) {
      const local = match(remote, locals);
      const entries = local && buckets.get(local.projectId);
      const ids = new Set((entries || []).filter(entry => !remote.hostId || entry.session.hostId === remote.hostId)
        .map(entry => entry.session.conversationId));
      if (local && remote.threadKeys?.length && remote.threadKeys.every(key => ids.has(key.split(":").pop()))) {
        remote.container.setAttribute("data-claudex-merged-hidden", ""); hidden.add(remote.container);
      }
    }
    lastStatus = { projects: buckets.size, sessions, hiddenRemoteProjects: hidden.size };
  } catch (error) {
    lastStatus = { ...lastStatus, error: String(error) };
    console.warn("[Claudex merge remote projects]", error);
  } finally { observe(); }
};
const schedule = () => { clearTimeout(timer); timer = setTimeout(refresh, 100); };
const uninstall = () => {
  observer?.disconnect(); observer = undefined; clearTimeout(timer);
  catalogSubscription?.(); catalogSubscription = undefined;
  for (const [id, panel] of panels) removePanel(id, panel);
  for (const element of hidden) element.removeAttribute("data-claudex-merged-hidden");
  hidden.clear(); style?.remove(); style = undefined;
  return { installed: false };
};
const install = async () => {
  uninstall();
  style = document.createElement("style");
  // Native collapse/reopen can append its remounted rows after our panel. Flex
  // order keeps Remote after them regardless of React's DOM insertion order.
  style.textContent = "[data-claudex-merged-hidden]{display:none!important}.claudex-merged-remote[hidden],.claudex-merged-remote-entries[hidden]{display:none!important}.claudex-merged-remote{order:1;padding-inline-start:24px}.claudex-merged-remote-label{display:flex;align-items:center;gap:5px;width:100%;border:0;background:none;color:inherit;text-align:start;cursor:pointer;font-size:11px;opacity:.65;padding:5px 8px 2px}.claudex-merged-remote-label:hover{opacity:1}.claudex-merged-remote-label:focus-visible{outline:2px solid var(--color-text-info,currentColor);outline-offset:-2px}.claudex-merged-remote-label:before{content:'>';display:inline-block;transition:transform .12s}.claudex-merged-remote-label[aria-expanded=true]:before{transform:rotate(90deg)}.claudex-merged-remote [data-claudex-merged-remote=row]{content-visibility:visible}";
  document.head.append(style);
  style.textContent += ".claudex-merged-remote-label{margin-inline-start:-12px;width:calc(100% + 12px);padding:6px 8px;gap:6px;border-radius:6px;font-size:12px;font-weight:500;color:var(--color-text-secondary,inherit);opacity:1}.claudex-merged-remote-label:before{content:none}.claudex-remote-chevron{flex:none;opacity:.6;transition:transform .12s}.claudex-merged-remote-label[aria-expanded=true] .claudex-remote-chevron{transform:rotate(90deg)}";
  style.textContent += ".claudex-merged-remote [data-claudex-merged-remote=row][hidden],.claudex-merged-remote-more[hidden],.claudex-merged-remote-more-container[hidden]{display:none!important}";
  // Let native utilities supply typography, spacing, hover/focus and theme
  // colors. Inheriting the container's font made fallback rows 16px vs 13px.
  style.textContent += ".claudex-merged-catalog-row{border:0;text-align:start}.claudex-merged-catalog-title{flex:1;min-width:0;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}";
  // Userscript-only utility classes may not exist in Codex's generated CSS.
  // Own the slot geometry so native-row remounts cannot expose a missing margin.
  style.textContent += ".claudex-merged-device-slot{display:flex;align-items:center;justify-content:center;flex-shrink:0;min-width:calc(var(--spacing,4px)*5);height:calc(var(--spacing,4px)*5);margin-inline-end:calc(var(--spacing,4px)*.75)}";
  // Keep the host in a stable rightmost slot, even when a native status badge
  // is present. CSS ordering preserves React's DOM and forwarded event paths.
  style.textContent += ".claudex-merged-remote [data-claudex-merged-remote=row] [data-hover-card-open-immediately]>span:has([data-testid=task-row-environment-remote]){order:1}";
  observer = new MutationObserver(schedule);
  try {
    if (!nativeCatalog) await loadNativeCatalog();
    if (!observer) return { installed: false };
    catalogSubscription = nativeCatalog.subscribe();
  } catch (error) {
    uninstall();
    throw error;
  }
  refresh();
  return { installed: true, ...lastStatus };
};
const controller = { id: scriptId, name: "Merge remote projects",
  description: "Groups remote chats under matching local projects.", install, uninstall, refresh,
  get installed() { return Boolean(observer); }, get status() { return lastStatus; },
  matching: { repo, folder, score, match } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail: { id: scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem("claudex-yourself.userscripts.v1") || "{}"); } catch {}
return preferences[scriptId] === false ? uninstall() : await install();
