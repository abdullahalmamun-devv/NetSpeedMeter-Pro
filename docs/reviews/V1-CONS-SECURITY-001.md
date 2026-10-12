# V1 consumer closure — independent security, privacy, and QA review

**Reviewer:** Security / Privacy + QA
**Scope:** V1-CONS-UPDATE-001, V1-CONS-INSTALL-001, V1-CONS-CRASH-001
**Authority:** PRD §§24–25; ADR-014/015; consumer closure request §§10–17, 22, 29–36, 43–58, 62–68
**Decision:** CHANGES REQUESTED pending executable and packaged-flow evidence. The source fixes below are accepted on inspection. This review does not declare integration, feature Done, or a gate passed.

## Evidence inspected

- `PROJECT_BOARD.md`, `IMPLEMENTATION_STATUS.md`, the three task and handoff files, ADR-014/015, PRD §§24–25, and the consumer closure request.
- `UpdateService`, `UpdateVerificationService`, `CrashReportService`, application crash wiring/recovery window, installer engine/program/window, and the three dedicated fixture files.
- Source review only. The Director owns the coordinated build, executable fixture run, packaged installation, updater rehearsal, Windows 11 matrix, and Gate E2 evidence. No live production manifest or final production key is evidenced; the update operations document labels this `EXTERNAL-DEPENDENCY-PENDING`.

## Findings

1. **Resolved in source, execution pending — installer launch rollback.** The first review found that `InstallerEngine` could delete its backup, then catch a `Process.Start` failure and roll back a committed installation. Launch now runs outside the transactional catch (`InstallerEngine.cs:327–344`); a launch failure is logged as a warning without deleting the installation. A failing-launch regression and actual packaged upgrade should still execute.
2. **Resolved in source, execution pending — staging growth and ready-state loss.** `UpdateService` now removes only its named staging files on service construction and before a new download (`UpdateService.cs:74,179–180,289–307`) and prevents a new check from overwriting `ReadyToInstall` (`79,91`). The dedicated fixture covers an orphan and a manual recheck. Confirm actual Windows process-file sharing permits cleanup after installation and never removes an in-flight installer.
3. **Resolved in source, native rehearsal pending — verification-to-launch race.** A `FileShare.Read` handle now spans final hash verification, Authenticode and `Process.Start` (`UpdateService.cs:255–270`). This closes normal same-user write/delete sharing during process creation. Validate it with the real signed Setup.exe and a replacement attempt; Windows loader/WinVerifyTrust behavior is not proven by source inspection.
4. **P2 — Required consumer update matrix remains partially unexecuted.** `ConsumerUpdateFixtures.cs` now registers eleven checks (`17–28`), adding signed newer/current/older offers, tampering, offline response, hash/size failure cleanup, cancellation, and ready-state restart cleanup. Those fixtures still await the integrated executable run. Publisher/WinVerifyTrust failure, download interruption, insufficient disk, real installer launch, and physical RC.1→RC.2 update/restart remain unobserved. Record Windows-only evidence separately. Consumer closure request §§63–66 names these cases.
5. **P2 — Installer and crash fixtures do not prove the consumer flows.** `ConsumerInstallerFixtures.cs` checks initial labels/defaults only (`14–40`); `ConsumerCrashFixtures.cs` tests service records and URI composition (`12–82`). Neither verifies packaged setup/uninstall rollback, cancel/purge/reinstall, RC.1→RC.2 data survival, WPF fatal capture, or View/Save/Report issue behavior. Those require the Director's integration and live drills before feature completion under request §§64–68.

## Positive observations and limits

- Automatic update checks default off and return before HTTP when consent is absent (`SettingsDomain.cs:94–95`, `UpdateService.cs:71–85`); manual check is explicit. The privacy document discloses GitHub contact and the one-hour failure backoff. This is source and fixture evidence, not a packet-level observation.
- Consumer update verification explicitly uses Production trust for manifest and Authenticode (`UpdateService.cs:115–117`, `203`, `247`); the test public-key override only applies to DevelopmentTest (`UpdateVerificationService.cs:86–115`). `ReleaseNotesUrl` is included in canonical signed bytes (`UpdateVerificationService.cs:154–159`). The externally provisioned production key, live GitHub URL, and production certificate remain pending.
- Manifest and payload reads are bounded and cancellation-aware. The manifest has a 64 KiB ceiling; packages have a signed size, 512 MiB ceiling, stream copy, and 15-minute timeout. The installer launch still needs the integrity boundary in finding 3.
- Crash capture writes local bounded records, retains 12, marks unknown unclean termination separately, and leaves WPF fatal exceptions unhandled. The issue URL contains intended metadata, while report text requires a user-selected save and is never attached automatically. The redaction fixture covers named SSID/token, IPv4, MAC, URL, and user path examples; heuristic redaction of arbitrary exception text should be described as best effort, with report review before sharing.
- The graphical uninstaller defaults to retaining data and asks for a second confirmation before purge. Its actual Windows Settings invocation and deferred binary cleanup have not been observed in a packaged drill.

## Re-review criteria

Attach integrated build/fixture and relevant live Windows drill results for findings 1–5. Keep the feature and Gate E2 status pending where external keys, publication, physical upgrade, and field evidence are unavailable.
