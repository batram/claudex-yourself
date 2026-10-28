// Checks the shared layout contract in the real renderer, including the
// working + unread combination and focus controls that previously collided.
const assert = require('node:assert/strict');
const { evaluate } = require('./AutoloadWindowChecks.js');
async function checkLayout() {
  const check = (value, message) => { if (!value) throw new Error(message); };
  const icons = window[Symbol.for('claudex-yourself.remote-device-icons')];
  const merge = window[Symbol.for('claudex-yourself.merge-remote-projects')];
  const scroll = document.querySelector('[data-app-action-sidebar-scroll]');
  const top = scroll.scrollTop, focus = document.activeElement;
  const native = [...document.querySelectorAll('[data-app-action-sidebar-thread-row]')]
    .find(row => row.querySelector('[data-claudex-device-part=status] [role=status]'));
  check(native, 'A working remote native row is available');
  const status = native.querySelector('[data-claudex-device-part=status]');
  const unread = document.createElement('span');
  unread.style.cssText = 'display:block;flex:none;width:8px;height:8px;background:#438cff;border-radius:50%';
  unread.setAttribute('aria-label', 'Unread layout test');
  const changed = [];
  let checks = 0;
  try {
    status.append(unread);
    merge.refresh();
    const rows = [...document.querySelectorAll('[data-app-action-sidebar-thread-row],[data-claudex-merged-remote=row]')]
      .filter(row => row.querySelector('[data-claudex-device-slot],.claudex-merged-device-slot') && row.getBoundingClientRect().width);
    for (const row of rows) {
      changed.push([row, row.style.width]);
      for (const width of [220, 420]) {
        row.style.width = `${width}px`;
        const icon = row.querySelector('[data-claudex-device-slot],.claudex-merged-device-slot');
        for (const focused of [false, true]) {
          if (focused) row.focus({preventScroll:true}); else row.blur();
          const device = icon.getBoundingClientRect(), box = row.getBoundingClientRect();
          check(Math.abs(device.top + device.height / 2 - box.top - box.height / 2) < 1, 'Icon stays centered');
          for (const node of row.querySelectorAll('[data-claudex-device-part=status],.claudex-merged-row-status,button')) {
            const other = node.getBoundingClientRect();
            if (!other.width || !other.height) continue;
            check(device.right <= other.left + 1 || device.left >= other.right - 1, 'Icon never overlaps status or focus controls');
          }
          checks++;
        }
      }
      row.style.width = changed.at(-1)[1];
    }
    // Suppress the real action at its source while proving normalized clones
    // still dispatch to the exact original button, independent of DOM offsets.
    const copy = [...document.querySelectorAll('[data-claudex-merged-remote=row]')]
      .find(row => row.dataset.claudexSessionId === native.dataset.appActionSidebarThreadId.split(':').at(-1));
    check(copy, 'The working row has a grouped copy');
    const button = copy.querySelector('button[aria-label="Pin chat"],button[aria-label="Unpin chat"]');
    const original = native.querySelector(`button[aria-label="${button?.getAttribute('aria-label')}"]`);
    check(original && button, 'Source and copied pin controls exist');
    let forwarded = 0;
    const intercept = event => { forwarded++; event.preventDefault(); event.stopImmediatePropagation(); };
    original.addEventListener('click', intercept, true);
    try { button.click(); check(forwarded === 1, 'Copied control reaches its exact original once'); }
    finally { original.removeEventListener('click', intercept, true); }
    icons.uninstall(); merge.refresh(); icons.install(); merge.refresh();
    check(!document.querySelector('.claudex-merged-row-layout [data-claudex-device-part]'), 'Grouped copies never retain native layout markers');
    return {rows:rows.length, geometryChecks:checks, widths:[220,420], workingAndUnread:true, focusControls:true, exactEventForwarding:true, reinstall:true};
  } finally {
    unread.remove();
    changed.forEach(([row,width]) => {row.style.width = width;});
    merge.refresh();
    if (focus?.isConnected) focus.focus({preventScroll:true});
    scroll.scrollTop = top;
  }
}
async function main() {
  const targets = await (await fetch('http://127.0.0.1:9229/json/list')).json();
  const target = targets.find(target => target.url === 'app://-/index.html');
  assert(target, 'Controlled main renderer is available');
  console.log('Remote icon layout live checks passed:', JSON.stringify(await evaluate(target, `(${checkLayout.toString()})()`)));
}
main().catch(error => {console.error(error);process.exitCode=1;});
