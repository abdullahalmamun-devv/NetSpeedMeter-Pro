# SpeedMeter Pro User Guide & Documentation

Welcome to **SpeedMeter Pro**, a precision network telemetry and speed monitoring application designed for Windows 10 and Windows 11.

---

## 1. Quick Start

### Installation
1. Extract the release archive `SpeedMeterPro-1.0.0-rc.1-win-x64.zip`.
2. Right-click `Install.ps1` and select **Run with PowerShell** (or run `powershell -ExecutionPolicy Bypass -File .\Install.ps1` from a terminal).
3. The installer places the application into your local user programs directory (`%LocalAppData%\Programs\SpeedMeterPro`) and adds a Start Menu shortcut.
4. Launch **SpeedMeter Pro** from your Start Menu.

### First-Time Launch & Onboarding
Upon initial launch, SpeedMeter Pro opens the Welcome window with default safe settings:
- **Scope Mode:** `Automatic (Recommended)` — detects your primary internet-facing network adapter automatically.
- **Refresh Rate:** 1 second update interval.
- **Floating Widget:** Active on your primary monitor.

---

## 2. Interface Overview

### Main Application Window
Access the main window by double-clicking the system tray icon or selecting **Open SpeedMeter Pro** from the tray context menu:

- **Overview (Dashboard):** Live gauges for real-time Download and Upload speeds, connection state, current adapter name, and 24-hour traffic summary.
- **Live Traffic:** Real-time throughput graph displaying high-precision ingress and egress waveforms over 60-second sliding windows.
- **Apps (V1.5 Preview):** Displays informational status regarding per-application network attribution (scheduled for V1.5). V1.0 strictly avoids simulated or deceptive process data.
- **History:** Deep analytics across 24-Hour, 7-Day, 30-Day, and Custom date ranges. View exact byte totals, peak transfer rates, and session durations with automatic local timezone awareness.
- **Tools:**
  - *Ping Tool:* Measures round-trip time (RTT), jitter, and packet loss against any target host or IP (defaults to 10 ICMP echo requests).
  - *DNS Lookup:* Resolves IPv4 (A) and IPv6 (AAAA) records using native Windows DNS resolution.
  - *Public IP Lookup:* Optional external query that discloses external IPv4/IPv6 egress.
- **Settings:** Customize UI themes, widget docking, alert thresholds, data retention periods, and import/export profiles.

---

## 3. Desktop Widget & Taskbar Companion

### Floating Desktop Widget
- **Repositioning:** Click and drag the widget to any position on any monitor. The application remembers its exact coordinates across reboots and adjusts for monitor DPI scaling.
- **Click-Through Mode:** Enable "Click-Through" in Widget Settings or via the widget context menu. When enabled, mouse clicks pass directly through the widget to underlying windows. To restore normal interaction, right-click the SpeedMeter Pro system tray icon and uncheck "Click-Through".
- **Compact View:** Switch to compact view to minimize screen footprint while retaining legible rate readouts.

### Taskbar Companion
- In Settings, enable **Taskbar Docked Mode** to pin a compact live meter adjacent to the Windows taskbar notification area.
- *Fallback Resilience:* If the Windows taskbar geometry cannot be detected (e.g. during fullscreen games or virtual desktop transitions), the companion automatically enters a safe floating fallback state to prevent off-screen loss.

---

## 4. Measurement Scope & Network Adapters

SpeedMeter Pro provides three truthful measurement scopes in Settings > Network:

1. **Automatic (Recommended):** Uses the Windows routing table to dynamically identify your primary default gateway adapter. If you switch from Wi-Fi to Ethernet, SpeedMeter Pro seamlessly switches without restarting.
2. **Specific Adapter:** Locks measurement strictly to a single network interface (e.g. `Wi-Fi`, `Ethernet`, or a specific USB adapter).
3. **All Adapters:** Measures aggregate physical traffic.
   - *Zero Double-Counting Invariant:* VPN virtual adapters and tunnel interfaces are never summed with physical adapter traffic, ensuring your total bytes accurately reflect physical line usage.

---

## 5. Alerts & Quota Management

Configure automated notifications in **Settings > Alerts**:
- **Speed Threshold Alert:** Triggers a notification when bandwidth consumption exceeds your configured ceiling (e.g., 50 MB/s for 10 seconds).
- **Latency / High Ping Alert:** Alerts when mean ping round-trip times exceed your chosen tolerance.
- **Connection Disconnect Alert:** Instant notification when network reachability is lost.
- **Monthly Quota Warning:** Set a monthly data cap (e.g. 500 GB). SpeedMeter Pro tracks usage against your billing cycle billing day and alerts at 80%, 90%, and 100% thresholds.
- **Quiet Hours:** Define quiet hour windows to silence popups while preserving event records in the local alert timeline.

---

## 6. Data Management & Backup

- **Local Storage Location:** All usage statistics are stored in `%LocalAppData%\SpeedMeterPro\speedmeter.db`.
- **Exporting Configuration:** Click **Export Configuration** in Settings to save your rules, preferences, and thresholds to a portable `.json` file.
- **Importing Configuration:** Click **Import Configuration** to restore previously exported settings. SpeedMeter Pro validates the file structure and protects your privacy invariants.
- **Diagnostic Bundle:** If encountering an issue, click **Create Diagnostic Bundle** in Settings to export a sanitized troubleshooting package containing sanitized system information and logs.

---

## 7. Troubleshooting & FAQ

**Q: The widget disappeared off-screen after unplugging an external monitor.**  
**A:** Right-click the system tray icon and select **Reset Widget Position**. The widget will instantly snap to the center of your primary monitor.

**Q: Why does the Wi-Fi Band show "Unavailable"?**  
**A:** SpeedMeter Pro correlates radio band and channel strictly to the access point BSSID your adapter is currently associated with. If your adapter is in a transient state or the driver does not expose BSS list attributes, the app displays "Unavailable" rather than guessing.

**Q: How do I completely remove SpeedMeter Pro and all data?**  
**A:** Open PowerShell and run:
```powershell
powershell.exe -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\SpeedMeterPro\Uninstall.ps1" -PurgeUserData
```
This cleanly removes all application binaries, shortcuts, registry keys, and the historical SQLite database.
