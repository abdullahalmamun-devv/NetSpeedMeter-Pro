# V1-CONS-ACCESSIBILITY-003 — RC.4 native consumer accessibility review

**Owner:** Independent Accessibility specialist
**Reviewer:** Project Director
**Authority:** PRD §6.2–6.4, §25.1–25.2, US-12, AC-W08; final pre-Gate-E2 native closure §§29–34
**Dependency:** Final RC.4 clean-source package and verified push before current-profile native testing. The initial package from `63bd9d1645950416b97964863ce3df8dad47a94c` was superseded by installer cleanup changes.
**Scope:** Packaged Setup, uninstall, first run, Settings update controls, crash recovery, and diagnostic preview.
**Decision:** ACCESSIBILITY CONSUMER SURFACES NOT VERIFIED.

## Method and candidate

This is a native observation review. Source attributes, fixture results, and silent installer results are recorded separately and do not establish Narrator speech, visual focus, or display scaling behavior. Windows UI control uses the Computer Use skill and `@oai/sky`; the reviewer read its guidance, API, and confirmation policy. The reviewer does not invoke installation, uninstall, purge, update, report saving, or external issue submission.

| Field | Value |
|---|---|
| Final source SHA | Pending clean-source commit and verified push |
| Initial package | `artifacts/release/1.0.0-rc.4/SpeedMeterPro-1.0.0-rc.4-Setup.exe` from source `63bd9d1645950416b97964863ce3df8dad47a94c`; Director-reported release drill PASS, then superseded by installer cleanup changes |
| Final package | Pending build and Director handoff |
| Host | Windows 10 build 19045 developer host; not Windows 11 acceptance |
| Initial display | 1536 × 864 primary display, 1536 × 824 work area; `GetDpiForSystem` 96 DPI (100%) |
| Accessibility bridge | `@oai/sky` initialized; it identified `Install SpeedMeter Pro` window ID 5310082 from the first RC.4 Setup process and returned a UI Automation tree. Its window capture repeatedly timed out on both Setup and Narrator. |

## Source review before native observation

- `InstallerWindow` has resizable work-area-bounded dimensions, scrollable body, persistent action footer, explicit keep-data default, purge confirmation, and default/cancel buttons. Native focus, text fit, and High Contrast behavior remain to be observed.
- `FirstRunWindow` has a resizable scroll region with the Get Started action outside it. Theme and Units use `AutomationProperties.LabeledBy`; actual screen reader names and keyboard order remain to be observed.
- Settings exposes the update checkbox, current version/channel, status text with `LiveSetting=Polite`, named progress, and action buttons. The final source adds: “Installing an update closes and restarts SpeedMeter Pro. Your history and settings are kept.” The PRD §25.2 requires version, release notes, size, and restart disclosure; it does not require a separate banner or dialog. Source search found no update banner or dialog, so those surfaces are absent rather than observed.
- `CrashRecoveryWindow` gives initial focus to View report, provides named View/Save/Report issue/Dismiss buttons, and a named selectable report preview. Native focus transfer and return, clipping, and Narrator speech remain to be observed.

## Native observation matrix

| Surface and condition | Keyboard / visual focus | Narrator spoken output | Layout / clipping | Result |
|---|---|---|---|---|
| Setup, 100% DPI, initial RC.4 package before subsequent source fix | Tab reached Cancel → Install → an unreported focus stop or scroll region → Start With Windows → Cancel. Reverse Tab reached Start With Windows → Create a desktop shortcut. UIA exposed both checkboxes, action buttons, and the per-user path. No action button was invoked. | Built-in Narrator was launched. `CapsLock+Alt+X` did not expose a speech recap window; its secondary window had higher Windows integrity than the helper. Spoken strings could not be captured. Narrator was stopped afterward. | Three `sky.get_window_state` screenshot attempts timed out, so clipping and visible focus could not be inspected. | Partially observed on superseded package; final package not verified |
| Uninstall keep-data and purge choice, 100% DPI | Pending | Pending | Pending | Not verified |
| First run, 100% DPI | Pending | Pending | Pending | Not verified |
| Settings updates, 100% DPI | Pending | Pending | Pending | Not verified |
| Crash recovery and diagnostic preview, 100% DPI | Pending | Pending | Pending | Not verified |
| Consumer surfaces, 150% DPI | Pending | Pending | Pending | Not verified |
| Consumer surfaces, increased text | Pending | Pending | Pending | Not verified |
| Consumer surfaces, High Contrast | Pending | Pending | Pending | Not verified |

## Decision and handoff

**ACCESSIBILITY CONSUMER SURFACES NOT VERIFIED.** The initial Setup keyboard/UIA observation applies to a superseded package. Final packaged native windows, visual focus, spoken Narrator output, 150% DPI, increased text, and High Contrast remain unobserved. The `@oai/sky` screenshot bridge timed out on both Setup and Narrator, and the Narrator secondary window was inaccessible at higher Windows integrity; UIA names cannot substitute for speech or visual evidence. The Director retains integration and gate authority.
