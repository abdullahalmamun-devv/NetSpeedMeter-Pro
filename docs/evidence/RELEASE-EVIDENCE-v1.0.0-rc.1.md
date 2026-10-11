# SpeedMeter Pro Release Candidate Evidence: 1.0.0-rc.1 (Wave 5)

**Evaluation Date:** October 12, 2026  
**Product Version:** `1.0.0-rc.1`  
**FileVersion:** `1.0.0.1`  
**AssemblyVersion:** `1.0.0.0`  
**Database Schema Version:** `3`  
**Evaluator:** SpeedMeter Pro Engineering Office  

---

## 1. Executive Gate Declarations

```text
================================================================================
GATE E1 INDEPENDENTLY VERIFIED — RC FIELD VALIDATION AUTHORIZED (TEST-SIGNED RC LAB MODE)
GATE E2: V1.0 PUBLIC RELEASE NOT VERIFIED — GA BLOCKED
         (Awaiting physical multi-monitor, live Windows 11 matrix,
          24-hour mixed workload, and 7-day soak evidence)
================================================================================
```

SpeedMeter Pro V1.0 has fulfilled all feature and release engineering requirements. Release candidate distribution packages have been compiled, signed, validated, packaged, and verified through automated end-to-end drills. Field validation is authorized. General Availability (GA) remains blocked until physical hardware multi-monitor and live Windows 11 soak tests are concluded.

---

## 2. Release Package Manifest & Checksums

All authoritative artifacts are generated in `artifacts/release/` and verified with SHA-256 hashes in `RELEASE-HASHES.sha256`:

| Artifact | Role | Format | Size | SHA-256 Checksum |
|---|---|---|---|---|
| **`SpeedMeterPro-1.0.0-rc.1-Setup.exe`** | **Primary Canonical Installer** | Standalone Single-File Executable | ~49.62 MB (52,033,785 bytes) | `2a0bc49da6c654497b94b5e2fe50c6a66c237ac4e5b10ae0b1f50a700085a3f0` |
| **`SpeedMeterPro-1.0.0-rc.1-win-x64.zip`** | Secondary Portable Archive | Self-Contained ZIP (275 files) | ~35.33 MB (37,050,570 bytes) | `1eea9a098e5f72a9958189d86ac14b9705e349141bcf0680f86411295d314bc5` |
| **`SpeedMeterPro-1.0.0-rc.1-win-x64-fdd.zip`** | Secondary Enterprise Archive | Framework-Dependent ZIP (9 files) | ~0.44 MB (464,161 bytes) | `01053a9fded144c65d92fa7bded5ef0956e3b95b65022fdb51bfe74a9250d6cd` |
| **`SpeedMeterPro-1.0.0-rc.1-sbom.spdx.json`** | Software Bill of Materials | SPDX 2.3 JSON Document | ~8.7 KB | `eac6299d2fb2c9c75f3a0937c44e99c15d4834789547dcf3a8904733cefece28` |
| **`SpeedMeterPro-1.0.0-rc.1-THIRD-PARTY-NOTICES.txt`** | Third-Party License Notices | UTF-8 Text Notice File | ~4.2 KB | `46270b2849e7b233a76e93895ec0503f56cf8ff162d00742f1f00889241ec7bb` |

### Package Inclusions & Architecture
1. **Primary Installer (`SpeedMeterPro-1.0.0-rc.1-Setup.exe`):**
   - Self-extracting single-file executable built from `src/SpeedMeter.Installer/SpeedMeter.Installer.csproj` with embedded self-contained payload.
   - Transactional staging (`.staging_<guid>`) and upgrade backup (`.backup_<guid>`) with atomic rollback on failure.
   - Two-step graceful shutdown handshake signaling `Local\SpeedMeterPro.Shutdown.<UserSid>` and `WM_CLOSE`, waiting up to 5s. Exits with 1618 (`ERROR_INSTALL_ALREADY_RUNNING`) if active instance cannot exit cleanly; zero ungraceful `Stop-Process -Force`.
   - Full command-line support for silent installations (`/S`, `/s`, `-Silent`, `/qn`).
2. **Application Assemblies & Runtimes:**
   - Host executable `SpeedMeter.App.exe` and assemblies (`SpeedMeter.Core.dll`, `SpeedMeter.Windows.dll`, `SpeedMeter.Storage.dll`, `SpeedMeter.Application.dll`).
   - Embedded .NET 10 LTS CoreCLR and WPF runtime components.
   - Native SQLite engine (`e_sqlite3.dll`).
