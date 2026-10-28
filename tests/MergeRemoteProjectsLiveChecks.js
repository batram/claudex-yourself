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
  const nativeMore = [...panel.parentElement.querySelectorAll("button")].find(button =>
    !button.closest(".claudex-merged-remote") && /^(Show more|Show less)$/.test(button.textContent.trim()));
  check(nativeMore, "Native pagination button is available");
  for (const property of ["fontSize", "fontFamily", "fontWeight", "lineHeight", "height", "padding", "color", "opacity"]) {
    check(getComputedStyle(more)[property] === getComputedStyle(nativeMore)[property],
      `Remote pagination matches native ${property}`);
  }
  check(getComputedStyle(more.querySelector(".text-base")).font ===
    getComputedStyle(nativeMore.querySelector(".text-base")).font, "Pagination label matches native typography");
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
    check(Math.abs(more.getBoundingClientRect().x - nativeMore.getBoundingClientRect().x) < 1,
      "Remote pagination aligns horizontally with native pagination");
    check(visible() === 5, "Preview contains five sessions");
    const checkIconColumn = () => {
      const iconPositions = rows().filter(row => !row.hidden).map(row => {
        const icon = row.querySelector("svg.icon-2xs.no-drag") || row.querySelector("[aria-label='Remote session'] svg");
        check(icon, "Each remote row has a device icon slot");
        const box = row.getBoundingClientRect(), glyph = icon.getBoundingClientRect();
        check(Math.abs(glyph.y + glyph.height / 2 - box.y - box.height / 2) < 1,
          "Device icon is vertically centered");
        return box.right - glyph.right;
      });
      check(Math.max(...iconPositions) - Math.min(...iconPositions) < 1,
        "Copied, compact, and status-bearing rows share a device icon column");
      for (const row of rows().filter(row => !row.hidden)) {
        const slot = row.querySelector(".claudex-merged-device-slot").getBoundingClientRect();
        check(row.querySelectorAll(".claudex-merged-device-slot").length === 1,
          "Each copied or catalog row owns exactly one device slot");
        for (const status of row.querySelectorAll(".claudex-merged-row-status,[role=status],button")) {
          const box = status.getBoundingClientRect();
          if (!box.width || !box.height) continue;
          check(slot.right <= box.left + 1 || slot.left >= box.right - 1,
            "Device column never overlaps working/unread status or hover controls");
        }
      }
    };
    checkIconColumn();
    more.focus();
    more.click();
    for (let index = 0; index < 3; index++) controller.refresh();
    check(visible() === total, "Show more reveals the full catalog");
    checkIconColumn();
    check(panel.querySelector(".claudex-merged-remote-more") === more, "Refresh preserves button identity");
    check(document.activeElement === more, "Refresh preserves pagination focus");
    check(foreignClicks === 0, "No native project's Show more was clicked");
    more.click();
    controller.refresh();
    check(visible() === 5, "Show less restores the preview");
    checkIconColumn();
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

async function checkNavigation() {
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  const controller = window[Symbol.for("claudex-yourself.merge-remote-projects")];
  const scroll = document.querySelector("[data-app-action-sidebar-scroll]");
  const original = document.querySelector("[data-app-action-sidebar-thread-active=true]") ||
    document.querySelector("[data-claudex-merged-active=true]");
  check(original, "The current chat has a sidebar entry to return to");
  const originalId = original.dataset.claudexSessionId || original.dataset.appActionSidebarThreadId;
  const originalHost = original.dataset.claudexHostId || original.dataset.appActionSidebarThreadHostId;
  const originalTop = scroll.scrollTop;
  const panel = [...document.querySelectorAll(".claudex-merged-remote")].find(element =>
    element.parentElement.getAttribute("aria-label") === "once");
  const more = panel.querySelector(".claudex-merged-remote-more");
  const heading = panel.querySelector(".claudex-merged-remote-label");
  const collapsed = heading.getAttribute("aria-expanded") === "false";
  const expanded = more.getAttribute("aria-expanded") === "true";
  const settle = () => new Promise(resolve => setTimeout(resolve, 400));
  try {
    if (collapsed) heading.click();
    if (!expanded) more.click();
    for (const selector of ["[data-claudex-merged-remote=row]:not(.claudex-merged-catalog-row)", ".claudex-merged-catalog-row"]) {
      const row = panel.querySelector(selector);
      check(row, `Grouped navigation fixture is available: ${selector}`);
      const id = row.dataset.claudexSessionId, host = row.dataset.claudexHostId;
      row.scrollIntoView({ block: "center" });
      const before = scroll.scrollTop;
      let cancellations = 0;
      const canceled = () => cancellations++;
      window.addEventListener("codex-sidebar-thread-reveal-cancel", canceled);
      try {
        row.click();
        // Observe multiple paints, not just one final offset: native reveal
        // runs through a ResizeObserver after route effects and row mounts.
        for (let frame = 0; frame < 12; frame++) {
          await new Promise(resolve => setTimeout(resolve, 50));
          check(Math.abs(scroll.scrollTop - before) < 1, "Grouped navigation stays at the clicked entry during delayed rendering");
        }
        check(cancellations > 0, "Grouped navigation cancels the native Recents revealer");
      } finally { window.removeEventListener("codex-sidebar-thread-reveal-cancel", canceled); }
      controller.refresh();
      check(Math.abs(scroll.scrollTop - before) < 1, "Grouped navigation preserves sidebar scroll");
      check([...panel.querySelectorAll("[data-claudex-merged-active=true]")].some(element =>
        element.dataset.claudexSessionId === id && element.dataset.claudexHostId === host &&
        element.getAttribute("aria-current") === "page"), "Clicked grouped entry is highlighted");
      scroll.dispatchEvent(new WheelEvent("wheel", {deltaY:30,bubbles:true}));
      scroll.scrollTop += 30;
      const moved = scroll.scrollTop;
      controller.refresh();
      await new Promise(resolve => setTimeout(resolve, 100));
      check(Math.abs(scroll.scrollTop - moved) < 1, "User scrolling immediately releases navigation protection");
    }
    return { copiedRowNavigation: true, compactRowNavigation: true, scrollPreserved: true, nativeRevealCanceled: true, userScrollReleased: true };
  } finally {
    const candidates = [...document.querySelectorAll("[data-app-action-sidebar-thread-row],[data-claudex-merged-remote=row]")];
    const back = candidates.find(element =>
      (element.dataset.claudexSessionId || element.dataset.appActionSidebarThreadId) === originalId &&
      (element.dataset.claudexHostId || element.dataset.appActionSidebarThreadHostId) === originalHost);
    back?.click();
    await settle();
    if ((more.getAttribute("aria-expanded") === "true") !== expanded) more.click();
    if ((heading.getAttribute("aria-expanded") === "false") !== collapsed) heading.click();
    scroll.scrollTop = originalTop;
  }
}

async function main() {
  const endpoint = process.env.CLAUDEX_CDP_URL || "http://127.0.0.1:9229";
  const targets = await (await fetch(`${endpoint}/json/list`)).json();
  const target = targets.find(item => item.type === "page" && item.url === "app://-/index.html");
  assert(target, "A controlled main renderer is available");
  const result = await evaluate(target, `(${checkPagination.toString()})()`);
  console.log("Merge remote projects live checks passed:", JSON.stringify(result));
  const navigation = await evaluate(target, `(${checkNavigation.toString()})()`);
  console.log("Grouped navigation checks passed:", JSON.stringify(navigation));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
