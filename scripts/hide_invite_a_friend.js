// ==ClaudexUserScript==
// @name          Hide Invite a friend
// @id            hide_invite_a_friend
// @version       1.0.0
// @description   Hides the Invite a friend account-menu entry.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @grant         codex-request
// ==/ClaudexUserScript==
const stateKey = Symbol.for("claudex-yourself.hide-invite-a-friend");
const previous = window[stateKey];
previous?.uninstall?.();

const scriptId = "hide_invite_a_friend";
const preferencesKey = "claudex-yourself.userscripts.v1";
const registryKey = Symbol.for("claudex-yourself.userscript-registry");

const hideMatching = root => {
  const candidates = [];
  if (root instanceof Element && root.matches("[role='menuitem']")) candidates.push(root);
  if (root instanceof Element || root instanceof Document) {
    candidates.push(...root.querySelectorAll("[role='menuitem']"));
  }
  let hidden = 0;
  for (const element of candidates) {
    if (element.textContent?.trim().toLowerCase() !== "invite a friend") continue;
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
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof Element) hideMatching(node);
      }
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });
  return { installed: true, hiddenNow };
};
const uninstall = () => {
  observer?.disconnect();
  observer = undefined;
  for (const element of document.querySelectorAll("[data-claudex-hide-invite='true']")) {
    element.style.removeProperty("display");
    delete element.dataset.claudexHideInvite;
  }
  return { installed: false };
};

const controller = { id: scriptId, install, uninstall, get installed() { return Boolean(observer); } };
window[stateKey] = controller;
(window[registryKey] ??= new Map()).set(scriptId, controller);
window.dispatchEvent(new CustomEvent("claudex-userscript-registered", { detail: { id: scriptId } }));
let preferences = {};
try { preferences = JSON.parse(localStorage.getItem(preferencesKey) || "{}"); } catch {}
return preferences[scriptId] === false ? uninstall() : install();
