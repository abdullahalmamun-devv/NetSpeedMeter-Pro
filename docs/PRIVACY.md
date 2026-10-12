# SpeedMeter Pro Privacy Policy & Data Architecture

**Effective Date:** October 12, 2026  
**Product Version:** 1.0.0-rc.2
**Architecture:** Local-First Windows Desktop Software  

---

## 1. Core Commitment: Local-First & Zero Tracking

SpeedMeter Pro is engineered with an absolute commitment to user privacy and data sovereignty.

- **No Remote Telemetry:** The application contains zero remote analytics, telemetry beacons, crash phone-home collectors, or behavioral tracking SDKs.
- **No Mandatory Accounts:** SpeedMeter Pro does not require an account, email address, registration key, cloud sign-in, or internet activation.
- **No Monetization of User Data:** The software does not collect, profile, aggregate, monetize, or sell user network traffic or system telemetry.

---

## 2. Network Data Collection & Processing

All network speed, byte volume, and adapter monitoring take place purely within the local operating system:

1. **Local Operating System Counter Tables:**
   - SpeedMeter Pro reads network byte counters directly from the Windows kernel IP Helper table (`GetIfTable2`).
   - The application does not inspect packet payloads, intercept HTTP requests, or decrypt network traffic.
   - Per-application deep packet inspection (DPI) is absent in V1.0.

2. **Wi-Fi and Interface Metadata:**
   - SpeedMeter Pro queries the Windows Native WiFi API (`wlanapi.dll`) to display SSID, signal strength, radio frequency, and channel.
   - This information is processed strictly in-memory to populate the user interface and is never transmitted off the local machine.

---

## 3. Remote Network Operations (Strict Opt-In)

The core monitoring engine runs completely offline. Optional features that can generate outbound network traffic remain under user control:

### A. Connectivity Probes (Internet Reachability & Ping)
- **Default State:** Disabled by default.
- **Opt-In Requirement:** Outbound probes execute only when the user explicitly enables connectivity monitoring in Settings.
- **Metered Connection Suppression:** When connected to a metered network (e.g. mobile hotspot), probes are automatically suspended to preserve data allowances.
- **Targets:** Probes check standard user-configurable public endpoints (e.g., standard DNS / HTTPS reachability checks). Responses are evaluated solely to classify network reachability (Reachable, Captive Portal, Local Only, Disconnected).

### B. Public IP Discovery Tool
- **User-Initiated:** Executes only when the user explicitly clicks "Check Public IP" in the Tools view.
- **Transparency:** The target endpoint is disclosed prior to query execution. The retrieved IP address is displayed locally in the UI and is never stored or transmitted elsewhere.

---

## 4. Local Data Storage & Retention

All historical usage data is stored on your local hard drive in an open SQLite database:

- **Location:** `%LocalAppData%\SpeedMeterPro\speedmeter.db`
- **Encryption & Access:** The database is protected by Windows standard NTFS access control lists (ACLs) restricted to your local Windows user account.
- **Retention Control:** Users can configure data retention periods (e.g., 30 days, 90 days, 1 year, or indefinite) or click "Clear History" in Settings to instantly purge all stored records.
- **Export & Import:** Users can export their complete configuration and historical usage to a standard local JSON file at any time.

---

## 5. Operational Logs & Sanitization

To assist with local troubleshooting, SpeedMeter Pro maintains a bounded rolling log:

- **Location:** `%LocalAppData%\SpeedMeterPro\Logs\`
- **Automatic Redaction:** Operational logs automatically redact sensitive identifiers:
  - Private IPv4 addresses are replaced with `[REDACTED_IP]`.
  - Hardware MAC addresses are replaced with `[REDACTED_MAC]`.
  - Windows user account file paths are sanitized to `[REDACTED_USER]`.
- **Bounded Storage:** Log files are capped at 5 MB total storage and purged after 7 days.
- **Diagnostic Bundles:** If you generate a diagnostic support bundle via Settings, SpeedMeter Pro displays a live redaction preview before exporting a local ZIP archive. Bundles are saved to your Desktop or selected folder for your inspection; they are never uploaded automatically.

---

## 6. Software Updates

SpeedMeter Pro checks for updates only when explicitly configured by the user:

- **Automatic checks:** Off until the user knowingly enables them in first run or Settings. When enabled, checks use a GitHub-hosted HTTPS manifest at most once per 24 hours after a successful check. GitHub receives ordinary request metadata such as source IP and user agent; no usage history is sent.
- **Manual checks:** Pressing **Check for updates** makes one explicit request regardless of the automatic setting.
- **Downloads:** The installer is downloaded only after the user chooses **Update now**. There is no unattended download or installation.
- **Manifest Integrity:** Update manifests are cryptographically signed using RSA-SHA256. Unsigned or tampered manifests are rejected.
- **Authenticode Verification:** All downloaded binaries are verified against the authoritative code-signing certificate and SHA-256 package checksum before extraction.
- **Zero Silent Installation:** Updates are never executed silently without user confirmation.

Production update manifest signing material and production Authenticode signing are external release dependencies for RC.2. A test-signed field/lab package is not a publicly trusted GA update.

## 7. Local crash records

Fatal failures may create bounded, redacted diagnostics under `%LocalAppData%\SpeedMeterPro\crashes\`. The next launch may offer to view or save a report. **Report issue** opens a GitHub issue page in your browser with limited version/build metadata; no diagnostic file is uploaded automatically. Saving a report works without GitHub.

---

## 8. Data Removal & Uninstallation

SpeedMeter Pro provides uninstallation through Windows **Settings → Apps → Installed apps**:

- **Default Uninstall:** Removes application binaries, shortcuts, and registry keys while preserving your historical database in `%LocalAppData%\SpeedMeterPro\` so that your data is not lost if you choose to reinstall later.
- **Complete Purge:** An explicit choice and confirmation permanently delete local data, including the historical SQLite database and crash records.
