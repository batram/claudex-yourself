// ==ClaudexUserScript==
// @name          Hide pets button
// @id            hide_pets_button
// @version       1.1.0
// @description   Hides the Show pet / Hide pet account-menu entry.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @codex-tested  26.901.5280.0
// @grant         none
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.hide-pets-button");
const previous = window[stateKey];
previous?.uninstall?.();

const scriptId = "hide_pets_button";
const preferencesKey = "claudex-yourself.userscripts.v1";
const registryKey = Symbol.for("claudex-yourself.userscript-registry");

const labels = new Set(["show pet", "hide pet"]);
const menuitemSelector = "[role='menuitem']";
const normalize = text => (text || "").replace(/\s+/g, " ").trim().toLowerCase();
const savedDisplays = new Map();
const isPetItem = element => {
  if (!element.matches(menuitemSelector)) return false;
  if (labels.has(normalize(element.getAttribute("aria-label"))) ||
      labels.has(normalize(element.textContent))) return true;
  // Codex now renders the label and keyboard shortcut in separate spans.
  // Match the main label exactly; a prefix match could hide unrelated commands.
  return [...element.querySelectorAll("span.flex-1")].some(label =>
    label.closest(menuitemSelector) === element && labels.has(normalize(label.textContent)));
};
const restore = element => {
  const saved = savedDisplays.get(element);
  if (!saved) return;
  if (saved.value) element.style.setProperty("display", saved.value, saved.priority);
  else element.style.removeProperty("display");
  delete element.dataset.claudexHidePets;
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
    if (!isPetItem(element)) { restore(element); continue; }
    if (!savedDisplays.has(element)) savedDisplays.set(element, {
      value: element.style.getPropertyValue("display"),
      priority: element.style.getPropertyPriority("display")
    });
    if (element.style.getPropertyValue("display") !== "none" ||
        element.style.getPropertyPriority("display") !== "important") {
      element.style.setProperty("display", "none", "important");
    }
    if (element.dataset.claudexHidePets !== "true") element.dataset.claudexHidePets = "true";
    hidden++;
  }
  return hidden;
};

let observer;
const install = () => {
  observer?.disconnect();
  const hiddenNow = hideMatching(document);
  observer = new MutationObserver(records => {
    // Release closed menus and restore rows React has reused for another command.
    for (const element of savedDisplays.keys()) {
      if (!element.isConnected || !isPetItem(element)) restore(element);
    }
    for (const record of records) {
      hideMatching(record.target);
      for (const node of record.addedNodes) {
        hideMatching(node);
      }
    }
  });
  observer.observe(document.body, {
    childList: true, subtree: true, characterData: true,
    attributes: true, attributeFilter: ["role", "aria-label", "style"]
  });
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
