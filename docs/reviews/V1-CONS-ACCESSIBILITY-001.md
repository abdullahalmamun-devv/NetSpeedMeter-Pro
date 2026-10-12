# V1 consumer closure — independent accessibility and product review

**Reviewer:** Accessibility / Product
**Scope:** V1-CONS-UPDATE-001, V1-CONS-INSTALL-001, V1-CONS-CRASH-001
**Authority:** PRD §§6, 24–25; consumer closure request §§22–28, 38–47, 48–58
**Decision:** CHANGES REQUESTED. This is source review; native keyboard, Narrator, 150% DPI/text scaling, high contrast, and packaged-flow evidence is still required. The Project Director owns integration, feature Done, and gate decisions.

## Evidence inspected

- `PROJECT_BOARD.md`, `IMPLEMENTATION_STATUS.md`, `docs/engineering-office.md`, the three consumer task and handoff files, PRD §§6 and 24–25, and the consumer closure request.
- `InstallerWindow.cs`, `SettingsView.xaml`, `SettingsViewModel.cs`, `FirstRunWindow.xaml` and code-behind, `CrashRecoveryWindow.cs`, and application startup/recovery wiring.
- No build or native UI session was run during this review because the Director is coordinating shared build outputs. These findings describe source-observable behavior and risks, not witnessed Narrator or display-scaling failures.

## Actionable findings

1. **P1 — Installer content can be clipped at enlarged scaling.** `src/SpeedMeter.Installer/InstallerWindow.cs:55–61` fixes a 570×440 window and disables resizing; `:77–88` places an unscrollable `_body` above the footer. The retained-data path, status/error log path, and buttons must remain reachable at 150% DPI and increased text size under PRD §6.2. Bound the window to the available work area, allow resizing, and put the body in a vertical `ScrollViewer` while keeping the action footer visible. Verify install, uninstall, purge confirmation, long paths, and error states at 150% DPI and enlarged text.
2. **P1 — First-run consent choices lack reliable accessible names and have scaling risk.** `src/SpeedMeter.App/Views/FirstRunWindow.xaml:5–10` fixes a borderless 520×580 window with no resize; the header at `:24–32` does not wrap. The Theme and Display Units text blocks at `:116–130` are visually adjacent but are not associated with their ComboBoxes, so a screen reader may announce only the selected value. Set `AutomationProperties.Name` or `LabeledBy` on both ComboBoxes, wrap header text, and ensure the whole flow fits or scrolls within the work area at 150% DPI and enlarged text. Verify every consent choice, Get Started, and focus sequence with keyboard and Narrator. The update opt-in itself is clearly worded and defaults off in source.
3. **P2 — Update results are not announced as state changes.** `src/SpeedMeter.App/Views/SettingsView.xaml:196–198` binds an asynchronously changing message and download progress, but the message has no `AutomationProperties.LiveSetting`. A user can invoke Check for updates and receive no screen reader announcement of available, up-to-date, offline, or verification-failed results. Mark the state message as a polite live region and verify announcement on meaningful state transitions; do not announce every progress percentage. The ViewModel supplies explicit state messages in `src/SpeedMeter.App/ViewModels/SettingsViewModel.cs:135–149`.
4. **P2 — Several Settings controls have no programmatic label.** `src/SpeedMeter.App/Views/SettingsView.xaml:28–34`, `:70–93`, and `:117–133` put labels beside ComboBoxes, a TextBox, and the Widget Opacity Slider without `AutomationProperties.Name` or `LabeledBy`. Give each control a concise label, especially Color Theme, Sampling Interval, Reporting Timezone, Billing Cycle Start Day, Layout Style, Retention Policy, and Widget Opacity. Check Narrator names and tab order through the entire Updates section.
5. **P2 — Crash recovery layout and focus need native verification.** `src/SpeedMeter.App/CrashRecoveryWindow.cs:20–27` caps height at the work area but uses a `StackPanel` without scrolling; `:42–47` provides a wrap panel of actions. At narrow width, high scaling, or longer localized text, actions may wrap below the cap. Add a scrollable content region or demonstrate that all actions remain visible and keyboard reachable. `:87–99` correctly gives the redacted preview an accessible name and moves focus to Save when Save report is chosen; verify initial recovery focus, preview focus, and return focus after closing the modeless window.

## Positive observations and limitations

- Installer startup and desktop shortcuts are explicit choices; uninstall starts with Keep Data and shows a second confirmation before purge (`InstallerWindow.cs:19–22`, `:41–53`, `:152–158`). The success state explains retained data location. These are clear consumer choices on source inspection.
- First-run update disclosure names GitHub, source IP, check cadence, and manual fallback (`FirstRunWindow.xaml:83–87`). Settings repeats the disclosure (`SettingsView.xaml:189–192`). The async update UI is inside Settings and does not block live monitoring.
- Crash recovery is modeless, says nothing was sent, offers View, Save, Report issue, and Dismiss, and provides a selectable redacted preview (`CrashRecoveryWindow.cs:27–49`, `:59–99`). Its buttons have explicit automation names. Actual save/browser behavior and user focus were not observed.
- High contrast behavior needs an OS-level check. The installer samples `SystemParameters.HighContrast` only during construction (`InstallerWindow.cs:64–75`), so a mode switch while it is open may leave hard-coded brushes in place. The first-run local-first badge uses fixed white on green (`FirstRunWindow.xaml:47–48`); its calculated normal-theme contrast is 2.54:1, below the PRD's proposed 4.5:1 ordinary-text target. Use theme/system colors or a verified contrast pair.
- No Bengali localization or actual user/Narrator study is evidenced in this RC.2 source review. Record this as a PRD §6.3 release limitation unless native review evidence is supplied.

## Re-review criteria

Reinspect source after findings 1–4 are addressed, then observe the packaged installer/uninstaller, first run, Settings update states, and crash recovery with keyboard and Narrator. Exercise 150% DPI, increased Windows text size, high contrast and a small work area. Record which combinations were actually observed. Keep CHANGES REQUESTED until these results are attached; this review alone does not pass consumer closure or Gate E2.
