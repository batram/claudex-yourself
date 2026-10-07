// Regression: an overflow-hidden main precedes the real scrolling descendant in DOM order.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname,'../scripts/userscript_settings.js'),'utf8');
const fragment = source.slice(source.indexOf('const findSettingsScroller ='),source.indexOf('const openPanel ='));
function element(overflow, heading = true) {
 return {overflow,children:[],height:500,
  getBoundingClientRect(){return {height:this.height}},
  querySelector(){return heading ? {} : null},
  contains(other){return this===other || this.children.some(child=>child.contains(other))}
 };
}
const main=element('hidden'),scroller=element('auto'),nav=element('auto',false),hidden=element('auto');
main.children=[scroller];hidden.height=0;
nav.parentElement={querySelectorAll(){return [nav,hidden,main,scroller]},parentElement:null};
const context=vm.createContext({getComputedStyle:e=>({overflowY:e.overflow})});
vm.runInContext(fragment,context);
const find=vm.runInContext('findSettingsScroller',context);
assert.equal(find(nav),scroller,'Choose the native scrolling child, never the clipping main');
scroller.overflow='hidden';
assert.equal(find(nav),main,'Use the main for an explicit scrolling wrapper when no native scroller exists');
scroller.overflow='scroll';main.overflow='auto';
assert.equal(find(nav),scroller,'Use the innermost content scroller when both ancestors can scroll');
console.log('Settings scroll checks passed: nested clipping, hidden/sidebar exclusion, fallback, and innermost scroll container.');
