// Async function body, evaluated in the controlled renderer. Read metadata only.
const bridge = window.electronBridge;
const bootstrap = bridge.getInitialSidebarBootstrap();
const identity = bootstrap.catalogEntries?.map(e => e.startupReadState)
  .find(e => typeof e?.accountId === 'string' && typeof e?.userId === 'string');
const cacheKey = Symbol.for('claudex-yourself.jump-list-history');
const scope = identity ? identity.accountId + ':' + identity.userId : null;
if (window[cacheKey]?.scope !== scope) window[cacheKey] = {scope, hosts:new Map(), chatgpt:[]};
const cache = window[cacheKey];
const errors = [];
const entries = [];
const hosts = [...new Set(['local', ...(bootstrap.catalogHostIds ?? [])])];
const requestHost = (hostId, method, params) => new Promise((resolve, reject) => {
  const id = 'claudex-jumplist-' + crypto.randomUUID();
  const finish = (error, value) => {
    clearTimeout(timer);
    window.removeEventListener('message', listener);
    error ? reject(error) : resolve(value);
  };
  const listener = event => {
    const envelope = event.data;
    if (envelope?.type !== 'mcp-response' || envelope.message?.id !== id) return;
    finish(envelope.message.error && new Error(envelope.message.error.message), envelope.message.result);
  };
  const timer = setTimeout(() => finish(new Error('History request timed out')), 6000);
  window.addEventListener('message', listener);
  bridge.sendMessageFromView({type:'mcp-request', hostId, request:{id, method, params},
    priority:'background', source:'claudex-jumplist', timeoutMs:6000, expiresAtMs:Date.now()+6000})
    .catch(error => finish(error));
});
// Query each host afresh; bootstrap titles/timestamps are only a startup snapshot.
const pending = [...hosts];
await Promise.all(Array.from({length:Math.min(3, pending.length)}, async () => {
  while (pending.length) {
    const hostId = pending.shift();
    try {
      const result = await requestHost(hostId, 'thread/list', {
        limit:50, sortKey:'updated_at', archived:false, useStateDbOnly:true
      });
      if (!Array.isArray(result.data)) throw new Error('Unsupported thread/list response');
      const current = [];
      for (const thread of result.data) {
        if (thread.ephemeral || thread.source?.subAgent) continue;
        current.push({kind:'codex', id:thread.id, hostId, title:thread.name ?? thread.title ?? thread.preview,
          updatedAt:thread.updatedAt ?? thread.createdAt});
      }
      cache.hosts.set(hostId, current);
      entries.push(...current);
    } catch (error) {
      const previous = cache.hosts.get(hostId);
      errors.push(hostId + ': ' + error.message + (previous ? ' (using last successful history)' : ' (using startup catalog metadata)'));
      if (previous) entries.push(...previous);
      else for (const thread of bootstrap.catalogEntries ?? []) {
          if (thread.hostId !== hostId || thread.sourceKind === 'chatgpt' || thread.sourceKind === 'subAgent') continue;
          entries.push({kind:'codex', id:thread.threadId, hostId, title:thread.displayTitle,
            updatedAt:thread.sourceUpdatedAt ?? thread.sourceCreatedAt});
      }
    }
  }
}));
try {
  if (!identity) throw new Error('Current account identity is unavailable');
  // Resolve the API binding from the native history call, without hardcoded
  // hashed filenames or minified aliases. Resolve once per build and account.
  const root = [...document.scripts].map(e => e.src).find(e => e.startsWith('app://-/assets/index-'));
  if (!root) throw new Error('Codex application module is unavailable');
  if (cache.apiRoot !== root) {
    const source = await (await fetch(root, {signal:AbortSignal.timeout(6000)})).text();
    const dependency = source.match(/["'`]([^"'`]*app-shared-[\w-]+\.js)["'`]/)?.[1];
    if (!dependency) throw new Error('Codex shared module is unavailable');
    const initialPath = source.match(/["'`]([^"'`]*app-initial-[\w-]+\.js)["'`]/)?.[1];
    if (!initialPath) throw new Error('Codex history module is unavailable');
    const initial = await (await fetch(new URL(initialPath, root), {signal:AbortSignal.timeout(6000)})).text();
    const localName = initial.match(/await ([\w$]+)\.safeGet\([`'"]\/conversations[`'"]/)?.[1];
    const sharedImport = [...initial.matchAll(/import\s*\{([^}]+)\}\s*from\s*[`'"]([^`'"]+)[`'"]/g)]
      .find(match => new URL(match[2], root).href === new URL(dependency, root).href);
    const binding = sharedImport?.[1].split(',').map(e => e.trim().split(/\s+as\s+/)).find(e => e[1] === localName);
    if (!binding) throw new Error('Codex history API binding is unavailable');
    cache.apiModule = new URL(dependency, root).href;
    cache.apiExport = binding[0];
    cache.apiRoot = root;
  }
  const shared = await import(cache.apiModule);
  const api = shared[cache.apiExport];
  if (!api || typeof api.safeGet !== 'function') throw new Error('Codex history API is unavailable');
  const result = await api.safeGet('/conversations', {
    parameters:{query:{limit:50, offset:0, order:'updated', is_archived:false}},
    expectedIdentity:{accountId:identity.accountId, userId:identity.userId},
    signal:AbortSignal.timeout(6000), retry:false
  });
  if (!Array.isArray(result.items)) throw new Error('Unsupported ChatGPT history response');
  const current = [];
  for (const chat of result.items) {
    if (chat.is_archived || chat.is_temporary) continue;
    current.push({kind:'chatgpt', id:chat.id, accountId:identity.accountId,
      projectId:chat.gizmo_id ?? null, title:chat.title, updatedAt:chat.update_time ?? chat.create_time});
  }
  cache.chatgpt = current;
  entries.push(...current);
} catch (error) {
  errors.push('ChatGPT: ' + error.message + (cache.chatgpt.length ? ' (using last successful history)' : ''));
  entries.push(...cache.chatgpt);
}
return {entries, errors};
