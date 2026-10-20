const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../scripts/userscript_settings.js'), 'utf8');
const fragment = source.slice(source.indexOf('const catalog ='), source.indexOf('const style ='));
async function main() {
for (const platform of ['Linux', 'Windows', 'macOS']) {
 const registry = new Map();
 const controller = installed => ({installed,install(){this.installed=true},uninstall(){this.installed=false}});
 registry.set('sidebar_usage', controller(true));
 registry.set('hide_pets_button', controller(false));
 registry.set('codex_updates', controller(true));
 registry.set('custom-script', controller(true));
 let saved = {};
 const context = vm.createContext({navigator:{platform},window:{[Symbol.for('claudex-yourself.userscript-registry')]:registry},registryKey:Symbol.for('claudex-yourself.userscript-registry'),localStorage:{getItem:()=>null,setItem(key,value){saved=JSON.parse(value)}},renderSources(){},preferencesKey:'preferences'});
 vm.runInContext(fragment,context);
 const api = vm.runInContext('({getCatalog,scriptState,isEnabled,setEnabled})', context);
 assert.equal(api.isEnabled('sidebar_usage'),true);
 assert.equal(api.isEnabled('hide_pets_button'),false);
 assert.equal(api.isEnabled('hide_invite_a_friend'),false,'Missing scripts are not enabled by default');
 assert.equal(api.isEnabled('custom-script'),true,'Registered custom scripts are included');
 await api.setEnabled('custom-script',false);
 assert.equal(registry.get('custom-script').installed,false,'Controller methods retain their receiver when disabling');
 await api.setEnabled('custom-script',true);
 assert.equal(registry.get('custom-script').installed,true,'Controller methods retain their receiver when enabling');
 let finish;
 registry.set('async-script', {installed:false,install(){return new Promise(resolve=>{finish=()=>{this.installed=true;resolve()}})},uninstall(){this.installed=false}});
 const enabling = api.setEnabled('async-script',true);
 assert.equal(saved['async-script'],undefined,'Do not persist the temporary disabled state during async installation');
 finish(); await enabling;
 assert.equal(saved['async-script'],true,'Persist enabled only after async install finishes');
 registry.set('failing-script',{installed:false,async install(){throw new Error('adapter unavailable')},uninstall(){}});
 await assert.rejects(api.setEnabled('failing-script',true),/adapter unavailable/);
 assert.equal(saved['failing-script'],undefined,'Failed installation does not overwrite preferences');
 const update = api.scriptState(api.getCatalog().find(x=>x.id==='codex_updates'));
 assert.equal(update.available,platform==='Windows','Updater is Windows only even if accidentally registered elsewhere');
 assert.equal(update.enabled,platform==='Windows');
}
console.log('Settings state checks passed: platform support, unloaded scripts, actual enabled state, and custom scripts.');

}
main().catch(error=>{console.error(error);process.exitCode=1});
