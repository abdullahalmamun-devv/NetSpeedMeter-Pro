# V1 consumer closure — independent security, privacy, and QA review

**Reviewer:** Security / Privacy + QA
**Scope:** V1-CONS-UPDATE-001, V1-CONS-INSTALL-001, V1-CONS-CRASH-001
**Authority:** PRD §§24–25; ADR-014/015; consumer closure request §§10–17, 22, 29–36, 43–58, 62–68
**Decision:** CHANGES REQUESTED for the remaining consumer-flow and production-trust evidence. The RC.2 source fixes, executable fixtures, and test-signed package drill below are accepted as lab evidence. This review does not declare integration, feature Done, or a gate passed.

## Evidence inspected

- `PROJECT_BOARD.md`, `IMPLEMENTATION_STATUS.md`, the three task and handoff files, ADR-014/015, PRD §§24–25, and the consumer closure request.
- `UpdateService`, `UpdateVerificationService`, `CrashReportService`, application crash wiring/recovery window, installer engine/program/window, and the three dedicated fixture files.
- `artifacts/rc2-release-authoritative.log` and `docs/evidence/RELEASE-EVIDENCE-v1.0.0-rc.2.md`. The authoritative rerun started from clean Git HEAD `b0e425f34ac3033ee36bf2da27ee9d339a9b3bed` on Windows 10 build 19045. I also inspected the earlier `artifacts/rc2-release-final.log`; its source SHA and package hash are superseded and do not identify the current artifact. No live production manifest or final production key is evidenced; the update operations document labels this `EXTERNAL-DEPENDENCY-PENDING`.

## Observed RC.2 lab result

- The release build completed across seven projects with **0 warnings and 0 errors**. The executable Windows fixture runner reported **276 passed, 1 intentional skip, 0 failed**, including all 11 consumer update, 3 installer, and 4 crash fixtures. The skip was the fixture for an unavailable platform timezone; it was not an update, installer, or crash failure.
- Native WPF smoke completed with its named collector, navigation, widget, tray, and storage assertions True. That smoke did not exercise the new interactive installer or crash recovery window.
- The authoritative orchestrator produced a **test-signed** standalone `SpeedMeterPro-1.0.0-rc.2-Setup.exe` (217,575,696 bytes; SHA-256 `5ac09394b27c2d6c9f7be2cf8b5b3961e059652d6874d0dc85c2d1a459c1eedf`). It completed a silent sandbox install and uninstall with exit code 0, checked installed application files, and verified embedded version/source identity `1.0.0-rc.2+b0e425f34ac3033ee36bf2da27ee9d339a9b3bed`.
- The self-signed test certificate yielded Authenticode `UnknownError` under the untrusted test root, as expected for this lab mode. This package cannot establish Production trust or public-distribution readiness.
- The release evidence records `NuGetAudit=false` because the vulnerability feed was unavailable. The clean build does not establish a fresh dependency vulnerability audit.

## Findings

1. **Resolved in source, full update rehearsal pending — installer launch rollback.** The first review found that `InstallerEngine` could delete its backup, then catch a `Process.Start` failure and roll back a committed installation. Launch now runs outside the transactional catch (`InstallerEngine.cs:330–347`); a launch failure is logged as a warning without deleting the installation. The final silent install/uninstall drill passed, but it did not invoke `-LaunchAfterInstall`, inject a launch failure, or upgrade RC.1.
2. **Resolved in source and fixture, native update rehearsal pending — staging growth and ready-state loss.** `UpdateService` removes its named staging files on service construction and before a new download (`UpdateService.cs:74,179–180,289–307`) and prevents a new check from overwriting `ReadyToInstall` (`79,91`). The executable fixture passed orphan cleanup and manual recheck. Confirm actual Windows process-file sharing permits cleanup after installation and never removes an in-flight installer.
3. **Resolved in source, native rehearsal pending — verification-to-launch race.** A `FileShare.Read` handle now spans final hash verification, Authenticode and `Process.Start` (`UpdateService.cs:255–270`). This closes normal same-user write/delete sharing during process creation. Validate it with the real signed Setup.exe and a replacement attempt; Windows loader/WinVerifyTrust behavior is not proven by source inspection.
4. **P2 — Consumer update matrix is partially proven.** The 11 dedicated fixtures passed: consent/cadence, manual malformed rejection, SemVer, signed release notes and Production-key separation, offline response, signed newer/current/older offers, tampering, hash/size cleanup, cancellation, and ready-state cleanup. The Wave 5 fixtures additionally cover primitive signature, hash, publisher, and package verification rejection. They do not prove the installed Production trust path, an actual interrupted/insufficient-disk download, native `Process.Start` from the locked staged file, or physical RC.1→RC.2 update/restart. Consumer closure request §§63–66 still requires those observations.
5. **P2 — Packaged and WPF consumer flows remain incomplete.** The final package drill verifies silent sandbox install/uninstall only. `ConsumerInstallerFixtures.cs` checks initial labels/defaults and scroll layout; `ConsumerCrashFixtures.cs` checks local records, retention, outcome tracking, redaction examples, and issue URI composition. Windows Settings interactive uninstall, cancellation, explicit purge, keep-data reinstall, RC.1→RC.2 data survival, controlled UI/background fatal capture, and View/Save/Report issue behavior remain unobserved under request §§64–68.

## Positive observations and limits

- Automatic update checks default off and return before HTTP when consent is absent (`SettingsDomain.cs:94–95`, `UpdateService.cs:77–93`); manual check is explicit. The privacy document discloses GitHub contact and the one-hour failure backoff. The executable no-request fixture passed; packet-level observation remains open.
- Consumer construction fixes Production trust for manifest and Authenticode (`UpdateService.cs:55–56`); the test public-key override only applies to DevelopmentTest (`UpdateVerificationService.cs:86–115`). `ReleaseNotesUrl` is included in canonical signed bytes (`UpdateVerificationService.cs:154–159`). The externally provisioned production key, live GitHub URL, and production certificate remain pending.
- Manifest and payload reads are bounded and cancellation-aware. The manifest has a 64 KiB ceiling; packages have a signed size, 512 MiB ceiling, stream copy, and 15-minute timeout. The produced Setup.exe is below that cap. Native locked-file launch still needs the rehearsal in finding 3.
- Crash capture writes local bounded records, retains 12, marks unknown unclean termination separately, and leaves WPF fatal exceptions unhandled. The issue URL contains intended metadata, while report text requires a user-selected save and is never attached automatically. The redaction fixture covers named SSID/token, IPv4, MAC, URL, and user path examples; heuristic redaction of arbitrary exception text should be described as best effort, with report review before sharing.
- The graphical uninstaller defaults to retaining data and asks for a second confirmation before purge. Its actual Windows Settings invocation and deferred binary cleanup were not exercised by the silent packaged drill.

## Re-review criteria

Attach production key/certificate and live GitHub publication evidence, then execute the actual updater from RC.1 to RC.2 with data preservation, graceful exit/relaunch, locked-file launch, and post-update health. Observe the interactive installer/uninstaller and crash UX, accessibility, and packet-level consent behavior on the target environments. Windows 10 build 19045 lab results do not validate Windows 11 support or authorize a Windows 10 product port. Keep consumer closure and Gate E2 pending.
