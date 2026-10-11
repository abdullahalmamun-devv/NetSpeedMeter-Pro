# ADR-012: Distribution Strategy & Packaging Architecture

## Status
Accepted (Wave 5 Release Engineering)

## Context
SpeedMeter Pro is a feature-complete Windows desktop application engineered strictly from `Premium-Speed-Meter-PRD.md`. To transition from local development to a public release candidate, we must define an authoritative distribution strategy that aligns with the product's runtime architecture, security requirements, and commercial decisions:
1. **Commercial Policy:** SpeedMeter Pro V1 and planned V2 core are free to users. No paid activation, license key, subscription, trial expiration, or account requirement is permitted.
2. **Privilege Model:** SpeedMeter Pro operates as a standard-user Windows desktop application. No administrative privileges or elevated helper services are required for core network monitoring.
3. **Platform Interop:** The application interacts directly with Win32 APIs:
   - IP Helper (`iphlpapi.dll` `GetIfTable2`) for octet counters.
   - Native Windows WLAN API (`wlanapi.dll`) for Wi-Fi telemetry and BSSID correlation.
   - Network List Manager (NLM) COM API for profile name discovery.
   - Per-monitor V2 DPI placement pipeline (`GetDpiForWindow`, `SetWindowPos`).
   - Global hotkeys (`RegisterHotKey`) and System Tray notifications.
   - User SID-scoped mutex (`Global\SpeedMeterPro.V1.<UserSID>`) and canonical database lock (`%LocalAppData%\SpeedMeterPro\speedmeter.owner.<UserSID>.lock`).
   - SQLite WAL database (`%LocalAppData%\SpeedMeterPro\speedmeter.db`).
4. **Lifecycle & Uninstall Expectations:** Users expect clean installation and explicit control during uninstallation (either retaining historical usage data for future reinstall or completely removing all application data).

## Evaluated Options

### Option A: Direct Signed Per-User Windows Installer (Selected)
- **Mechanism:** A standalone, signed per-user setup package that installs binaries into `%LocalAppData%\Programs\SpeedMeterPro`, creates per-user Start Menu shortcuts, and registers standard Windows uninstall entries in `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro`.
- **Pros:**
  - Installs and runs without requiring administrative privileges (zero UAC elevation prompts).
  - Clean filesystem transparency: application binaries in `%LocalAppData%\Programs\SpeedMeterPro`, persistent data in `%LocalAppData%\SpeedMeterPro`.
  - Zero virtualization or sandbox interference: full compatibility with Win32 APIs, native WLAN, NLM COM, system tray, and single-instance cross-session mutexes.
  - Granular uninstallation options: allows user to choose between "Remove application only (preserve data)" and "Remove application and all local data".
- **Cons:**
  - Requires dedicated code signing and update management infrastructure.

### Option B: MSIX / App Installer Package
- **Mechanism:** Modern Windows packaging framework with AppContainer / Centennial desktop bridge.
- **Pros:**
  - Built-in declarative install, update, and clean uninstall mechanics.
- **Cons:**
  - Filesystem and registry virtualization: MSIX redirects `%LocalAppData%` to package virtual folders (`AppData\Local\Packages\...`), making database location opaque to the user and complicating external data export/import workflows.
  - Startup registration and global hotkey handling within AppContainer can encounter edge-case permission restrictions on certain Windows builds.
  - Requires trusted enterprise certificate installation for sideloading on non-Store systems.

### Option C: Machine-Wide MSI / EXE Installer
- **Mechanism:** Traditional `C:\Program Files` installer.
- **Pros:**
  - Single binary installation for all users on a shared machine.
- **Cons:**
  - Requires UAC elevation for installation and updates, violating the standard-user principle.
  - Multi-user data isolation requires complex permissions and shared registry handling.
  - Machine-wide background services or helpers are explicitly prohibited in V1.

### Option D: Microsoft Store
- **Mechanism:** Ingestion into Microsoft Store via packaged desktop bridge.
- **Pros:**
  - High consumer discovery, automatic Microsoft Store signing, and seamless silent updates.
- **Cons:**
  - Store certification lifecycle and review delays can impede critical patch releases.
  - Store submission can be pursued as an additional distribution lane in Wave 5 / future release, but cannot be the sole distribution channel for direct desktop downloads.

### Option E: Portable ZIP Archive
- **Mechanism:** Distributing an uncompressed or zipped folder containing binaries.
- **Pros:**
  - No installer required.
- **Cons:**
  - Untestable data location clarity: users frequently run from Downloads, Temp, or Desktop, leading to database fragmentation.
  - Cannot reliably provide automatic Windows startup, Start Menu integration, or clean uninstall workflows.
  - Single-instance lock file semantics become unpredictable across multiple extracted folder copies.

## Decision
1. **Primary V1 Distribution:** Adopt **Option A: Signed Per-User Windows x64 Standalone Installer (`SpeedMeterPro-1.0.0-rc.1-Setup.exe`)**.
   - Destination: `%LocalAppData%\Programs\SpeedMeterPro\`
   - Data Directory: `%LocalAppData%\SpeedMeterPro\`
   - Privilege: Standard user (`asInvoker`), 0 administrative prompts.
   - Start Menu shortcut: `%AppData%\Microsoft\Windows\Start Menu\Programs\SpeedMeter Pro.lnk`
   - Uninstall Registration: `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro`
   - Transactional Staging: Extracts payload to `.staging_<guid>`, creates `.backup_<guid>` for existing files on upgrade, and rolls back atomically on failure.
   - Graceful Shutdown Handshake: Two-step handshake via `Local\SpeedMeterPro.Shutdown.<UserSid>` and `WM_CLOSE`, bounded by a 5-second deadline. Exits cleanly with code 1618 if running process cannot exit; zero ungraceful `Stop-Process -Force`.
2. **Secondary Distribution Formats:**
   - Portable self-contained archive: `SpeedMeterPro-1.0.0-rc.1-win-x64.zip`
   - Framework-dependent archive: `SpeedMeterPro-1.0.0-rc.1-win-x64-fdd.zip`
3. **Uninstall Policy:** Provide two explicit uninstallation modes:
   - **Mode 1 (Default): Remove application only:** Removes binaries, Start Menu shortcuts, and registry keys. Preserves `%LocalAppData%\SpeedMeterPro\` so historical network usage and settings survive reinstallations.
   - **Mode 2: Remove application and all local data (`-PurgeUserData`):** Prompts user explicitly and removes all binaries, shortcuts, registry keys, and the entire `%LocalAppData%\SpeedMeterPro\` directory.
4. **Future Store Lane:** Packaging configuration is kept modular so an MSIX package for the Microsoft Store can be produced from the same publish output without modifying application architecture.

## Consequences
- No administrative privileges are required to install, use, update, or uninstall SpeedMeter Pro.
- Database and lockfile ownership remain deterministic and transparent under `%LocalAppData%\SpeedMeterPro\`.
- All native Win32 platform capabilities (WLAN BSSID queries, IP Helper, NLM COM, system tray, PerMonitorV2 placement) function without filesystem or virtualization restrictions.
- Zero risk of SQLite WAL or settings corruption during setup/upgrade due to elimination of forced termination.
