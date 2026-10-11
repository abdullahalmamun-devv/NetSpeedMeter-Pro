# SpeedMeter Pro 1.0.0-rc.1 Release Notes

**Release Candidate 1**  
**Date:** October 12, 2026  
**Target Platform:** Windows 10 / Windows 11 x64  
**Primary Distribution:** `SpeedMeterPro-1.0.0-rc.1-win-x64.zip` (Self-Contained)  

---

## 1. Overview

SpeedMeter Pro 1.0.0-rc.1 is the authoritative Release Candidate representing the completion of V1.0 engineering. Built from the requirements established in `Premium-Speed-Meter-PRD.md`, SpeedMeter Pro delivers enterprise-grade desktop network telemetry with zero cloud dependencies, zero mandatory accounts, and uncompromising local data sovereignty.

---

## 2. Key Capabilities in V1.0

### Real-Time Network Telemetry
- **Hardware-Accurate Octet Counters:** Queries Windows kernel IP Helper tables (`GetIfTable2`) for true 64-bit monotonic byte transfers.
- **Dynamic Routing & Scope Selection:**
  - *Automatic (Recommended):* Detects the primary default route adapter dynamically.
  - *Adapter Locking:* Pin monitoring to a specific physical or virtual network interface.
  - *All Adapters:* Sums physical interfaces without double-counting VPN tunnels.
- **Native Wi-Fi Intelligence:**
  - Direct Win32 Native WiFi API (`wlanapi.dll`) integration.
  - Radio frequency, channel, and band (2.4 GHz, 5 GHz, 6 GHz) strictly correlated to the connected Access Point BSSID.
  - RSSI signal percentage and WPA2/WPA3 authentication status.

### Multi-Surface User Experience
- **Floating Desktop Widget:** High-contrast live speed readout with drag-and-drop placement, Per-Monitor V2 DPI scaling, compact mode, and click-through passthrough.
- **Taskbar Companion:** Pins adjacent to the Windows taskbar notification area with automatic floating fallback resilience.
- **System Tray Integration:** Native tray icon with tooltip throughput meters, quick pause/resume controls, and widget toggles.
- **Full Dashboard & Analytics Window:**
  - *Overview:* Speedometer gauges and 24-hour summary.
  - *Live Traffic:* 60-second real-time ingress/egress waveform graphs.
  - *History:* Daily, weekly, monthly, and custom range analytics with local timezone alignment.
  - *Diagnostic Tools:* Integrated multi-attempt Ping (defaults to 10 iterations per PRD), DNS resolver, and opt-in Public IP discovery.
  - *Alerts & Quotas:* Bandwidth threshold warnings, latency alerts, connection drop alerts, and monthly quota tracking.

---

## 3. Wave 5 Release Engineering Highlights

### Packaging & Runtime Model
- **Self-Contained Deployment (`win-x64`):** Bundles the .NET 10 LTS CoreCLR runtime. Runs out of the box on standard Windows machines with zero prerequisite runtime installations.
- **Standard User Installation:** Installs into `%LocalAppData%\Programs\SpeedMeterPro\` with zero administrative UAC elevation prompts.
- **Full Uninstaller Control:**
  - *Default:* Removes binaries, shortcuts, and registry entries while preserving the historical database.
  - *Complete Purge (`-PurgeUserData`):* Erases all traces including local SQLite data.

### Update Security & Data Durability
- **Cryptographic Trust Pipeline:** Validates update manifests with RSA-SHA256 signatures and verifies package SHA-256 hashes and Authenticode signatures prior to staging.
- **Pre-Migration Snapshots & Rollback:** Automatically creates SQLite database snapshots before applying schema updates. If a migration is interrupted, the database is rolled back with 100% data fidelity.
- **Downgrade Rejection:** Prevents newer database schemas from being opened by older software versions to eliminate silent data corruption.
- **Bounded Operational Logging:** Caps rolling logs at 5 MB with 7-day retention, automatically redacting private IPv4 addresses, MAC addresses, and user directory paths.
- **Diagnostic Bundles:** Exports sanitized troubleshooting packages with live redaction preview.

---

## 4. Verification Evidence Summary

- **Standard Checks (`check.ps1`):** 243 passed, 1 skipped, 0 failed.
- **Windows Parity Checks (`check.ps1 -Windows`):** 251 passed, 1 skipped, 0 failed.
- **Native WPF Smoke Test (`smoke.ps1`):** 18/18 live UI assertions verified True (Exit code 0).
- **Package Integrity Drill:** Self-contained archive expansion, binary Authenticode signature verification, and installer script presence verified.

---

## 5. Release Gate Status

```text
================================================================
GATE E1: V1 RELEASE PACKAGE VERIFIED — RC FIELD VALIDATION AUTHORIZED
GATE E2: V1.0 PUBLIC RELEASE NOT VERIFIED — GA BLOCKED
         (Awaiting physical multi-monitor, live Windows 11 matrix,
          24-hour mixed workload, and 7-day soak evidence)
================================================================
```
