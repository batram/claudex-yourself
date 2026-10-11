// Requires the installed script and a controlled main renderer. Exercises real
// pagination and restores the project's previous expansion preferences.
const assert = require("node:assert/strict");
const { evaluate } = require("./AutoloadWindowChecks.js");

async function checkPagination() {
  const controller = window[Symbol.for("claudex-yourself.merge-remote-projects")];
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  check(controller?.installed, "Merge remote projects is installed");
  const native = document.querySelector("[data-app-action-sidebar-thread-row]");
  const compact = document.querySelector(".claudex-merged-catalog-row");
  check(native && compact, "Native and compact rows are available for style comparison");
  for (const property of ["fontSize", "fontFamily", "fontWeight", "lineHeight", "height", "padding", "color", "backgroundColor"]) {
    check(getComputedStyle(compact)[property] === getComputedStyle(native)[property],
      `Compact row matches native ${property}`);
  }
  check(getComputedStyle(compact.querySelector(".claudex-merged-catalog-title")).lineHeight ===
    getComputedStyle(native.querySelector("div")).lineHeight, "Compact title matches native line height");
  const panel = [...document.querySelectorAll(".claudex-merged-remote")].find(element =>
    element.parentElement.getAttribute("aria-label") === "once");
  check(panel, "The once project has a remote panel");
  const more = panel.querySelector(".claudex-merged-remote-more");
  const heading = panel.querySelector(".claudex-merged-remote-label");
  const rows = () => [...panel.querySelectorAll("[data-claudex-merged-remote=row]")];
  const visible = () => rows().filter(row => !row.hidden).length;
  const total = rows().length;
  check(total > 5, "More than five remote sessions are known");
  const wasCollapsed = heading.getAttribute("aria-expanded") === "false";
  const wasExpanded = more.getAttribute("aria-expanded") === "true";
  const oldFocus = document.activeElement;
  const otherButtons = [...document.querySelectorAll("[data-app-action-sidebar-scroll] button")]
    .filter(button => !button.closest(".claudex-merged-remote") && button.textContent.trim() === "Show more");
  let foreignClicks = 0;
  const foreignClick = () => foreignClicks++;
  otherButtons.forEach(button => button.addEventListener("click", foreignClick));
  try {
    if (wasCollapsed) heading.click();
    if (wasExpanded) more.click();
    check(visible() === 5, "Preview contains five sessions");
    more.focus();
    more.click();
    for (let index = 0; index < 3; index++) controller.refresh();
    check(visible() === total, "Show more reveals the full catalog");
    check(panel.querySelector(".claudex-merged-remote-more") === more, "Refresh preserves button identity");
    check(document.activeElement === more, "Refresh preserves pagination focus");
    check(foreignClicks === 0, "No native project's Show more was clicked");
    more.click();
    controller.refresh();
    check(visible() === 5, "Show less restores the preview");
    check(rows().length === total && !more.hidden, "Sessions and Show more remain available");
    const mountedRemoteRows = [...document.querySelectorAll("[data-app-action-sidebar-thread-row]")]
      .filter(row => row.dataset.appActionSidebarThreadHostId?.startsWith("remote"));
    check(total > mountedRemoteRows.length, "The catalog includes sessions outside mounted Recents rows");
    return { total, preview: visible(), mountedRemoteRows: mountedRemoteRows.length, foreignClicks, nativeStylesMatch: true };
  } finally {
    if ((more.getAttribute("aria-expanded") === "true") !== wasExpanded) more.click();
    if ((heading.getAttribute("aria-expanded") === "false") !== wasCollapsed) heading.click();
    otherButtons.forEach(button => button.removeEventListener("click", foreignClick));
    if (oldFocus?.isConnected) oldFocus.focus(); else more.blur();
  }
}

async function main() {
  const endpoint = process.env.CLAUDEX_CDP_URL || "http://127.0.0.1:9229";
  const targets = await (await fetch(`${endpoint}/json/list`)).json();
  const target = targets.find(item => item.type === "page" && item.url === "app://-/index.html");
  assert(target, "A controlled main renderer is available");
  const result = await evaluate(target, `(${checkPagination.toString()})()`);
  console.log("Merge remote projects live checks passed:", JSON.stringify(result));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
