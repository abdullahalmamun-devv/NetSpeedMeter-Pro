# SpeedMeter Pro 1.0.0-rc.2 release evidence

**Date:** 12 October 2026 (Asia/Dhaka)
**Decision:** TEST-SIGNED FIELD/LAB RC produced; consumer experience closure is **not verified** and Gate E2 is **blocked**.
**Baseline:** `37b5f57a086fcb9d11ae248e136b1c20f6ff78af` (`origin/main`, also confirmed as public `main` before implementation through the GitHub repository connector).
**RC2ReleaseSourceSha:** `b0e425f34ac3033ee36bf2da27ee9d339a9b3bed`. The release orchestrator checked a clean working tree at this SHA.
**Host:** Windows 10 Pro build 19045, .NET 10.0.12. This is developer-host evidence, not Windows 11 validation or authorization of a Windows 10 product port.

## Version and package identity

| Field | Observed value |
|---|---|
| ProductVersion | `1.0.0-rc.2` |
| FileVersion | `1.0.0.2` |
| AssemblyVersion | `1.0.0.0` |
| DB schema | `3` |
| Channel | `rc` |
| Primary file | `artifacts/release/1.0.0-rc.2/SpeedMeterPro-1.0.0-rc.2-Setup.exe` |
| Exact bytes | `217575696` |
| Independently recomputed SHA-256 | `5ac09394b27c2d6c9f7be2cf8b5b3961e059652d6874d0dc85c2d1a459c1eedf` |
| Signing | Local Test mode, self-signed `CN=SpeedMeter Pro Release Candidate Signing`, thumbprint `82BB4B0B71098DA66807E7753028349D12AD5B0B`; `Get-AuthenticodeSignature` reports `UnknownError` because its root is untrusted. Not for public distribution. |
| Installed DLL ProductVersion | `1.0.0-rc.2+b0e425f34ac3033ee36bf2da27ee9d339a9b3bed` |

The final run log is `artifacts/rc2-release-authoritative.log`; the release directory contains `RELEASE-HASHES.sha256`, self-contained and framework-dependent ZIPs, SPDX 2.3 SBOM, and third-party notices. The RC.1 artifacts and evidence were preserved. Because signing embeds time-dependent certificate/signature data, the preceding superseded RC.2 artifact hashes are not authoritative; only the values above identify this build.

## Executed verification

| Check | Result |
|---|---|
| `scripts/release.ps1 -SignMode Test` | Exit `0`, no bypass flags, clean source guard passed. It ran build, Windows fixtures, native WPF smoke, package/signing, SBOM/hashes, and silent install/uninstall drill. |
| Release build | 7 projects; 0 warnings, 0 errors. `NuGetAudit=false` was set because the external NuGet vulnerability feed was unavailable in this environment; no fresh vulnerability audit is claimed. |
| `scripts/check.ps1` (separate run) | 268 PASS, 1 intentional SKIP, 0 FAIL, 269 evaluated. |
| `scripts/check.ps1 -Windows` (release run) | 276 PASS, 1 intentional SKIP, 0 FAIL, 277 evaluated. This is the executable fixture runner, not `dotnet test` with zero discovered tests. |
| `scripts/smoke.ps1` (release run) | Exit `0`, live collector readings; all named WPF navigation, widget, tray, state, and storage assertions True. Artifact: `artifacts/smoke-20261012-120833`. |
| Primary Setup.exe | Silent per-user custom-directory install and uninstall completed within 30 seconds each with exit code `0`. Installed `SpeedMeter.App.exe` and `.dll` existed; the DLL embedded the exact release source SHA. The drill did not exercise the interactive Windows Settings UI, data purge, or application relaunch. |
| Checksums | Primary Setup.exe bytes and SHA-256 recomputed independently and matched `RELEASE-HASHES.sha256`. |

## Consumer acceptance still open

- **EXTERNAL-DEPENDENCY-PENDING:** Protected production update manifest private signing authority and trusted public Authenticode certificate. The pinned production public key is provisional. A test-signed installer is not accepted by the installed Production trust mode.
- **EXTERNAL-DEPENDENCY-PENDING:** Public GitHub Pages signed manifest and versioned GitHub Release asset publication/live URL verification. No GitHub token is embedded or required by clients.
- **ENVIRONMENT-PENDING:** Actual installed RC.1 → RC.2 one-click updater path, including Production manifest/WinVerifyTrust trust, graceful exit, installer upgrade, relaunch, post-update collector/storage health, and preservation of history, totals, quota, settings, theme, widget, alert rules, and first-run state. Deterministic service/installer fixtures do not prove this end-to-end path.
- **ENVIRONMENT-PENDING:** Interactive installer/uninstaller through Windows Settings; keep-data/reinstall and explicit purge observations; controlled UI/background crash capture and next-launch View/Save/Report Issue; keyboard/Narrator, enlarged text, 150% DPI and High Contrast observations; packet-level zero-network privacy check. Security and accessibility independent reviews remain requested until these are observed.
- Windows 11 physical matrix, 24-hour mixed workload, and 7-day soak are Gate E2 work and were intentionally not started.

An earlier installer fixture used a sandbox install target but accidentally pointed its purge at real `%LocalAppData%\SpeedMeterPro`. The live database lock prevented full deletion in that run; unaffected data was not established. The fixture now supplies an isolated temporary user-data path. The final release drill did not invoke purge and did not target the account's user-data directory.

## Director decision

The RC.2 source and test-signed package are suitable for further lab review, but the mandatory end-to-end consumer update, data preservation, interactive accessibility, privacy observation, and external production trust/publication evidence are missing. **CONSUMER EXPERIENCE CLOSURE NOT VERIFIED — GATE E2 BLOCKED.**
