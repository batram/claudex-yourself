# Windows compatibility audit: 2026-10-21

Host: Windows x64. Codex: **26.1016.6573.0**. Claudex: **0.3.1**.

The following maintained scripts were behaviorally checked in this exact build.
Their canonical and installed headers record it in `@codex-tested`; patch versions
were incremented so source updates can deliver the compatibility metadata.

| Script | Version | Observed behavior |
| --- | --- | --- |
| codex_updates | 1.5.2 | Live source check and panel status; 21 synthetic UI scenarios in a disposable iframe, including pending actions, progress, failures, blocked downloads and completion. |
| hide_invite_a_friend | 1.1.2 | Actual profile-menu entry hides, restores on uninstall, and hides again on install. |
| hide_pets_button | 1.2.2 | Actual Show Pet entry with keyboard shortcut hides and restores correctly. |
| merge_remote_projects | 1.4.6 | 40 remote chats grouped under three projects; native styling, pagination, copied/catalog-row navigation and scroll preservation pass. |
| remote_device_icons | 1.0.1 | Five presets, normalized PNG uploads, invalid-file feedback, uninstall/reinstall, and native/grouped rows pass; previous choices restored. |
| sidebar_usage | 1.4.10 | Live refresh and usage popover show current weekly allowance; visibility, retry and navigation regressions pass. |
| userscript_settings | 1.4.5 | Native Settings entry, connected source cards, device controls, draft/focus/disclosure preservation and non-overflowing cards verified. State and scroller regressions pass. |

## Controller and regression checks

- Native Windows publish, including explicit `win-x64`, uses `bin/`.
- Verified MCP reload replaced the old processes and confirmed the published build
  ID; MCP reported version 0.3.1 and 28 tools after reconnecting.
- Full C# self-test passed, including source downloads, scheduling, compatibility,
  updater logic, reload identity, taskbar, tray readiness and recent-chat checks.
  The printed source-check HTTP 503 is an intentional mocked failure.
- Live MCP protocol and source smoke checks passed.
- Captured launch returned while background workers remained independent.
- Fresh autoload status reported completion for all seven enabled scripts.
- JavaScript matching/startup, usage, device-icon, settings and recent-chat checks passed.
  Recent-chat tests require `node --experimental-vm-modules`.
- Windows package tests passed five mocked deployment scenarios; elevation tests
  passed seven mocked UAC/deployment scenarios. The latter harness was corrected
  to inspect expected native stderr instead of stopping before its assertions.

## Failures and limits

- The disabled, per-user-only `notification_router` v1.2.2 references the missing
  `app-shared-c568b0b98683.js` asset. Fetching that dependency fails on this build.
  It remains disabled and unverified; it is not a bundled or published script.
- The merge live test initially ran while Settings was active, preventing sidebar
  focus. It passed after returning to the chat view. Navigation tests also require
  a current chat with a sidebar entry.
- The default autoload-window test requires an additional session window, which
  was not open. Main-window coverage is available through `--main-only`;
  multi-window behavior was not certified by this audit.
- No real package deployment, UAC prompt, application quit/restart, or vanilla-mode
  switch was performed. Those paths have regression coverage, not fresh end-to-end
  installation evidence. The separate dgSpy reload helper was not exercised.
- Native macOS/Linux execution and Windows ARM64 were not tested here. Existing
  platform-specific compatibility records are preserved, not extended.