3. **Shell & User Data Retention:**
   - Standard user `%LocalAppData%\Programs\SpeedMeterPro\` installation with 0 UAC prompts.
   - Start Menu shortcut and HKCU Uninstall registry keys.
   - Default uninstall preserves user historical databases at `%LocalAppData%\SpeedMeterPro\speedmeter.db`. Explicit `-PurgeUserData` clears all traces.

---

## 3. Empirical Deployment Benchmark Evidence (ADR-013)

Empirical evaluation conducted on Windows x64 using pinned .NET 10 SDK:

| Evaluation Dimension | Framework-Dependent (`win-x64`) | Self-Contained (`win-x64`) | Authoritative Release Decision |
|---|---|---|---|
| **Published File Count** | 9 files | 275 files | Self-Contained embedded in standalone Setup.exe for zero friction |
| **Uncompressed Footprint** | ~1.13 MB | ~106.84 MB | Fits comfortably within modern Windows desktop profiles |
| **Compressed Package Size** | ~0.44 MB | ~35.33 MB (ZIP) / ~49.62 MB (Setup.exe) | Rapid download across all standard connections |
| **Fresh Machine Execution** | **Failed without .NET 10 Desktop Runtime** | **Immediate Execution (Exit Code 0)** | Standard user does not need administrator rights or runtime downloads |
| **WPF Smoke Test Result** | 18/18 True (when runtime present) | 18/18 True (standalone out of the box) | Verified identical functional parity |
| **Cold Startup Latency** | ~1.4s | ~1.6s | +0.2s startup difference is negligible |
| **Working Set Memory** | ~155 MB | ~158 MB | Comparable memory footprint |

---

## 4. Automated Verification Suite Results

### 4.1 Standard Checks (`scripts/check.ps1`)
- **Total Evaluated:** 250
- **Passed:** 249
- **Skipped:** 1 (Intentional long-running fixture)
- **Failed:** 0
- **Exit Code:** 0

### 4.2 Windows Parity & Hardware Tests (`scripts/check.ps1 -Windows`)
- **Total Evaluated:** 258
- **Passed:** 257
- **Skipped:** 1
- **Failed:** 0
- **Exit Code:** 0
- **Key Windows Observations:**
  - `ReadSnapshot` latency: 0.769 ms average across 50 iterations via `GetIfTable2`.
  - `SampleOnce` latency: 0.935 ms average across 50 iterations.
  - Process Working Set: 169 MB; Handle count: 774; Thread count: 44.
  - Native WLAN BSSID correlation: Verified correct connected access point frequency/channel detection.

### 4.3 Native WPF Smoke Test (`scripts/smoke.ps1`)
- **Assertions Evaluated:** 18
- **Assertions Passed:** 18 / 18 (100% True)
- **Exit Code:** 0
- **Live UI Checks:**
  - ValidReadings: 7–8 live samples observed
  - LastState: Live
  - HasAdapter: True
  - SnapshotMatches / UnitsMatch: True
  - PausePreservesTotals / ResetWhilePaused / ResumeIsLive: True
  - ClickThroughBeforeShow / ClickThroughRecoveredViaTray: True
  - WidgetPausedTruthful / WidgetStatesTruthful: True
  - QuickPanelSmokePass / PrimaryNavDestinations: True
  - StorageBannerTruthful: True

---

## 5. Security, Update Trust & Durability Verification (ADR-012, ADR-014, ADR-015)

The following 16 verification fixtures executed cleanly in `V1Wave5ReleaseFixtures.cs`:

1. **W5-REL: Canonical version model matches across ProductIdentity and Assembly:**
   - `ProductIdentity.ProductVersion` = `1.0.0-rc.1`
   - `ProductIdentity.FileVersion` = `1.0.0.1`
   - `ProductIdentity.ReleaseChannel` = `rc`
   - `ProductIdentity.DatabaseSchemaVersion` = `3`
   - `ProductIdentity.CommitSha` dynamically derived from MSBuild/git.
2. **W5-REL: Update manifest parses valid payload and rejects malformed fields:**
   - Validates JSON structure, channel matching, and minimum supported version barriers.
3. **W5-REL: Tampered manifest properties fail cryptographic signature verification:**
   - Modified manifest payload fails RSA-SHA256 signature verification.
4. **W5-REL: Attacker supplied manifest with untrusted public key fails trust anchor validation:**
   - Untrusted manifest keys rejected against pinned RSA trust anchor.
5. **W5-REL: Tampered package payload with single byte alteration fails SHA-256 verification:**
   - Single-bit flipping in downloaded zip rejects package before staging.
6. **W5-REL: Package staging path rejects directory traversal and quarantine escapes:**
   - Relative paths (`../../`, `\..`, `/evil.exe`) throw `SecurityException`.
7. **W5-REL: Live-writer SQLite WAL snapshot preserves atomic consistency and rollback restores 100% fidelity:**
   - Snapshot taken using native `SqliteConnection.BackupDatabase` while background thread actively writes to WAL mode database.
   - Snapshot opened independently with 100% integrity; simulated migration corruption cleanly restored.
8. **W5-REL: Incompatible newer schema strictly blocks downgrade to protect data integrity:**
   - Opening schema version 4 with version 3 binary throws `IncompatibleSchemaDowngradeException`.
9. **W5-REL: Operational logger redacts IPs, MAC addresses, and user paths with bounded rotation:**
   - Private IPs replaced with `[REDACTED_IP]`.
   - MAC addresses replaced with `[REDACTED_MAC]`.
   - User paths replaced with `[REDACTED_USER]`.
   - Storage ceiling strictly bounded at 5 MB.
10. **W5-REL: Diagnostic bundle exports sanitized system telemetry with verified redaction preview:**
    - Exports sanitized system environment, logs, and metadata with live preview.
11. **W5-REL: Update trust service performs zero unsolicited network requests when disabled:**
    - Zero sockets or HTTP requests initiated when update checks are disabled.
12. **W5-REL: Authenticode publisher enforcement rejects unsigned, tampered, and publisher-mismatched binaries:**
    - Validates publisher subject name, Authenticode signature existence, and certificate trust chain.
13. **W5-REL: Whole-application shutdown bound enforces 3.0s maximum and reports non-clean diagnostics:**
    - Whole-application shutdown completes within 3.0 seconds ceiling (`DisposeWithTimeout`), recording non-blocking operational diagnostics if storage hangs.
14. **W5-REL: Installer graceful shutdown handshake coordinates clean exit and refuses destructive force-kill:**
    - Coordinates via `Local\SpeedMeterPro.Shutdown.<UserSid>` and `WM_CLOSE`, waits up to 5s, and throws `SpeedMeterRunningException` without force kill.
15. **W5-REL: Installer transactional staging rolls back atomically to previous version on interrupted upgrade:**
    - Simulates failed upgrade file swap; previous installation restored with 100% fidelity.
16. **W5-REL: Installer fresh install and upgrade preserve user data while uninstall purge cleans all traces:**
    - Fresh install places binaries; default uninstall preserves `%LocalAppData%\SpeedMeterPro\speedmeter.db`; uninstall with `-PurgeUserData` erases all traces.

---

## 6. Code Signing & Packaging Verification Drill

- **Dual Trust Signing:** Supported in `scripts/sign.ps1`:
  - `Production`: Enforces public root CA chaining and RFC 3161 timestamping; timestamp failure or untrusted root fails the build.
  - `Test`: Generates or uses local RC certificate with explicit `[TEST-SIGNED -- NOT FOR PUBLIC DISTRIBUTION]` warning.
- **Standalone Installer Drill:**
  - `SpeedMeterPro-1.0.0-rc.1-Setup.exe` executed silently (`-Silent -NoLaunch -Dir <sandbox>`).
  - Extracted binaries verified, Authenticode signatures validated.
  - Silent uninstallation drill executed cleanly (`Uninstall.exe -Uninstall -Silent -Dir <sandbox>`).

---

## 7. Gate E2 Post-RC Requirements Checklist

The following items are required before Gate E2 General Availability can be declared:
- [ ] 7-day continuous soak test on physical machine (evaluating working set stability, handle leaks, and GDI/User handle ceilings).
- [ ] 24-hour mixed network workload (sustained gigabit transfers, sleep/wake cycles, adapter hotplugging).
- [ ] Live Windows 11 physical test matrix (Windows 11 25H2, 26H2, 24H2) verifying taskbar docking and Per-Monitor V2 DPI transitions across multi-monitor setups.
- [ ] Code-signing certificate from trusted public commercial Certificate Authority (replacing local RC test certificate).
