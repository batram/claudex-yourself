const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const source = fs.readFileSync("scripts/remote_device_icons.js", "utf8");
const saved = new Map();
const window = { dispatchEvent() {}, removeEventListener() {} };
const context = vm.createContext({ window, Symbol, Map, Set, console, clearTimeout,
  document: { querySelectorAll: () => [] }, CustomEvent: class {},
  localStorage: {
    getItem(key) { return key === "claudex-yourself.userscripts.v1" ? '{"remote_device_icons":false}' : saved.get(key); },
    setItem(key, value) { saved.set(key, value); }
  }
});
vm.runInContext(`(function(){${source}\n})()`, context);
const controller = window[Symbol.for("claudex-yourself.remote-device-icons")];
assert.equal(controller.installed, false);
assert.equal(controller.presets.map(preset => preset.id).join(","), "apple,linux,windows,ufo,duck");
for (const preset of controller.presets) {
  const svg = decodeURIComponent(preset.src.split(",")[1]);
  assert.match(svg, /<svg xmlns="http:\/\/www.w3.org\/2000\/svg"/);
  assert.doesNotMatch(svg, /<script|href=|onload=/i);
}
assert.equal(controller.inferOs("/Users/me/develop/once"), "apple");
assert.equal(controller.inferOs("/home/me/once"), "linux");
assert.equal(controller.inferOs("/root/project"), "linux");
assert.equal(controller.inferOs("C:\\dev\\once"), "windows");
assert.equal(controller.inferOs("/srv/project"), null);
controller.setChoice("remote:test", "duck");
assert.equal(JSON.parse(saved.get("claudex-yourself.remote-device-icons.v1"))["remote:test"].choice, "duck");
assert.throws(() => controller.setChoice("remote:test", "bogus"), /Unknown icon/);
Promise.all([
  assert.rejects(controller.upload("remote:test", { size: 1, type: "text/html" }), /PNG, JPEG/),
  assert.rejects(controller.upload("remote:test", { size: 3 * 1024 * 1024, type: "image/png" }), /2 MB/)
]).then(() => console.log("Remote device icon checks passed: five self-contained presets, OS inference, preferences, upload validation, disabled lifecycle."))
  .catch(error => { console.error(error); process.exitCode = 1; });
