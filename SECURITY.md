# Trust and local access

Userscripts run inside your signed-in Codex renderer with access to its page, storage, and exposed bridge. Read scripts before running them or enabling autoload. `@grant` documents intent; it is not an enforced permission boundary. Uninstalling a script cannot necessarily undo actions it already performed.

The DevTools endpoint on loopback port `9229` has no added authentication. The launcher requests `127.0.0.1`; the controller also probes IPv6 loopback (`::1`) because Chromium can bind there. Keep it local: processes that can reach it can potentially control the renderer. MCP execution and input tools act on the same live session.

Renderer captures and diagnostic output can contain conversation text, account details, and local paths. Review them before sharing.

Exact-build testing records compatibility, not a security review. For package validation and restart behavior, see [Windows updates](docs/windows-updates.md).

URL sources use the same renderer authority as local scripts. Version checks download data only. They run on startup by default and can repeat while controlled Codex is open, including with Settings closed; choose **Manually** globally or per script to disable future automatic requests. A feature toggle or disabled autoload entry does not stop version checks. Installing from a URL requires an explicit reviewed preview and can execute code in every open main/session renderer when requested. An HTTP source is not transport-authenticated; prefer HTTPS. SHA-256 pins a preview's bytes, not the author's identity or trustworthiness. The installer rejects unsupported platforms, ID changes during updates, expired previews, and concurrent local edits, but metadata and `@grant` do not enforce runtime permissions. Previous sources are retained in `source-backups`; restoring a file cannot undo executed code.

The URL downloader sends no browser cookies or account credentials, limits responses to 1 MiB and 20 seconds, validates redirect schemes, and rejects HTTPS-to-HTTP redirects. Local-network HTTP(S) file servers are intentionally supported. The settings worker uses the existing local DevTools channel without opening another listening port.

Source state remains in the Claudex state directory. `sources.json` and `source-checks.json` can contain full URLs (including query strings); previews and backups contain complete script source. Treat these files as potentially sensitive when sharing diagnostics. Installation pins a preview for 15 minutes and verifies that the installed file has not changed since review. Report publication separately from activation: a successfully saved file can still fail to run in one or more windows.

Claudex release checks offer a download/release link and never replace the controller automatically. Userscript schedules never install or execute downloaded code. The Windows package companion is a separate explicit install-and-restart operation with the package validation described in its guide.
