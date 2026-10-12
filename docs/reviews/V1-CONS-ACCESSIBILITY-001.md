# V1 consumer closure — independent accessibility and product review

**Reviewer:** Accessibility / Product
**Scope:** V1-CONS-UPDATE-001, V1-CONS-INSTALL-001, V1-CONS-CRASH-001
**Authority:** PRD §§6, 24–25; consumer closure request §§22–28, 38–47, 48–58
**Decision:** CHANGES REQUESTED for native accessibility evidence. All five original source findings have responsive fixes on reinspection. This review does not declare integration, feature Done, or a gate passed.

## Evidence inspected

- `PROJECT_BOARD.md`, `IMPLEMENTATION_STATUS.md`, `docs/engineering-office.md`, the three consumer task and handoff files, PRD §§6 and 24–25, and the consumer closure request.
- Current `InstallerWindow.cs`, `SettingsView.xaml`, `SettingsViewModel.cs`, `FirstRunWindow.xaml` and code-behind, `CrashRecoveryWindow.cs`, and application startup/recovery wiring.
- `artifacts/rc2-release-final.log`: clean source `70c873052eeb320134318963a8107aa19c37c69c`; Release build 0 warnings/errors; executable Windows fixtures 276 passed, 1 intentional skip, 0 failed; WPF smoke assertions shown true; test-signed RC.2 Setup.exe silent install and uninstall both succeeded, with installed commit identity verified. This verifies build and automated flows on Windows 10 build 19045. The silent installer drill and general WPF smoke do not exercise the graphical accessibility flows below.

## Original findings — source disposition

1. **P1 installer scaling: addressed in source.** `src/SpeedMeter.Installer/InstallerWindow.cs:55–64` now bounds initial/min/max dimensions to `SystemParameters.WorkArea` and permits resizing. The action footer remains separate while the body has a vertical `ScrollViewer` (`:80–97`). This is a suitable source fix for long retained-data and error-log paths. Native 150% DPI and increased-text verification remains outstanding.
2. **P1 first-run labels and scaling: addressed in source.** `src/SpeedMeter.App/Views/FirstRunWindow.xaml:5–9` now uses a standard resizable window; the heading and subtitle wrap (`:24–32`), and consent/preferences scroll while Get Started remains in its own grid row (`:35–36`, `:137–146`). `FirstRunWindow.xaml.cs:20–28` clamps initial and maximum dimensions to the current work area. Theme and Display Units use programmatic `AutomationProperties.LabeledBy` references (`FirstRunWindow.xaml:117–130`). Native Narrator and small-work-area verification remains outstanding.
3. **P2 update status announcement: addressed in source, behavior unverified.** The bound state message now has `AutomationProperties.LiveSetting="Polite"` (`src/SpeedMeter.App/Views/SettingsView.xaml:196`), while the progress bar has a name but no live setting (`:198`). This avoids an intentional per-percentage announcement. Verify with Narrator that available, up-to-date, offline, and verification-failed changes are actually spoken; if WPF does not raise the live-region change event from this binding, add an explicit UI Automation notification.
4. **P2 Settings control names: addressed in source.** The visually labeled Theme, Sampling Interval, Reporting Timezone, Billing Cycle Start Day, Widget Layout, Widget Opacity, and Retention Policy controls now each have `AutomationProperties.Name` (`SettingsView.xaml:30`, `:71`, `:84`, `:93`, `:118`, `:133`, `:157`). Verify spoken labels and logical tab order, including the Updates buttons, in Narrator.
5. **P2 crash recovery layout/focus: addressed in source.** The recovery actions are within a scrollable content region, and initial keyboard focus goes to View report (`src/SpeedMeter.App/CrashRecoveryWindow.cs:42–50`). Buttons have explicit names; the redacted preview has a named, selectable TextBox and Save report moves focus to Save a copy (`:53–57`, `:89–101`). Verify actions remain reachable, preview focus/return, save dialog, and browser action in a native session.

## Consumer clarity and remaining limits

- Installer shortcut/startup choices are explicit; uninstall defaults to retaining data and requires a second confirmation before purge (`InstallerWindow.cs:19–22`, `:41–53`, `:161–167`). Successful uninstall explains retained-data location. The release drill was silent, so actual graphical setup/uninstall and Windows Settings invocation remain unobserved by this review.
- First run and Settings disclose GitHub update checks, source IP exposure, check cadence, and manual fallback (`FirstRunWindow.xaml:83–87`, `SettingsView.xaml:189–192`). The update controls are nonblocking in Settings. A user-visible update notification outside Settings remains a separate product-flow review concern; the current source review does not establish how a user discovers an available update while using another view.
- Crash recovery is modeless and says that nothing was sent. View, Save, Report issue, and Dismiss remain separate choices. The report preview is redacted and selectable. Native save/browser operation is unobserved.
- The first-run LOCAL-FIRST badge now uses white text on `#065F46` (`FirstRunWindow.xaml:47–48`), yielding calculated contrast above 4.5:1 in the normal theme. High contrast still needs OS-level observation. Installer brushes are chosen from `SystemParameters.HighContrast` only at construction (`InstallerWindow.cs:67–78`); a system contrast switch while it remains open may need resource/theme change handling.
- English and Bengali text scaling/localization, reduced motion, and an actual user/Narrator review have no evidence in this package log. Record any unmet PRD §6.3 coverage as a release limitation rather than claiming it from build or smoke results.

## Evidence needed for accessibility sign-off

Observe the actual graphical Setup.exe install/uninstall and purge confirmation, first-run consent, Settings update state changes, and crash recovery with keyboard and Narrator. Exercise 150% DPI, increased Windows text size, high contrast, and a small work area; record which combinations were tested and whether every action is visible, named, focused, and operable. The current 276-pass fixture run, general WPF smoke, and silent package drill do not cover those interactions. Keep this review at CHANGES REQUESTED until native evidence is attached; Gate E2 remains a Director decision.
