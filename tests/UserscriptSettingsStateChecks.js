const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../scripts/userscript_settings.js'), 'utf8');
const fragment = source.slice(source.indexOf('const catalog ='), source.indexOf('const style ='));
for (const platform of ['Linux', 'Windows', 'macOS']) {
 const registry = new Map();
 const controller = installed => ({installed,install(){this.installed=true},uninstall(){this.installed=false}});
 registry.set('sidebar_usage', controller(true));
 registry.set('hide_pets_button', controller(false));
 registry.set('codex_updates', controller(true));
 registry.set('custom-script', controller(true));
 const context = vm.createContext({navigator:{platform},window:{[Symbol.for('claudex-yourself.userscript-registry')]:registry},registryKey:Symbol.for('claudex-yourself.userscript-registry'),localStorage:{getItem:()=>null,setItem(){}},renderSources(){},preferencesKey:'preferences'});
 vm.runInContext(fragment,context);
 const api = vm.runInContext('({getCatalog,scriptState,isEnabled,setEnabled})', context);
 assert.equal(api.isEnabled('sidebar_usage'),true);
 assert.equal(api.isEnabled('hide_pets_button'),false);
 assert.equal(api.isEnabled('hide_invite_a_friend'),false,'Missing scripts are not enabled by default');
 assert.equal(api.isEnabled('custom-script'),true,'Registered custom scripts are included');
 api.setEnabled('custom-script',false);
 assert.equal(registry.get('custom-script').installed,false,'Controller methods retain their receiver when disabling');
 api.setEnabled('custom-script',true);
 assert.equal(registry.get('custom-script').installed,true,'Controller methods retain their receiver when enabling');
 const update = api.scriptState(api.getCatalog().find(x=>x.id==='codex_updates'));
 assert.equal(update.available,platform==='Windows','Updater is Windows only even if accidentally registered elsewhere');
 assert.equal(update.enabled,platform==='Windows');
}
console.log('Settings state checks passed: platform support, unloaded scripts, actual enabled state, and custom scripts.');
