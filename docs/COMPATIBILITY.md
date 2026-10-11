# SpeedMeter Pro Compatibility Matrix & Platform Policy

## 1. Executive Summary

SpeedMeter Pro is engineered as a high-fidelity, local-first Windows desktop network telemetry application. It interacts directly with native Windows networking and user interface subsystems:
- **IP Helper API (`iphlpapi.dll`):** `GetIfTable2` for monotonic 64-bit octet counters.
- **Native WiFi API (`wlanapi.dll`):** BSSID correlation, channel/band detection, and RSSI signal metrics.
- **Network List Manager (NLM COM API):** Canonical network profile names.
- **User32 / DWM Subsystems:** Per-Monitor V2 DPI awareness (`GetDpiForWindow`, `SetWindowPos`), global hotkeys (`RegisterHotKey`), and taskbar-aware companion placement.

---

## 2. Operating System Support Matrix

| Windows Release | Build / Version | Target Status | Validation Status | Notes |
|---|---|---|---|---|
| **Windows 11 25H2 / 26H2** | Builds 26100+ | **Primary Supported Target** | In-Progress (RC Field Validation) | Modern taskbar behavior, Mica/Acrylic visual parity, per-monitor DPI scaling. |
| **Windows 11 24H2** | Builds 26100.x | **Supported Target** | In-Progress (RC Field Validation) | Transitional GA target. |
| **Windows 10 22H2** | Build 19045.x | **Development & Secondary Host** | Verified on Host (Build 19045) | **Host Development Environment.** Automated CI and test fixtures execute on 19045. *Note: Per project policy, Windows build 19045 evidence does not validate Windows 11 support or authorize an unapproved product port.* |
| **Windows 10 (Earlier)** | < Build 19041 | **Unsupported** | Untested | Lacks modern Per-Monitor V2 and WinRT/COM integration guarantees. |
| **Windows Server 2022/2025** | Server Core / Desktop | **Uncertified** | Untested | Server builds lack WLAN API services by default; not targeted for V1. |

---

## 3. Architecture & Hardware Requirements

- **Architecture:** `x64` (AMD64 / Intel 64-bit) is the sole supported native architecture in V1.0.
  - *ARM64:* Runs via Windows 11 Prism x64 emulation, but native ARM64 compilation is deferred to post-V1.
  - *x86 (32-bit):* Explicitly unsupported.
- **Memory (RAM):**
  - Minimum: 250 MB free RAM.
  - Typical Working Set: ~150–160 MB (Release self-contained CoreCLR).
- **Disk Footprint:**
  - Self-Contained Installation: ~175 MB disk space in `%LocalAppData%\Programs\SpeedMeterPro`.
  - Historical Database: ~5 MB base database, scaling ~1 MB per month under typical 1-second retention.
- **Display & Graphics:**
  - Standard display adapter with DirectX 9+ / Direct3D acceleration (WPF hardware rendering pipeline).
  - Multi-monitor configurations with mixed DPI scaling (100%, 125%, 150%, 200%) are supported via Per-Monitor V2 DPI awareness.

---

## 4. Privilege & Security Requirements

- **Zero UAC Elevation:** SpeedMeter Pro runs strictly as a standard user (`asInvoker`). It never requests or requires administrative elevation (`requireAdministrator`).
- **No Background Kernel Drivers:** SpeedMeter Pro does not install NDIS filter drivers, WinPcap, or WinDivert. Packet capture is explicitly prohibited in V1; all telemetry is derived from official OS counter tables.
- **Filesystem Boundaries:**
  - Binaries: `%LocalAppData%\Programs\SpeedMeterPro\`
  - User Telemetry & Settings: `%LocalAppData%\SpeedMeterPro\`
- **Single-Instance Enforcement:** Scoped to the current Windows user session SID (`Global\SpeedMeterPro.V1.<UserSID>`). Multiple distinct Windows users logged into the same workstation run independent, isolated SpeedMeter Pro instances without mutex or database collision.

---

## 5. Network Interface Compatibility

SpeedMeter Pro automatically enumerates and categorizes network adapters:
- **Ethernet (802.3):** Full high-speed link rate detection and instantaneous byte counting.
- **Wi-Fi (802.11 a/b/g/n/ac/ax/be):**
  - Instantaneous byte counters via IP Helper.
  - Radio band (2.4 GHz, 5 GHz, 6 GHz) and channel strictly correlated with the connected BSSID via `WlanGetNetworkBssList`.
  - Signal strength percentage and authentication type (WPA2, WPA3, 802.1X).
- **VPN / Virtual Interfaces:**
  - Enumerated as distinct secondary interfaces.
  - **Zero Double-Counting Invariant:** VPN and physical adapter counters are never summed together, preventing artificial traffic inflation.
