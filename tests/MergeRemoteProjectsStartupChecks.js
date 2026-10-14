const fs = require("node:fs");
const vm = require("node:vm");
const assert = require("node:assert/strict");
const source = fs.readFileSync("scripts/merge_remote_projects.js", "utf8");

async function checkStartup(readyAfter) {
  let attempts = 0, clock = 0, removed = 0;
  const scope = new Map([[Symbol(), { store: {}, queryClient: {} }]]);
  const row = { __reactFiber$test: { dependencies: { firstContext: { memoizedValue: scope } } } };
  const window = { dispatchEvent() {} };
  const context = vm.createContext({ window, Symbol, Map, Set, console, clearTimeout,
    Date: { now: () => clock },
    setTimeout(callback, delay) { clock += delay; queueMicrotask(callback); },
    CustomEvent: class {}, MutationObserver: class { disconnect() {} },
    localStorage: { getItem: () => '{"merge_remote_projects":false}' },
    document: {
      querySelector: () => ++attempts >= readyAfter ? row : null,
      querySelectorAll: () => [],
      createElement: () => ({ textContent: "", remove() { removed++; } }),
      head: { append() {} }
    },
    performance: { getEntriesByType: () => [] }
  });
  await vm.runInContext(`(async function(){${source}\n})()`, context);
  const controller = window[Symbol.for("claudex-yourself.merge-remote-projects")];
  const expected = Number.isFinite(readyAfter)
    ? /Codex sidebar module was not found/
    : /Codex sidebar session scope was not found/;
  await assert.rejects(controller.install(), expected);
  assert.equal(controller.installed, false, "Failed startup cleans up its observer");
  assert.equal(removed, 1, "Failed startup removes its style");
  if (Number.isFinite(readyAfter)) assert.equal(attempts, readyAfter, "Waits for the sidebar scope");
  else assert.equal(clock, 10000, "Readiness wait has a ten-second bound");
}

(async () => {
  await checkStartup(4);
  await checkStartup(Infinity);
  console.log("Merge remote projects startup checks passed.");
})().catch(error => { console.error(error); process.exitCode = 1; });
