# V1-CONS-SAFETY-002 — installer purge and destructive test guard

Owner: Windows Installer / Test Safety. Reviewers: independent Security/Privacy and Project Director. PRD §§24, 25.1, 26.13; AC-S03; consumer closure handoff §§7–12. Dependency: RC.2 local source and the shared `SpeedMeter.Core.TestSandboxGuard` contract. Gate E2 remains the Director's decision.

## Incident reconstruction (read only)

At baseline commit `37b5f57`, `InstallerFreshInstallUpgradeAndUninstallDrill` created a temp `speedmeter_matrix_drill_<GUID>` directory and a `UserData` child but passed no override to `UninstallOptions`. Its first uninstall used the production user data path for staging cleanup; its second used `PurgeUserData: true`, which selected `%LOCALAPPDATA%\SpeedMeterPro`. The fixture then deleted only its temp directory. This was a real account data exposure in the test design, regardless of the fixture comment that claimed an override.

The current account's `%LOCALAPPDATA%\SpeedMeterPro` was inventoried without opening, modifying, or deleting contents. It contained `speedmeter.db` (479,232 bytes, last written 2026-10-12 12:08:51 local), `logs`, `crashes`, and lock files. Earlier release evidence reported that a live database lock prevented a complete purge. Those facts do **not** prove all historical data survived or establish a before/after row count. No data recovery or deletion was attempted here.

## Code changes

- The public uninstall options no longer include an arbitrary user data directory. The fixture-only override is internal and validates its target and data as children of the same `%TEMP%\SpeedMeterPro.Tests\<GUID-N>` root with the shared sentinel guard. The real data root is never selected for a sandbox purge.
- Uninstall validation runs before shutdown, staging cleanup, registry edits, and deletion. It requires an absolute non-traversing install path, an owned install marker bound to that exact path plus both binaries, or the legacy canonical install. It rejects reparse components and descendants before recursive deletion. Production purge resolves only the current user's canonical data root.
- Fresh installation refuses a nonempty custom target unless it is an existing recognized SpeedMeter Pro installation. Custom per-user install paths remain available. Paths containing CMD expansion or metacharacters are rejected while the deferred batch removal mechanism remains in use.
- Uninstall normalizes the target before passing it to the deferred batch, validates the batch path, and passes the batch filename through `ProcessStartInfo.ArgumentList`. The deferred batch still has a race between validation and execution; it should be replaced by a managed cleanup helper in a subsequent release hardening change.
- A lab-only Setup build using `SPEEDMETER_UPDATE_LAB` derives the shutdown identity from a validated sandbox root GUID. It filters processes by exact installed executable path and signals only `Local\SpeedMeterPro.Shutdown.<GUID-N>`. Production Setup retains the current-user SID identity. The Director owns the corresponding App and build wiring.
- `V1Wave5ReleaseFixtures` creates roots with `TestDeleteGuard` and explicitly injects sandbox `UserData`; preserve, reinstall, and explicit purge are checked in sequence. `ConsumerInstallerFixtures` adds rejection cases for non-product directories, command characters, traversal, real account data override, parent/profile roots, and reparse descendants where link creation is available.

## Read-only recursive deletion inventory

| Location | Target and containment observed | Disposition |
| --- | --- | --- |
| `tests/SpeedMeter.Checks/V1Wave5ReleaseFixtures.cs` (seven former temp-root deletes) | Previously direct GUID temp roots. Now `TestDeleteGuard.CreateRoot/DeleteRoot`, which validates the Core sentinel, exact temp parent, child tree and reparse boundaries. | Guarded in this change; focused execution pending review. |
| `tests/SpeedMeter.Checks/ConfigExportImportFixtures.cs:82` | `%TEMP%\speedmeter_test_<GUID>`, no sentinel or reparse check. | Outside owned files; **not demonstrably isolated**. |
| `tests/SpeedMeter.Checks/ConsumerCrashFixtures.cs:89` | `%TEMP%\SpeedMeterCrashFixture-<GUID>`, no sentinel or reparse check. | Outside owned files; **not demonstrably isolated**. |
| `tests/SpeedMeter.Checks/ConsumerUpdateFixtures.cs:386` | `%TEMP%\speedmeter_update_fixture_<GUID>`, no sentinel or reparse check in inspected source. | Update agent owns this file; **not demonstrably isolated** until their changes are reviewed. |
| `tests/SpeedMeter.Checks/NetworkToolsFixtures.cs:814` | `%TEMP%\speedmeter_proc_test_<GUID>`, no sentinel or reparse check. | Outside owned files; **not demonstrably isolated**. |
| `tests/SpeedMeter.Checks/ConsumerInstallerFixtures.cs` | A negative fixture removes only its own just-created symbolic link without recursion; root deletion uses `TestDeleteGuard`. | Guarded in this change. |
| `scripts/release.ps1:182` | `%TEMP%\speedmeter_rc_drill_<GUID>` removed recursively in `finally`; no sentinel or reparse check. | Director-owned; **not demonstrably isolated**. Do not run release drill before review. |
| `scripts/package.ps1:24, 131–140, 369, 391` | Release version directory and its staging/backup/setup-publish children. Version regex and lexical containment exist; no reparse boundary check. | Director-owned; **not demonstrably isolated** against junctions. |
| `scripts/package.ps1:267, 279, 282` | Generated `Uninstall.ps1` recursively removes user data on purge and install contents, then generates `rd /s /q` batch from install path. | Director-owned legacy path; **not demonstrably safe** for arbitrary invocation or reparse races. |

The `scripts/package.ps1:260` recursive `Remove-Item` targets a registry key, not a filesystem tree. Single-file deletes were outside this recursive inventory.

## Verification and limitations

Performed: source-level path/deletion audit, baseline `37b5f57` fixture inspection, current account data metadata inventory, and code diff review. No destructive fixture, global check runner, release drill, installer build, or physical install/uninstall was run during this safety task. The shared checkout had concurrent App/update edits, and the Director requested integrated build and tests after source edits settle. Symbolic-link fixture enforcement is conditional on Windows allowing link creation; a blocked link attempt does not count as reparse-path execution evidence.

Before accepting this task, run a coordinated compile, focused non-destructive negative fixtures, and the isolated installer preserve/reinstall/purge drill after independent guard review. Resolve the flagged recursive deletes before the global check runner or release script. Inspect the deferred batch cleanup race and the generated legacy `Uninstall.ps1` path in the security review. This handoff does not declare a passed gate.
