const fs = require("node:fs");
const vm = require("node:vm");
const assert = require("node:assert/strict");
const window = { dispatchEvent() {} };
const context = vm.createContext({ window, Symbol, Map, Set, console, clearTimeout,
  CustomEvent: class {}, localStorage: { getItem: () => '{"merge_remote_projects":false}' } });
vm.runInContext(`(async function(){${fs.readFileSync("scripts/merge_remote_projects.js", "utf8")}\n})()`, context);
const { repo, folder, match } = window[Symbol.for("claudex-yourself.merge-remote-projects")].matching;
const project = { projectId: "one", label: "Deskflow", rootPaths: ["C:\\dev\\deskflow"],
  gitRepos: [{ ownerRepo: { owner: "batram", repoName: "deskflow" } }] };
assert.equal(repo("git@github.com:batram/deskflow.git"), "batram/deskflow");
assert.equal(repo("ssh://git@github.com/batram/deskflow.git"), "batram/deskflow");
assert.equal(repo("https://github.com/batram/deskflow.git?ref=main"), "batram/deskflow");
assert.equal(folder("C:\\dev\\Deskflow\\"), "deskflow");
assert.equal(match({ label: " deskflow " }, [project]), project);
assert.equal(match({ cwd: "/Users/me/deskflow/" }, [project]), project);
assert.equal(match({ label: "renamed", gitInfo: { originUrl: "git@github.com:batram/deskflow.git" } }, [project]), project);
assert.equal(match({ label: "deskflow", gitInfo: { originUrl: "https://github.com/other/deskflow.git" } }, [project]), null);
assert.equal(match({ label: "deskflow" }, [project, { ...project, projectId: "two" }]), null);
assert.equal(match({ label: "deskflow", gitInfo: { originUrl: "https://github.com/batram/deskflow" } },
  [{ projectId: "name-only", label: "deskflow" }, project]), project);
assert.equal(match({ label: "unrelated", cwd: "/tmp/elsewhere" }, [project]), null);
assert.equal(match({ cwd: "/Users/me/other" }, [{ ...project, rootPaths: ["C:/one", "C:/other"] }]).projectId, "one");
console.log("Merge remote projects: 12 matching checks passed.");
// Minified names can be reused for unrelated exports after an app update.
const source = fs.readFileSync("scripts/merge_remote_projects.js", "utf8");
const exportResolver = source.slice(source.indexOf('  const exports = source.slice'), source.indexOf('  // These private symbols'));
const adapter = source.slice(source.indexOf('  const legacyKeys ='), source.indexOf('  nativeCatalog ='));
for (const mode of ["legacy", "windows", "20770", "26.1016", "26.1021", "26.1023"]) {
  const latest = mode === "26.1023";
  const legacy = mode === "legacy", windows = mode === "windows", updated = mode === "26.1021", current = mode === "26.1016" || updated || latest;
  const atom = { scope: {}, resolve() {} };
  const main = { oldKeys: legacy ? atom : () => {}, oldList() {}, newKeys: atom, newList() {}, conversation: atom, router: atom, location: atom };
  if (windows) { main.windowsKeys = atom; main.windowsList = function(){return {conversationId:"test",workspaceKind:"project"}}; }
  if (current) { main.currentKeys = atom; main.currentList = function(e,t){return t.flatMap(t=>e().byKey.has(t)?[{conversationId:t,workspaceKind:"project"}]:[])}; }
  const exports = latest ? 'export{sDi as currentList,cq as currentKeys,wj as conversation,pA as router,Kg as location}' : updated ? 'export{fMi as currentList,tK as currentKeys,hj as conversation,Dk as router,Ak as location}' : current ? 'export{BXi as currentList,OZi as currentKeys,Fcr as conversation,CD as router,HE as location}' :
    (windows ? "export{EF as windowsKeys,_F as windowsList," : "export{") + 'Gtr as oldKeys,yF as oldList,$Ki as newKeys,qB as newList,ek as conversation,eh as router,ch as location}';
  const picked = vm.runInNewContext(`(()=>{${exportResolver}${adapter}return {keys,list,router,location}})()`, {
    source:exports, main, imported: () => atom
  });
  assert.equal(picked.keys, current ? main.currentKeys : windows ? main.windowsKeys : legacy ? main.oldKeys : main.newKeys);
  assert.equal(picked.list, current ? main.currentList : windows ? main.windowsList : legacy ? main.oldList : main.newList);
  assert.equal(picked.router, atom);
}
console.log("Legacy and build 20770 catalog adapters passed, including dollar-sign exports.");
async function checkSidebarReadiness() {
  const readiness = source.slice(source.indexOf('  // App upgrades can reach'), source.indexOf('  const url ='));
  const chain = new Map([['scope',{store:{},queryClient:{}}]]);
  const row = {__reactFiber$test:{dependencies:{firstContext:{memoizedValue:chain}}}};
  for (const available of [true,false]) {
    let polls = 0, waits = 0;
    const result = vm.runInNewContext(`(async()=>{${readiness}return chain})()`, {
      document:{querySelector(){polls++;return available && polls > 4 ? row : null}},
      threadSelector:'thread',projectSelector:'project',Map,observer:{},
      setTimeout(resolve){waits++;resolve()}
    });
    if (available) { assert.equal(await result,chain); assert(waits>0,'Wait for the late-mounted sidebar'); }
    else { await assert.rejects(result,/within 10 seconds/); assert.equal(waits,100,'Stop retrying when the sidebar remains unavailable'); }
  }
  console.log('Sidebar readiness checks passed: delayed mount and bounded failure.');
}
checkSidebarReadiness().catch(error=>{console.error(error);process.exitCode=1});
