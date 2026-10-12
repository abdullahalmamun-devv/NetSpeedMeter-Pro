# V1-CONS-QA-003 — Destructive safety regression handoff

- **Owner:** QA specialist
- **Reviewers:** Security/Privacy specialist and Project Director
- **PRD references:** §§24, 25.1–25.2; AC-S03, AC-S07
- **Status:** Implemented; independent review pending. No Gate E2 decision.
- **Dependency:** V1-CONS-SAFETY-003 internal helper validation seam and quarantine delete path.

## Scope and files

Changed `tests/SpeedMeter.Checks/ConsumerInstallerFixtures.cs`. Every destructive setup is under a `TestSandboxGuard.CreateRoot()` root at `%TEMP%\SpeedMeterPro.Tests\<GUID-N>` with a valid sentinel. No fixture invokes purge on the current user's real product profile. The test-only data override rejects real `%LocalAppData%\SpeedMeterPro`, `%LocalAppData%` itself, the user-profile root, the drive root, traversal, and non-sentinelled roots.

## Implemented checks

- Owned install validation rejects absent, forged, changed, and wrong path-bound markers; an arbitrary target and a replacement target remain intact after rejection.
- A valid owned install beneath `Family & (Work)!` passes validation and completes direct managed uninstall without shell parsing.
- A junction fallback supplies a real reparse component when symlink creation is unavailable. The uninstall path rejects it. In the data path, a junction inserted **after** an earlier valid preflight is rejected before purge; the outside file and owned install remain.
- Helper request fixtures construct a matching source/helper SHA-256 pair and validate the positive request. They reject arbitrary target, incorrect nonce, absent/stale/nonexistent parent context, mismatched `-Dir`, modified marker, modified helper binary, and replaced target.
- Quarantine purge removes only the selected sentinelled data and owned install, retaining an unrelated sibling. A locked disposable data file forces partial purge; uninstall removes program files, reports that some local data could not be deleted, and leaves a discoverable residual tree.
- Closing setup or uninstall through the Cancel button retains Windows user-exit code 1602 rather than claiming success.

## Executed evidence

The executable `SpeedMeter.Checks` runner was used, not `dotnet test` discovery. Because the NuGet vulnerability feed was unreachable (`NU1900` as error), local runs used `-p:NuGetAudit=false`; these runs do not constitute a package vulnerability audit. Output is saved at `artifacts/qa-cons-safety-final.txt` and `artifacts/qa-cons-safety-windows.txt`.

| Run | Result | Installer/safety result |
|---|---|---|
| Release runner | 281 passed, 1 intentional skip, 0 failed; exit 0 | All 13 `CONS-INSTALL`/`CONS-SAFETY` checks passed |
| Release runner `--windows` on Windows build 19045 | 288 passed, 1 intentional skip, 1 failed; exit 1 | All 13 `CONS-INSTALL`/`CONS-SAFETY` checks passed |

The Windows-only failure was the existing `Windows native WLAN BSSID correlation and band truth` check: “Active operational Wi-Fi must report non-null SSID.” It is outside this fixture's ownership and requires Director diagnosis; the Windows lane is not green.

## Limits and required follow-up

The internal helper seams exercise request and parent identity validation inside a sentinelled root. They do not execute the packaged `Uninstall.exe` named-pipe handshake, wait for a real uninstaller parent to exit, or deterministically schedule a filesystem swap between final revalidation and a delete syscall. The release/native packaged drill must verify completion from the external helper result, target removal and no residual quarantine; Security must review the remaining local-filesystem race model. The junction fixture verifies insertion between earlier validation and the later uninstall invocation, not an adversarial concurrent race during recursive traversal.

Windows build 19045 is developer-host evidence only and does not validate Windows 11 support or authorize a Windows 10 product port.
