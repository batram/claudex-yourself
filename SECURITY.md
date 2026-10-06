# Trust and local access

Userscripts run inside your signed-in Codex renderer with access to its page, storage, and exposed bridge. Read scripts before running them or enabling autoload. `@grant` documents intent; it is not an enforced permission boundary. Uninstalling a script cannot necessarily undo actions it already performed.

The DevTools endpoint on loopback port `9229` has no added authentication. The launcher requests `127.0.0.1`; the controller also probes IPv6 loopback (`::1`) because Chromium can bind there. Keep it local: processes that can reach it can potentially control the renderer. MCP execution and input tools act on the same live session.

Renderer captures and diagnostic output can contain conversation text, account details, and local paths. Review them before sharing.

Exact-build testing records compatibility, not a security review. For package validation and restart behavior, see [Windows updates](docs/windows-updates.md).
