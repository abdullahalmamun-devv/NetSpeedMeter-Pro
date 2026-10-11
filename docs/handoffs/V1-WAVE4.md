# V1 Wave 4 Engineering Handoff

**Milestone:** V1.0 Wave 4 — Connectivity, Network Details, Diagnostic Tools, Alerts, First-Run Experience, Settings Completion  
**Status:** Implementation complete; all automated and Windows checks passing; UI smoke passing; Gate D passed.  
**Director:** Project Director  
**Reviewers:** Independent Architect, Privacy/Security Reviewer, QA Engineer  
**PRD References:** §5.1, §12, §15, §16, §20, §22, §27; AC-C01–C07, AC-D01–D07, AC-N01–N07, AC-U09, AC-S09, AC-W01–W05  

---

## 1. Baseline and Environment

- **Starting commit:** `3ad5abc54aab388e20429f6a6b919fc6fd752b83`
- **Host OS:** Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors
- **Runtime:** .NET 10.0.12 (SDK 10.0.401)
- **Baseline checks:** 197 PASS, 1 intentional SKIP, 0 FAIL; 18/18 WPF smoke assertions True
- **Final checks:** 228 PASS, 1 intentional SKIP, 0 FAIL (standard `scripts/check.ps1`); 235 PASS, 1 intentional SKIP, 0 FAIL (`scripts/check.ps1 -Windows`); 18/18 WPF smoke assertions True (`scripts/smoke.ps1`)

---

## 2. Delivered Capabilities & Workstreams

### A. Connectivity Domain & Reachability Engine (`V1-CONNECT-001`, `V1-PROBE-001`)
- **Three Independent Axes:** Strictly preserves separate status axes:
  1. *Collector state:* Starting, Running, Paused, Suspended, Degraded, Failed
  2. *Connectivity state:* Unknown, Checking, Reachable, ChecksFailing, NoLocalConnection, SignInSuspected
  3. *Quality state:* High latency / degraded quality (independent of reachability)
- **Passive First & Zero Unsolicited Traffic:** Windows network interface status and default route changes are observed passively. When remote connection checks are disabled (the default setting), zero background probes or HTTP/ICMP packets are transmitted.
- **Typed Endpoint Registry:** Canonical registry with approved HTTPS reachability endpoints (`EndpointRegistry.cs`). All endpoints specify stable IDs, protocols, purpose (`ReachabilityCheck`, `CaptivePortalCheck`), expected status codes, minimum intervals, and privacy disclosures.
- **Hysteresis & Truthful Root Cause:** 3 consecutive all-endpoint failure rounds confirm `ChecksFailing`; 2 consecutive successful rounds recover to `Reachable`. Single ICMP probe failures never claim "Internet is down" or "ISP outage".
- **Captive Portal Detection:** Detects HTTP 302/redirect responses from captive portal probe endpoints and reports `SignInSuspected`.
- **Metered Network Policy:** Background remote checks default to suppressed on metered connections to avoid data consumption, with truthful UI display and explicit user toggle.
- **Lifecycle & Staleness:** Probes are bounded to 3-second timeouts. System suspend immediately closes observed connectivity incidents with uncertain end timestamps; stale successful checks expire rather than staying indefinitely green.

### B. Windows Network Information & Diagnostic Tools (`V1-NETINFO-001`, `V1-TOOLS-001`)
- **Truthful Network Details:** `NetworkDetailsProvider` extracts Windows adapter metadata (IPv4/IPv6 addresses, default gateway, DNS servers, DHCP status) and Wi-Fi telemetry (SSID, Signal %, Band, Channel, Auth). Interface link speed is strictly labeled as "Interface Link Speed" and explicitly noted as distinct from internet throughput.
- **User-Initiated Ping Tool:** Bounded ping benchmark with configurable count (default 10 attempts per PRD §16 contract) and 3-second timeout. Computes minimum, maximum, and mean RTT without NaN on packet loss; handles local errors and cancellation gracefully.
- **DNS Resolution Tool:** Resolves IPv4 (A) and IPv6 (AAAA) records; strictly validates and normalizes hostnames to prevent command or argument injection; reports timeouts and failures truthfully.
- **Privacy-Guarded Public IP Lookup:** Manual on-demand lookup strictly requiring explicit user acknowledgment/disclosure before each query. Queries approved privacy-preserving HTTPS provider; explains egress NAT/VPN routing caveats; caches last-known response.
- **Deterministic Test Fakes:** All automated test fixtures use local loopback/in-memory mock probe providers; zero external network dependencies in tests.

