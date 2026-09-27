// ==ClaudexUserScript==
// @name          Hide Invite a friend
// @id            hide_invite_a_friend
// @version       1.1.0
// @description   Hides the Invite a friend account-menu entry.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @grant         none
// @codex-tested  26.924.2738.0
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.hide-invite-a-friend");
const previous = window[stateKey];
previous?.uninstall?.();

const scriptId = "hide_invite_a_friend";
const preferencesKey = "claudex-yourself.userscripts.v1";
const registryKey = Symbol.for("claudex-yourself.userscript-registry");

const menuitemSelector = "[role='menuitem']";
const normalize = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
const savedDisplays = new Map();
const isInviteItem = element => element.matches(menuitemSelector) && (
  normalize(element.getAttribute("aria-label")) === "invite a friend" ||
  normalize(element.textContent) === "invite a friend" ||
  [...element.querySelectorAll("span")].some(span =>
    span.closest(menuitemSelector) === element && normalize(span.textContent) === "invite a friend")
);
const restore = element => {
  const saved = savedDisplays.get(element);
  if (!saved) return;
  if (saved.value) element.style.setProperty("display", saved.value, saved.priority);
  else element.style.removeProperty("display");
  delete element.dataset.claudexHideInvite;
  savedDisplays.delete(element);
};
const hideMatching = root => {
  const candidates = new Set();
  const element = root instanceof Element ? root : root.parentElement;
  const owner = element?.closest(menuitemSelector);
  if (owner) candidates.add(owner);
  if (root instanceof Element || root instanceof Document) {
    for (const item of root.querySelectorAll(menuitemSelector)) candidates.add(item);
  }
  let hidden = 0;
  for (const element of candidates) {
    if (!isInviteItem(element)) { restore(element); continue; }
    if (!savedDisplays.has(element)) savedDisplays.set(element, {
      value:element.style.getPropertyValue("display"),
      priority:element.style.getPropertyPriority("display")
    });
    element.style.setProperty("display", "none", "important");
    element.dataset.claudexHideInvite = "true";
    hidden++;
  }
  return hidden;
};

let observer;
const install = () => {
  observer?.disconnect();
  const hiddenNow = hideMatching(document);
  observer = new MutationObserver(records => {
    for (const element of savedDisplays.keys()) {
      if (!element.isConnected || !isInviteItem(element)) restore(element);
    }
    for (const record of records) {
      hideMatching(record.target);
      for (const node of record.addedNodes) {
        hideMatching(node);
      }
    }
  });
  observer.observe(document.body, { childList:true, subtree:true, characterData:true, attributes:true,
    attributeFilter:["role", "aria-label"] });
  return { installed: true, hiddenNow };
};
const uninstall = () => {
  observer?.disconnect();
  observer = undefined;
  for (const element of savedDisplays.keys()) restore(element);
  return { installed: false };
};

const controller = { id: scriptId, install, uninstall, get installed() { return Boolean(observer); } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail: { id: scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(preferencesKey) || "{}"); } catch {}
return preferences[scriptId] === false ? uninstall() : install();
