# SpeedMeter Pro Known Issues & Technical Limitations

**Version:** 1.0.0-rc.2 (under verification)
**Status:** Consumer experience closure review; Gate E2 not authorized

This document provides a transparent, authoritative accounting of known limitations, design boundaries, and platform behaviors in SpeedMeter Pro V1.0.

## RC.2 release dependencies

- The public GitHub Pages RC manifest and versioned GitHub Release asset have not yet been published or verified live.
- The embedded production update-manifest public key is provisional (2048-bit RSA). External production private-key provisioning and independently reviewed public-key replacement are pending. No production private key is stored in this repository.
- A trusted public Authenticode certificate is pending. Any local test-signed RC.2 installer is a field/lab artifact, not GA.
- Physical RC.1 → RC.2 upgrade/restart, full uninstall/reinstall/purge, and Windows 11 field validation require observed evidence before the corresponding claim.
- Automatic update checks require user consent. User-initiated downloads are not yet automatically suppressed on metered networks; users should choose when to download.

---

## 1. Scope & Feature Boundaries

### 1.1 Per-Application Bandwidth Attribution (Deferred to V1.5)
- **Status:** By Design / Gated.
- **Description:** SpeedMeter Pro V1.0 measures network traffic at the network adapter and operating system interface level. The **Apps** tab in the main window is informational and explicitly gated for V1.5.
- **Rationale:** Standard Win32 IP Helper APIs (`GetIfTable2`) do not expose per-process socket statistics without Event Tracing for Windows (ETW) kernel network sessions or elevated filter drivers. To maintain our strict rule against simulated mock data and zero-elevation design, per-app breakdown will be introduced in V1.5 via standard-user ETW providers.

### 1.2 Packet Capture & Payload Inspection
- **Status:** Explicit Non-Goal.
- **Description:** SpeedMeter Pro does not perform deep packet inspection (DPI), packet dumping, or payload parsing. It relies entirely on official OS counter tables.

---

## 2. Platform & Environment Limitations

### 2.1 Windows 11 vs Windows 10 Validation Scope
- **Status:** Gate E2 GA Blocker.
- **Description:** Automated test suites and developer verification have been executed on Windows 10 x64 Build 19045. Per project policy (`AGENTS.md`), evidence from Build 19045 does not authorize Windows 11 General Availability without dedicated live Windows 11 matrix testing.
- **Impact:** Developer observations on Windows 10 build 19045 do not establish Windows 11 support or authorize a Windows 10 product port. Full GA signoff is deferred until physical Windows 11 validation is completed.

### 2.2 Taskbar Docked Companion Geometry on Third-Party Shells
- **Status:** Handled via Safe Fallback.
- **Description:** On systems utilizing non-standard third-party desktop shells (e.g., StartAllBack, RetroBar, or custom virtual desktop switchers), the Win32 `Shell_TrayWnd` bounding rectangle may fail to return canonical geometry.
- **Resolution:** When taskbar query fails, SpeedMeter Pro automatically falls back to safe floating placement on the primary display rather than crashing or clipping off-screen.

### 2.3 Wi-Fi 6 GHz Band Detection on Legacy Drivers
- **Status:** Handled via Truthful "Unavailable" State.
- **Description:** For Wi-Fi 6E / Wi-Fi 7 networks operating in the 6 GHz band, older Windows WLAN driver releases occasionally omit the `ulChCenterFrequency` field in the `WLAN_BSS_ENTRY` structure returned by `WlanGetNetworkBssList`.
- **Resolution:** SpeedMeter Pro strictly requires authentic BSSID correlation. If the frequency is missing or malformed, the radio band displays `Unavailable` rather than guessing.

---

## 3. Operational & Runtime Behaviors

### 3.1 Low-Resource Mode UI Throttling
- **Status:** By Design.
- **Description:** When the host machine enters Windows Battery Saver mode or high system load, SpeedMeter Pro automatically throttles UI redraw rates from 1 second to 2 seconds to minimize CPU and GPU overhead.
- **Accounting Guarantee:** Background byte counter delta calculations and SQLite persistence continue to record exact monotonic intervals without data loss.

### 3.2 Single-Instance Enforcement per Windows User SID
- **Status:** By Design.
- **Description:** Launching a second instance of SpeedMeter Pro within the same Windows user session brings the existing window to the foreground and terminates the secondary process.
- **Isolation:** Multiple distinct Windows user accounts logged in concurrently each run their own independent instance and maintain separate local databases.
