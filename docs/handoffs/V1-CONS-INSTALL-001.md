# V1-CONS-INSTALL-001 handoff

**Owner:** Windows Release / Installer engineer
**Reviewer:** Accessibility + QA + Project Director
**PRD:** §25.1, §25.2; consumer closure §§33–35 and 38–47
**Dependencies:** Gate E1 passed; ADR-012, ADR-013, ADR-015; existing transactional `InstallerEngine`
**Status:** Implementation submitted for independent review; integration and feature Done are Director decisions.

## Scope and files

- `src/SpeedMeter.Installer/SpeedMeter.Installer.csproj`: enable WPF in the existing windowed, self-contained installer project.
- `src/SpeedMeter.Installer/Program.cs`: interactive setup and uninstall enter the WPF window. Silent CLI remains available. `-Silent -LaunchAfterInstall` is the update invocation contract; `-NoLaunch` overrides. `-DesktopShortcut` and `-StartWithWindows` are explicit opt-ins for silent installs; omitted choices preserve existing upgrade preferences.
- `src/SpeedMeter.Installer/InstallerWindow.cs`: install location disclosure, desktop and startup choices reflecting current registrations, progress/error state, success and launch choice; uninstall defaults to keeping data, discloses retained location, and requires a second confirmation for permanent purge. A failed operation exposes its setup log path; exception detail stays in that local log.
- `src/SpeedMeter.Installer/InstallerEngine.cs`: optional HKCU Run registration, staged-update cleanup on uninstall, purge failure propagation before unregistering, required uninstaller/registry registration, native shell-link creation without a PowerShell fallback, and rollback removal of newly committed files while retaining the backup if restoration fails. Existing graceful close and transactional staging remain the engine used by both modes.
- `tests/SpeedMeter.Checks/ConsumerInstallerFixtures.cs`: STA WPF checks of setup and uninstall default choices. The Director has registered `ConsumerInstallerFixtures.Run(Check)` in the shared fixture runner.

## Acceptance and verification

- Source inspection: Windows uninstall registration still invokes `Uninstall.exe -Uninstall`, which now opens the interactive WPF uninstall window; quiet uninstall still invokes `-Uninstall -Silent`.
- Source inspection: neither normal install nor uninstall automatically adds startup; retained data is the default; user-selected purge displays a separate Yes/No warning before engine invocation.
- Source inspection: setup invokes the existing `RequestGracefulShutdown` and staging/rollback engine, and update CLI launch occurs only after install returns successfully.
- Independent security review found that a launch exception after backup deletion could enter rollback and remove the newly installed files. Launch and final success notification now occur strictly after the transactional `try/catch`; launch failure is reported as a post-install warning through the installer log callback and cannot trigger rollback.
- Independent accessibility review found the fixed-size, unscrollable window could clip choices and retention disclosure at high DPI or text scaling. The window is now resizable and bounded to 94% of work-area width and 90% of work-area height. Content scrolls vertically while a wrapping action footer stays outside the scroll region. A dedicated fixture checks the scroll container, footer docking and work-area bound.
- **Build not yet verified.** An initial installer build collided with another agent's shared Core `obj` output (`CS2012` file lock). A second attempt using a separate intermediate path lacked a restored assets file (`NETSDK1004`). The Director requested a coordinated integrated build after edits settle.
- **Fixtures not yet executed.** The Director will run the shared executable fixture runner after concurrent edits settle.
- Physical Windows Settings uninstall, Narrator, high contrast, Windows 11, update restart, and RC.1→RC.2 data survival remain review/field-validation work. Do not treat source inspection as physical evidence.
- Silent update launch warnings are available only to a caller that supplies an installer log callback; the current `-Silent` Program suppresses console messages. A failed automatic restart still leaves the new installation intact and can be started from Windows Start. End-to-end update outcome reporting needs coordinated update-service review.

## Review focus

Confirm the WPF window compiles and behaves on the release host; keyboard and Narrator labels; successful install/upgrade/uninstall through the actual packaged Setup.exe; direct Windows Settings uninstall; reboot/startup setting only when selected; and update launch with `-Silent -LaunchAfterInstall`. The existing delayed self-removal batch runs hidden after the uninstaller closes; it should be checked on a real package for complete binary removal.
