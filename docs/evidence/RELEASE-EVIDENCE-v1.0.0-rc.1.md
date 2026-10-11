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
GATE E1: V1 RELEASE PACKAGE VERIFIED — RC FIELD VALIDATION AUTHORIZED
GATE E2: V1.0 PUBLIC RELEASE NOT VERIFIED — GA BLOCKED
         (Awaiting physical multi-monitor, live Windows 11 matrix,
          24-hour mixed workload, and 7-day soak evidence)
================================================================================
```

SpeedMeter Pro V1.0 has fulfilled all feature and release engineering requirements. Release candidate distribution packages have been compiled, signed, validated, packaged, and verified through automated end-to-end drills. Field validation is authorized. General Availability (GA) remains blocked until physical hardware multi-monitor and live Windows 11 soak tests are concluded.

---

## 2. Release Package Manifest & Checksums

All artifacts are generated in `artifacts/release/` and verified with SHA-256 hashes in `RELEASE-HASHES.sha256`:

| Artifact | Type | File Count | Uncompressed Size | Package Size (ZIP) | SHA-256 Checksum |
|---|---|---|---|---|---|
| **`SpeedMeterPro-1.0.0-rc.1-win-x64.zip`** | **Primary (Self-Contained)** | 489 | 182,644,801 bytes (~174.18 MB) | 75,294,461 bytes (~71.81 MB) | `40ade12bad316bf1682c8149ee8ebfe96a81ad217a5b0a9bd48e35ab274bab6f` |
| **`SpeedMeterPro-1.0.0-rc.1-win-x64-fdd.zip`** | Secondary (Framework-Dependent) | 21 | 3,379,976 bytes (~3.22 MB) | 1,678,751 bytes (~1.60 MB) | `fc415f605987fc82de9448b9f16fd131f4eca472672aa1b1469c7886cadb6a20` |
| **`sbom.spdx.json`** | SBOM (SPDX 2.3) | 1 | 8,766 bytes | N/A | `b8ba84c0df407a5898bc97f716162d6c9360b6a6dac31bcb84479a7c1e9c4879` |
| **`THIRD-PARTY-NOTICES.md`** | License Notices | 1 | 4,154 bytes | N/A | `450a68c05828a069cb577e19ed224219c3e971d5d541426b906ac13c34c59bc3` |

### Package Inclusions
The primary distribution directory and ZIP archive bundle:
1. `SpeedMeter.App.exe` (Signed application host executable)
2. Application assemblies: `SpeedMeter.Core.dll`, `SpeedMeter.Windows.dll`, `SpeedMeter.Storage.dll`, `SpeedMeter.Application.dll`
3. Embedded .NET 10 LTS CoreCLR and WPF runtime components
4. SQLite engine native binaries (`e_sqlite3.dll`)
5. `Install.ps1` (Standard-user installer into `%LocalAppData%\Programs\SpeedMeterPro`, zero UAC prompts)
6. `Uninstall.ps1` (Standard-user uninstaller with data retention / purge control)
7. `README.txt` (Getting started guidance)

---

## 3. Empirical Deployment Benchmark Evidence (ADR-013)

Empirical evaluation conducted on Windows x64 using pinned .NET 10 SDK:

| Evaluation Dimension | Framework-Dependent (`win-x64`) | Self-Contained (`win-x64`) | Authoritative Release Decision |
|---|---|---|---|
| **Published File Count** | 21 files | 489 files | Self-Contained selected for zero consumer friction |
| **Uncompressed Footprint** | ~3.22 MB | ~174.18 MB | Disk footprint fits comfortably within modern desktop profiles |
| **Compressed Package Size** | ~1.60 MB | ~71.81 MB | Within standard broadband download bandwidth |
| **Fresh Machine Execution** | **Failed without .NET 10 Desktop Runtime** | **Immediate Execution (Exit Code 0)** | Standard user does not need administrator rights or runtime downloads |
| **WPF Smoke Test Result** | 18/18 True (when runtime present) | 18/18 True (standalone out of the box) | Verified identical functional parity |
| **Cold Startup Latency** | ~1.4s | ~1.6s | +0.2s startup difference is negligible |
| **Working Set Memory** | ~155 MB | ~158 MB | Comparable memory footprint |

---

## 4. Automated Verification Suite Results

### 4.1 Standard Checks (`scripts/check.ps1`)
- **Total Evaluated:** 244
- **Passed:** 243
- **Skipped:** 1 (Intentional long-running fixture)
- **Failed:** 0
- **Exit Code:** 0

### 4.2 Windows Parity & Hardware Tests (`scripts/check.ps1 -Windows`)
- **Total Evaluated:** 252
- **Passed:** 251
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

## 5. Security & Update Trust Verification (ADR-014 & ADR-015)

The following verification fixtures executed cleanly in `V1Wave5ReleaseFixtures.cs`:

1. **W5-REL: Canonical version model matches across ProductIdentity and Assembly:**
   - `ProductIdentity.ProductVersion` = `1.0.0-rc.1`
   - `ProductIdentity.FileVersion` = `1.0.0.1`
   - `ProductIdentity.ReleaseChannel` = `rc`
   - `ProductIdentity.DatabaseSchemaVersion` = `3`
2. **W5-REL: Update manifest parses valid payload and rejects malformed fields:**
   - Validates JSON structure, channel matching, and minimum supported version barriers.
3. **W5-REL: Tampered manifest properties fail cryptographic signature verification:**
   - Modified manifest payload fails RSA-SHA256 signature verification.
4. **W5-REL: Tampered package payload with single byte alteration fails SHA-256 verification:**
   - Single-bit flipping in downloaded zip rejects package before staging.
5. **W5-REL: Package staging path rejects directory traversal and quarantine escapes:**
   - Relative paths (`../../`, `\..`, `/evil.exe`) throw `SecurityException`.
6. **W5-REL: Pre-migration snapshot preserves authentic database and rollback restores 100% data fidelity:**
   - SQLite snapshot created before simulated corrupted migration.
   - Rollback drill restores exact byte counters and health status.
7. **W5-REL: Incompatible newer schema strictly blocks downgrade to protect data integrity:**
   - Opening schema version 4 with version 3 binary throws `IncompatibleSchemaDowngradeException`.
8. **W5-REL: Operational logger redacts IPs, MAC addresses, and user paths with bounded rotation:**
   - Private IPs replaced with `[REDACTED_IP]`.
   - MAC addresses replaced with `[REDACTED_MAC]`.
   - User paths replaced with `[REDACTED_USER]`.
   - Storage ceiling strictly bounded at 5 MB.
9. **W5-REL: Diagnostic bundle exports sanitized system telemetry with verified redaction preview:**
   - Exports sanitized system environment, logs, and metadata with live preview.
10. **W5-REL: Update trust service performs zero unsolicited network requests when disabled:**
    - Zero sockets or HTTP requests initiated when update checks are disabled.

---

## 6. Code Signing & Packaging Verification Drill

- **Authenticode Signing:** All application binaries signed with SHA-256 Authenticode signatures and validated via `Get-AuthenticodeSignature`.
- **Extraction Drill:** Primary archive `SpeedMeterPro-1.0.0-rc.1-win-x64.zip` expanded into an isolated temporary directory:
  - Verified `SpeedMeter.App.exe` exists and retains valid signature.
  - Verified `Install.ps1`, `Uninstall.ps1`, and `README.txt` are present and executable.
  - Verified uninstall script properly respects `-PurgeUserData` switch to preserve or delete `%LocalAppData%\SpeedMeterPro`.

---

## 7. Gate E2 Post-RC Requirements Checklist

The following items are required before Gate E2 General Availability can be declared:
- [ ] 7-day continuous soak test on physical machine (evaluating working set stability, handle leaks, and GDI/User handle ceilings).
- [ ] 24-hour mixed network workload (sustained gigabit transfers, sleep/wake cycles, adapter hotplugging).
- [ ] Live Windows 11 physical test matrix (Windows 11 25H2, 26H2, 24H2) verifying taskbar docking and Per-Monitor V2 DPI transitions across multi-monitor setups.
- [ ] Code-signing certificate from trusted public commercial Certificate Authority (replacing local RC test certificate).
