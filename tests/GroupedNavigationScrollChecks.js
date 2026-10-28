const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('scripts/merge_remote_projects.js', 'utf8');
const navigation = source.slice(source.indexOf('let releaseNavigation;'), source.indexOf('const observe ='));
const frames = new Map(), timers = new Map(), listeners = new Map();
let serial = 0, mutation, cancelCount = 0, navigations = 0;
const sidebar = {
  scrollTop: 450,
  addEventListener(type, handler) { listeners.set(type, handler); },
  removeEventListener(type, handler) { if (listeners.get(type) === handler) listeners.delete(type); }
};
const context = vm.createContext({
  window: {dispatchEvent(event) { assert.equal(event.type, 'codex-sidebar-thread-reveal-cancel'); cancelCount++; }},
  Event: class {constructor(type) {this.type = type;}},
  MutationObserver: class {constructor(callback) {mutation = this;this.callback=callback;} observe() {} disconnect() {this.disconnected=true;}},
  requestAnimationFrame(callback) {frames.set(++serial, callback);return serial;},
  cancelAnimationFrame(id) {frames.delete(id);},
  setTimeout(callback) {timers.set(++serial, callback);return serial;},
  clearTimeout(id) {timers.delete(id);},
  nativeCatalog: {navigate(id, host, project) {assert.equal(id,'chat');assert.equal(host,'remote');assert.equal(project,'project');navigations++;}},
});
vm.runInContext(`${navigation}\nthis.navigate = navigateFromGroup; this.release = () => releaseNavigation?.();`, context);
const row = {closest: () => sidebar}, session = {conversationId:'chat',hostId:'remote'};
function flushFrame() {const [id,fn] = frames.entries().next().value;frames.delete(id);fn();}
context.navigate(session, 'project', row);
sidebar.scrollTop = 1900;
mutation.callback();
assert.equal(sidebar.scrollTop, 450, 'Delayed native reveal restores the grouped click position');
assert.equal(cancelCount, 1, 'Cancel the native reveal through its event');
flushFrame();flushFrame();
assert.equal(cancelCount, 3, 'Cancel newly installed route effects before painting');
for (const type of ['wheel','touchstart','pointerdown','keydown','click']) {
  listeners.get(type)();
  assert(mutation.disconnected, `${type} releases the observer`);
  assert.equal(frames.size,0);assert.equal(timers.size,0);assert.equal(listeners.size,0);
  context.navigate(session, 'project', row);
}
const previous = mutation;
context.navigate(session, 'project', row);
assert(previous.disconnected, 'A second grouped click releases the first guard');
timers.values().next().value();
assert.equal(listeners.size,0, 'Guard expires without further user input');
context.navigate(session, 'project', row);
context.release();
assert(mutation.disconnected, 'Uninstall releases navigation protection');
assert.equal(timers.size,0);assert.equal(frames.size,0);
context.navigate(session, 'project', {closest:()=>null});
assert(navigations > 0);
console.log('Grouped navigation scroll checks passed: delayed reveal, route effects, user input, consecutive clicks, expiry, uninstall.');
