// Exercises synthetic update states in a disposable iframe, never the real installer.
const fs = require('node:fs');
const assert = require('node:assert/strict');
const {evaluate} = require('./AutoloadWindowChecks.js');
(async () => {
  const targets = await (await fetch('http://127.0.0.1:9229/json/list')).json();
  const target = targets.find(t => t.url === 'app://-/index.html');
  assert(target, 'Controlled main renderer is available');
  const source = fs.readFileSync('scripts/codex_updates.js', 'utf8');
  const checks = fs.readFileSync('tests/UpdatePanelChecks.js', 'utf8');
  const result = await evaluate(target, `(async()=>{const runInFrame=async(window,document,localStorage)=>{${source}\n};${checks}\n})()`);
  console.log('Update panel live checks passed:', result.passed);
})().catch(error => {console.error(error);process.exitCode=1;});