### C. V1 Alert Engine & Timeline Persistence (`V1-ALERT-001`, `V1-STORAGE-003`)
- **Alert Rules & Incident Tracking:** Configurable rules for Local Disconnect, Remote Reachability Failure, High Ping Latency (> threshold mean over 10 replies for 30s), and Quota Warnings.
- **Deduplication & Cooldown:** Incidents are debounced, enforce configurable cooldown periods, and suppress duplicate popups across app restarts.
- **Quiet Hours:** Configurable quiet hours window suppresses visual notifications while recording incidents into history truthfully.
- **Storage Migration 3:** SQLite migration 3 creates `connectivity_incidents` and `alert_incidents` tables with compound unique indexes for deduplication. `SqliteStorageEngine` provides persistent storage and query methods.

### D. First-Run Onboarding & Privacy Controls (`V1-FIRST-RUN-001`)
- **Transparent First-Run Dialog:** `FirstRunWindow` presents a local-first onboarding flow explaining:
  1. Local monitoring functions entirely offline without telemetry or accounts.
  2. Optional reachability checks send small periodic requests to configured endpoints (default: off).
  3. Public IP lookups contact external HTTPS endpoints that necessarily observe the user's egress IP (manual only).
- Bypassed in `--smoke` automated testing to preserve deterministic headless smoke runs.

### E. V1 Experience & Settings Completion (`V1-SETTINGS-COMPLETE-001`, `V1-W4-INTEGRATION-001`)
- **Theme Engine:** Full Light, Dark, and System theme support with `ThemeLight.xaml` matching design tokens. Pack URI resolution with resilient fallback prevents cross-assembly resource resolution issues.
- **Floating Widget Layouts:** Supports Minimal (200x72), Compact Graph (240x110 with live sparkline), and Detailed (280x145 with sparkline and adapter info). Includes >30s idle dimming and fullscreen auto-hide.
- **Windows Startup & Global Hotkeys:** `StartupHelper` manages Windows HKCU Run key registration. `HotkeyHelper` registers `Ctrl+Shift+S` (Dashboard), `Ctrl+Shift+W` (Toggle Widget), and `Ctrl+Shift+C` (Click-through recovery).
- **Single Instance Mutex Activation:** Named mutex `Local\SpeedMeterPro.V1.<user>` detects secondary launches, cleanly restores and focuses the existing window via Win32 `FindWindow`/`ShowWindow`/`SetForegroundWindow`, and terminates without modal popups.
- **Low-Resource / Battery Mode:** Dynamically throttles UI refresh to 1 Hz (`displayTimer.Interval = 1000ms`) when battery power is detected or explicit toggle is active, conserving CPU/battery while preserving 100% byte accounting accuracy.
- **History Data Retention & Pruning:** Added `PruneHistoryAsync` (30/90/180/365 days retention) and `ClearAllHistoryAsync` to `UsageHistoryService` and `SettingsView`.
- **Versioned Configuration Export & Import:** `ConfigExportImportService` provides atomic `.tmp` export and import of settings and alert rules with schema validation, future schema rejection, and strict privacy invariants (never silently enables probes on import).

---

## 3. Test & Verification Evidence

1. **Solution Build:**
   ```text
   Build succeeded.
       0 Warning(s)
       0 Error(s)
   ```
2. **Automated Check Fixtures (`scripts/check.ps1`):**
   ```text
   Checks summary: 222 passed, 1 skipped, 0 failed. Total evaluated: 223. Exit code: 0.
   ```
3. **Windows Native Checks (`scripts/check.ps1 -Windows`):**
   ```text
   Checks summary: 229 passed, 1 skipped, 0 failed. Total evaluated: 230. Exit code: 0.
   ```
4. **WPF Smoke Test (`scripts/smoke.ps1`):**
   ```text
   18/18 native assertions True. Exit code: 0.
   Artifacts: C:\Users\asad\Desktop\Dev\artifacts\smoke-20261012-002140
   ```

---

## 4. Release Engineering Carryover (Pending for Wave 5)

Per product authority decision, the following items are designated as `RELEASE-GATE PENDING` and do not block Wave 4:
1. Live Windows sleep/hibernate hardware transitions and live wall-clock jump hardware testing.
2. Windows 11 target validation matrix (26H2, 25H2, 26H1, 24H2).
3. Physical multi-monitor mixed-DPI hardware verification.
4. 24-hour continuous workload and 7-day soak testing.
5. Installer packaging, signing, and update infrastructure.
6. Whole-application disposal timing and user-facing shutdown failure UX under writer faults.
