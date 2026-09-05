// Browser regression checks. Supply runInFrame(window, document, localStorage), whose
// function body is the canonical userscript (inline it rather than using iframe eval).
// All UI and synthetic statuses stay in a disposable iframe, away from the live updater.
const frame = document.createElement('iframe');
frame.hidden = true;
document.body.append(frame);
const assertions = [];
const assert = (condition, message) => { if (!condition) throw Error(message); assertions.push(message); };
try {
  const realm = frame.contentWindow;
  await runInFrame(realm, realm.document, realm.localStorage);
  const controller = realm[Symbol.for('claudex-yourself.codex-updates')];
  controller.install();
  const shadow = realm.document.getElementById('claudex-codex-updates').shadowRoot;
  const button = shadow.getElementById('install');
  const checkButton = shadow.getElementById('check');
  const initial = { check: { updateAvailable: true, availableVersion: '2.0.0.0', installed: { version: '1.0.0.0' } }, operation: { state: 'idle', updatedAtUtc: '2000-01-01T00:00:00Z' } };
  controller.update(initial);
  button.click();
  assert(button.disabled && shadow.getElementById('details').textContent.includes('Starting'), 'click gives immediate feedback');
  controller.update(initial);
  assert(button.disabled, 'stale poll cannot re-enable installation');
  assert(controller.takeAction() === 'install' && controller.takeAction() === 'none', 'one click delivers one action');
  button.click();
  assert(controller.takeAction() === 'none', 'duplicate click is ignored while pending');
  const now = new Date(Date.now() + 1000).toISOString();
  controller.update({ ...initial, operation: { state: 'downloading', message: 'Downloading: 25%.', updatedAtUtc: now } });
  assert(button.disabled && shadow.getElementById('details').textContent.includes('25%'), 'worker acknowledgement displays real progress');
  controller.update({ ...initial, operation: { state: 'failed', message: 'Test failure', updatedAtUtc: now } });
  assert(!button.disabled && shadow.getElementById('details').textContent === 'Test failure', 'failure enables retry and remains visible');
  const complete = { check: { updateAvailable: false, installed: { version: '2.0.0.0' } }, operation: { state: 'completed', message: 'Controlled restart verified.', updatedAtUtc: now } };
  controller.update(complete);
  assert(button.hidden && shadow.getElementById('badge').textContent === 'Updated', 'completion is visible after restart');
  checkButton.click();
  controller.update(complete);
  assert(checkButton.disabled, 'check remains pending through an old status poll');
  controller.takeAction();
  controller.update({ ...complete, checkFinishedAtUtc: now, checkRunning: false });
  assert(!checkButton.disabled, 'check completes after its response');
  controller.uninstall();
  assert(!realm.document.getElementById('claudex-codex-updates'), 'uninstall removes the panel');
  return { passed: assertions.length, assertions };
} finally {
  frame.contentWindow[Symbol.for('claudex-yourself.codex-updates')]?.uninstall();
  frame.remove();
}
