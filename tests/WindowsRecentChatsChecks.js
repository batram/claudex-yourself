// Run with: node --experimental-vm-modules tests/WindowsRecentChatsChecks.js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const {randomUUID} = require('node:crypto');
const source = fs.readFileSync(require('node:path').join(__dirname, '../update/WindowsRecentChats.js'), 'utf8');
const listeners = new Set();
let accountId = 'account-one';
let unavailable = false;
let archived = false;
const local = {id:'local-thread',name:'Local latest',updatedAt:100};
const remote = {id:'remote-thread',name:'Remote latest',updatedAt:200};
const bootstrap = () => ({catalogHostIds:['local','remote-control:test'],catalogEntries:[
  {hostId:'local',threadId:local.id,sourceKind:'vscode',startupReadState:{accountId,userId:'user'}},
  {hostId:'remote-control:test',threadId:remote.id,displayTitle:'Stale startup title',sourceKind:'vscode',sourceUpdatedAt:50}
]});
const window = {
  addEventListener(type, listener) { listeners.add(listener); },
  removeEventListener(type, listener) { listeners.delete(listener); },
  electronBridge:{
    getInitialSidebarBootstrap:bootstrap,
    async sendMessageFromView(message) {
      assert.equal(message.request.method, 'thread/list');
      assert.equal(message.request.params.sortKey, 'updated_at');
      assert.equal(message.request.params.archived, false);
      const error = unavailable && message.hostId !== 'local' ? {message:'offline'} : null;
      const result = {data:archived ? [] : [message.hostId === 'local' ? local : remote,
        {id:'temporary',ephemeral:true,updatedAt:999}, {id:'subagent',source:{subAgent:{}},updatedAt:999}]};
      queueMicrotask(() => [...listeners].forEach(listener => listener({data:{type:'mcp-response',message:{id:message.request.id,error,result}}})));
    }
  }
};
const context = vm.createContext({window,crypto:{randomUUID},URL,AbortSignal,setTimeout,clearTimeout,
  document:{scripts:[{src:'app://-/assets/index-fixture.js'}]},
  fetch:async url => ({text:async () => String(url).includes('index-fixture')
    ? "import './app-shared-fixture.js'; import './app-initial-fixture.js';"
    : "import {historyApi as Ae} from './app-shared-fixture.js'; async function history(){return await Ae.safeGet(`/conversations`);}"})
});
async function read() {
  const expectedAccount = accountId;
  const moduleSource = `export const historyApi = {async safeGet(path, options) {
    if (path !== '/conversations' || options.expectedIdentity.accountId !== ${JSON.stringify(expectedAccount)} || options.parameters.query.order !== 'updated') throw Error('Invalid scoped history request');
    ${unavailable ? "throw Error('offline');" : `return {items:${JSON.stringify(archived ? [] : [{id:'chatgpt-thread',title:'ChatGPT',update_time:'2026-10-10T12:00:00Z'}])}};`}
  }};`;
  return new vm.Script('(async()=>{'+source+'})()', {
    importModuleDynamically:async url => {
      assert.equal(url, 'app://-/assets/app-shared-fixture.js');
      return import('data:text/javascript;base64,' + Buffer.from(moduleSource).toString('base64'));
    }
  }).runInContext(context);
}
(async () => {
  let result = await read();
  assert.equal(result.errors.length, 0);
  assert.deepEqual(Array.from(result.entries, e=>e.title).sort(), ['ChatGPT','Local latest','Remote latest']);
  assert.equal(listeners.size, 0, 'Completed history requests must release their listeners');
  unavailable = true;
  result = await read();
  assert.equal(result.entries.find(e=>e.hostId==='remote-control:test').title, 'Remote latest', 'An offline host must retain its freshest successful metadata');
  assert.equal(result.entries.filter(e=>e.kind==='chatgpt').length, 1, 'A transient ChatGPT failure must retain its scoped history');
  assert.equal(result.errors.length, 2, 'Cached histories must report freshness gaps');
  accountId = 'account-two';
  result = await read();
  assert.equal(result.entries.filter(e=>e.kind==='chatgpt').length, 0, 'Changing accounts must discard the old account cache');
  assert.equal(result.entries.find(e=>e.hostId==='remote-control:test').title, 'Stale startup title');
  unavailable = false;
  archived = true;
  result = await read();
  assert.equal(result.entries.length, 0, 'A successful empty history must clear archived/deleted cached entries');
  assert.equal(listeners.size, 0);
  console.log('Windows recent-chat adapter checks passed: live sources, host/account scoping, partial failures, cache invalidation, and request cleanup.');
})().catch(error => {console.error(error);process.exitCode=1;});
