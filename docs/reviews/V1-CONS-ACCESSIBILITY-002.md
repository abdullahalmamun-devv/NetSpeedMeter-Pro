# V1-CONS-ACCESS-NATIVE-002 — native accessibility observation plan and current evidence

**Owner:** Accessibility reviewer  
**Reviewer:** Project Director  
**Authority:** PRD §§6.2–6.4, 24–25; consumer closure request §§25–29 and 35–37; V1-CONS-ACCESSIBILITY-001  
**Dependency:** Destructive test safety gate. Do not install, uninstall, purge, update, or use real account data in this task.  
**Status:** Native consumer-surface validation pending. No accessibility approval or gate decision is claimed.

## Attempt and observed blocker

On 12 October 2026, the reviewer read the Computer Use skill, its Windows automation guidance, API and confirmation rules, inspected the application/installer startup source, and initialized its `@oai/sky` client. `sky.list_apps()` returned no running SpeedMeter window. A read-only source check found that `InstallerWindow` is constructed before any install/uninstall engine call in `src/SpeedMeter.Installer/Program.cs:48–57`; this made initial-window inspection and Cancel the only intended actions. The reviewer called `sky.launch_app` on the locally built `artifacts/release/1.0.0-rc.2/SpeedMeterPro-1.0.0-rc.2-Setup.exe`. The result was exactly `Computer Use app approval timed out`. A fresh `sky.list_windows()` query returned no SpeedMeter Setup window. The launch was not retried, and no installer button was invoked. The review has **zero direct keyboard, focus, Narrator, UI Automation, scaling, or high-contrast observations** of that window.

The main application was deliberately not launched: `src/SpeedMeter.App/App.xaml.cs:34–42` creates the default crash/run record, while `src/SpeedMeter.App/MainWindow.xaml.cs:62` creates `MonitorSession` whose `ApplicationContext.GetDefaultDataDirectory()` resolves the real account's `%LocalAppData%\SpeedMeterPro` (`src/SpeedMeter.Application/ApplicationContext.cs:36–42`). `--smoke` suppresses first run (`MainWindow.xaml.cs:199–207`) and still constructs that session. A normal or smoke launch would therefore fail the isolation requirement for this task. No app source, installer source, real settings, or real account data was changed.

## Safe test setup required

1. Run a pre-existing test-signed RC.2 package in a dedicated disposable Windows user profile or a harness that injects **all** test settings, database, crash/run-marker, log, staging, and single-instance paths. Do not reuse the current account's `%LocalAppData%\SpeedMeterPro`. Preserve the package hash/source identity in the test record. The existing `--smoke` mode is insufficient for first-run and data isolation.
2. Arrange a host/session where the accessibility automation helper can target the actual installer window without an approval timeout. If Windows or browser security asks for an exception, stop; do not bypass it. Confirm the active process, title, window handle, and test profile before input.
3. Use preconfigured standard Windows 100% and 150% display-scaling sessions and an increased-text/high-contrast session. The reviewer will observe current settings rather than changing OS security/privacy/accessibility settings through automation. Record OS build, work-area bounds, effective DPI/text scale, contrast mode, and screenshots. This Windows 10 build 19045 host cannot establish Windows 11 acceptance.
4. Keep the destructive safety gate closed. For setup/uninstall, inspect choices, navigation, labels, focus, scroll and Cancel only. For update, use a deterministic fake `IUpdateService` to publish status transitions without network/download/installer launch. For crash recovery, use a synthetic sanitized report in an isolated process; do not open the external issue page or save to a real-user path.

## Native observation matrix

| Surface | Safe steps | Evidence required |
|---|---|---|
| Setup initial window | Open test package, inspect per-user location, desktop/startup choices, Tab/Shift+Tab/Enter/Escape, resize, scroll, Cancel. Never press Install. | UIA names/roles/states and focused element per step; visible focus screenshot; action footer and long path visible/reachable at 150% DPI and increased text; no unexpected registration or data mutation. |
| Uninstall initial window | Open isolated `-Uninstall` window, confirm Keep Data default and retained location, Tab/Shift+Tab, select purge checkbox only if needed to inspect its label, then Cancel. Never press Uninstall or confirm purge. | UIA label/state/focus record; retained-path visibility and purge disclosure; Cancel leaves test installation unchanged. |
| First run | Present isolated first-run window, traverse reachability/update opt-ins, Theme, Units, Get Started without enabling requests. Close without persisting if the test harness permits. | Spoken Narrator names and choices; UIA `LabeledBy`/Name for Theme and Units; focus order, visible focus, scroll, footer reachability at small work area/150% scaling. |
| Settings update section | Navigate in isolated app/harness and publish fake Checking, Available, UpToDate, Offline, Verification Failed, Downloading, and Deferred transitions. Avoid real GitHub requests. | UIA names of settings and update buttons; Narrator live announcement for meaningful status changes; progress does not announce every percentage; focus remains predictable while controls enable/disable. |
| Crash recovery | Present a synthetic report, traverse View/Save/Report issue/Dismiss, open preview, inspect redacted selectable text, close. Do not save or open external browser. | Initial focus on View; UIA names/roles; scroll/reachability at scaled text; preview focus and return focus; no automatic upload. |

Narrator speech requires a real spoken-output observation; a UIA tree or source attribute alone does not prove an announcement. Record pass/fail for each individual step and any assistive technology/version used. Do not convert the 276/1/0 fixture result, general WPF smoke assertions, or silent installer drill into evidence for this matrix.

## Current decision and handoff

**NOT VERIFIED.** The existing source review in `docs/reviews/V1-CONS-ACCESSIBILITY-001.md` records responsive fixes to the five code findings, but this task has no native consumer-flow observation. The Computer Use approval timeout and lack of an isolated main-app profile are current blockers. Once the Director supplies a safe test environment and the helper can open the target window, execute the matrix above and append observed results before requesting accessibility approval. The Director retains integration and gate authority.
