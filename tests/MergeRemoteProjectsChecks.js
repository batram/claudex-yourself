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
