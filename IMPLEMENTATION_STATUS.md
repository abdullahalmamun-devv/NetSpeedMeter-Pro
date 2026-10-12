# Implementation status

**Current release:** 1.0.0-rc.3 is the latest packaged, test-signed field candidate, built from clean `ReleaseSourceSha` `1badfde9e3e79770195b63e843e506e79a6ebd3f`. It is not a public production release. RC.1/RC.2 evidence remains preserved. GA remains blocked.
**Current milestone:** Gate E1 passed. RC.2/RC.3 consumer experience and safety closure is **NOT VERIFIED**; Gate E2 field validation is blocked. The isolated RC.2 lab → RC.3 signed DevelopmentTest one-click update/restart/data-preservation drill succeeded, but independent security acceptance, native accessibility, Installed Apps/purge/reinstall, packaged crash, packet/process privacy, public hosting and production signing remain open. See `docs/evidence/CONSUMER-CLOSURE-RC3.md`.
**RC.4 work in progress:** Managed uninstall/purge safety, test fixtures, release scripts and a sentinelled controlled-crash harness are under integration. RC.4 is not yet a packaged candidate. The user authorized native testing in the current Windows profile with exact SpeedMeter-owned targets after verified source commit/push. No Gate E2 work has started.
**Updated:** 12 October 2026 (Asia/Dhaka).

## Build / test status

### RC.3 closure decision (12 October 2026)

Final source SHA is `1badfde9e3e79770195b63e843e506e79a6ebd3f`; the canonical test-signed Setup is 217,586,960 bytes, SHA-256 `36A1594E46404E6AD34458386B3B5673CBEE83FC70A6B83DCB59E72AAE39E9CC`. The source commits were pushed normally. The exact final-source lab update succeeded with 1,110 prior interval IDs and RX/TX counters retained, database integrity `ok`, and one RC.3 process relaunched. Final build: zero warnings/errors; standard runner 273 pass/1 skip/0 fail; Windows runner 281 pass/1 skip/0 fail; native smoke passed. NuGet vulnerability audit feed was available and the tested dependency graph reported no vulnerable packages. The former real-data fixture risk has indeterminate historical impact; guarded test roots now prevent intentional real-profile fixture purge. Independent security and accessibility acceptance remain open. Full details: `docs/evidence/CONSUMER-CLOSURE-RC3.md`.

### RC.2 consumer integration observations (12 October 2026)

- Clean-tree `scripts/release.ps1 -SignMode Test` completed without bypass flags from `b0e425f34ac3033ee36bf2da27ee9d339a9b3bed`. The canonical test-signed Setup.exe is 217,575,696 bytes, SHA-256 `5ac09394b27c2d6c9f7be2cf8b5b3961e059652d6874d0dc85c2d1a459c1eedf`. Its silent custom-directory install and uninstall returned exit code 0; installed ProductVersion embedded the source SHA. The signing root is untrusted and the artifact is not for public distribution.
- `scripts/build.ps1`: initial restore failed `NU1900` because the NuGet vulnerability feed was unreachable. With `NuGetAudit=false` for this offline local run, the full solution built with zero warnings/errors. This does not constitute a fresh package-vulnerability audit.
- `scripts/check.ps1`: **268 pass, 1 intentional skip, 0 fail** with normal user access after installer fixtures were isolated from real account data and shell entries.
- `scripts/check.ps1 -Windows`: **276 pass, 1 intentional skip, 0 fail** on Windows build 19045. This remains developer-host evidence only.
- `scripts/smoke.ps1`: native WPF smoke completed with all expected state/navigation assertions True and live monitoring. Artifact: `artifacts/smoke-20261012-065004`.
- Independent Security and Accessibility source reviews requested further packaged-flow and native accessibility evidence. RC.1→RC.2 update/restart, public Pages manifest, production signing, and Windows 11 field behavior remain unverified.
- Independent reviews were refreshed after the package drill: source findings are addressed, while both decisions remain CHANGES REQUESTED because consumer end-to-end and native accessibility evidence are incomplete. Gate E2 work was not started.

### Prior RC.1 gate evidence

- **Build:** Succeeded with 0 warnings, 0 errors across 7 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Installer`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 250 passed, 1 intentional skip, 0 failed via `scripts/check.ps1`; 258 passed, 1 skip, 0 failed via `scripts/check.ps1 -Windows`. Total evaluated: 259. Groups include:
  - 14 baseline arithmetic and boundary checks
  - 21 epoch, large counter, and delta discontinuity fixtures (including mandatory 125 MB / 10s = 12.5 MB/s and 100 Mbps)
  - 7 application collector lifecycle and resilience fixtures
  - 11 scope engine, transition, metric priority, and fail-closed aggregate fixtures
  - 6 taskbar companion geometry and fallback fixtures
  - 3 application attribution ProcessIdentity and NotMeasured capability fixtures
  - 12 independent audit regression fixtures in `AuditFixtures.cs`
  - 10 Windows audit regression fixtures in `WindowsAuditFixtures.cs`
  - 10 V1 Foundation Wave 1 fixtures in `V1FoundationFixtures.cs`
  - 10 V1 Foundation Gate A forensic audit fixtures in `V1FoundationFixtures.cs`
  - 28 V1 Wave 2 & Gate B audit fixtures in `V1Wave2Fixtures.cs`
  - 21 V1 Wave 3 & Gate C closure fixtures in `V1Wave3Fixtures.cs`
  - 14 V1 Core Truth closure fixtures in `V1CoreTruthFixtures.cs`
  - 14 adversarial clock and shutdown fixtures in `V1ClockShutdownFixtures.cs`
  - 11 connectivity and reachability fixtures in `ConnectivityFixtures.cs`
  - 11 alert engine fixtures in `AlertFixtures.cs`
  - 21 network tools, privacy, and V1-Must closure fixtures in `NetworkToolsFixtures.cs`
  - 7 configuration export/import fixtures in `ConfigExportImportFixtures.cs`
  - 16 Wave 5 release engineering, update trust, and installer truth fixtures in `V1Wave5ReleaseFixtures.cs` (W5-REL: canonical version identity, manifest parsing/validation, tampered manifest RSA signature rejection, untrusted key rejection against pinned trust anchor, single-byte tampered payload SHA-256 rejection, staging path traversal prevention, live-writer SQLite WAL snapshot creation and rollback recovery drill with 100% data fidelity, incompatible schema downgrade prevention, bounded operational log rotation with IPv4/MAC/user path redaction, diagnostic bundle sanitized export with live redaction preview, zero-external-network update privacy invariant, Authenticode publisher enforcement, whole-application 3.0s shutdown bound, installer graceful shutdown handshake without destructive force-kill, installer transactional staging and upgrade rollback, installer fresh install and uninstall purge)
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 5 live Windows IP Helper, RouteProvider, TaskbarCompanion, and native WLAN BSSID correlation checks
- **UI Smoke:** Real WPF shell, production navigation across all 5 primary views individually, Quick Panel, and floating widget smoke passed via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`) with all 18 native assertions True.
- **Release Verification Drill:** Standalone installer `SpeedMeterPro-1.0.0-rc.1-Setup.exe` tested via silent installation and uninstallation drills into sandbox directory with verified Authenticode signature integrity.

## Implemented verified paths

- **SpeedMeter.Core:**
  - Strict monotonic timing domain, 64-bit checked integer deltas, atomic RX/TX overflow handling, `CounterEpoch` and `MeasurementEpoch` boundaries, decimal rate and byte formatters (`MetricFormat`).
  - `HistoricalRange`: Readonly struct with canonical half-open `[StartUtcMs, EndUtcMs)` semantics, non-negative duration enforcement, overlap detection, and duration calculation.
  - `UsageIntervalRecord`: Pure immutable domain record for interval usage accounting, isolating domain and presentation from storage mechanics.
  - `CalendarRangeResolver`: Timezone-aware local boundary resolution for Today, Yesterday, This Week, This Month, and Billing Cycle.
  - `IntegerSplitter`: Cumulative monotonic mapping guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) with 0 remainder loss.
  - `QuotaDomain`: `QuotaRule`, `QuotaDirectionMode`, `QuotaPeriodType`, `QuotaEvaluation`, and `QuotaEvaluator.Evaluate`. Integer basis points (`int ThresholdBps`). PRD §11.2 defaults (`[8000, 9000, 10000]`).
  - `LiveGraphBuffer`: Pre-allocated circular ring buffer with bounded memory and peak-preserving downsampling.
  - `SettingsDomain`: `SpeedMeterSettings` schema version 1, value sanitization, monitor identity persistence, atomic write, corrupt file recovery, and strict future schema rejection.
  - `ConnectivityDomain`: Independent 3-axis status model (`CollectorState`, `ConnectivityState`, `QualityState`), `EndpointRegistry` with typed approved HTTPS targets, probe result contracts, hysteresis rules, captive portal detection, and metered network policy.
  - `AlertDomain`: `AlertRule`, `AlertIncident`, `AlertSeverity`, cooldown enforcement, quiet hours popup suppression, and persistent deduplication ledger contracts.
  - `NetworkDetailsContracts`: `NetworkInterfaceDetails`, `GatewayDetails`, `DnsServerDetails`, and truthful link speed labeling contracts.
- **SpeedMeter.Storage:**
  - Dedicated storage layer consuming `Microsoft.Data.Sqlite 9.0.2` and security-patched `SQLitePCLRaw.bundle_e_sqlite3 2.1.12`.
  - Single background writer over bounded `Channel`, WAL mode, `PRAGMA synchronous = FULL; foreign_keys = ON; busy_timeout = 5000;`.
  - Migration 2: Table `quota_threshold_events` with integer compound uniqueness `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`.
  - Migration 3: Tables `connectivity_incidents` and `alert_incidents` with compound unique constraints for restart-durable incident deduplication and timeline history.
  - Strict inequality half-open range queries, range-aware `GetTotalBytesAsync`, and daily rollup computation.
- **SpeedMeter.Application:**
  - `IUsageHistoryService` / `UsageHistoryService`: Period summaries, custom ranges, paged intervals, availability, `PruneHistoryAsync`, and `ClearAllHistoryAsync`.
  - `IQuotaService` / `QuotaService`: Cycle quota evaluation, threshold event recording, and persistent alert deduplication.
  - `ISettingsService` / `SettingsService`: Centralized settings state, atomic disk persistence, and `SettingsChanged` event notifications.
  - `IConnectivityService` / `ConnectivityService`: Passive Windows link monitoring, optional active HTTPS reachability checks with hysteresis, metered suppression, and captive portal detection.
  - `INetworkToolsService` / `NetworkToolsService`: Passive network details, ping benchmark runner, DNS resolution, and privacy-guarded manual public IP lookup.
  - `IAlertService` / `AlertService`: Rule evaluation, incident deduplication, cooldown tracking, and quiet hours suppression.
  - `ConfigExportImportService`: Versioned JSON configuration export/import with atomic writing, schema compatibility validation, and privacy invariant preservation.
  - `ApplicationContext`: Composition root wiring Collector, Storage, History, Quota, Settings, LiveGraph, Connectivity, Tools, Alerts, and Export services.
- **SpeedMeter.Windows & App:**
  - Design tokens (`Tokens.xaml`, `ThemeDark.xaml`, `ThemeLight.xaml`) with directional colors. Tabular numerals across all metrics.
  - Full theme engine (`ThemeManager`) supporting Dark, Light, and System (Windows registry `AppsUseLightTheme` with fallback) via pack URIs.
  - Complete MVVM presentation architecture: `MainViewModel`, `OverviewViewModel`, `LiveTrafficViewModel`, `HistoryViewModel`, `QuotaViewModel`, `SettingsViewModel`, `QuickPanelViewModel`, `NetworkToolsViewModel`, `AppsViewModel`.
  - Floating widget with Minimal, Compact Graph (live sparkline), and Detailed layouts, idle dimming, fullscreen auto-hide, and tray click-through recovery.
  - Windows Startup helper (`StartupHelper`) via HKCU Run registry key.
  - Global hotkeys (`HotkeyHelper`) for Dashboard, Widget toggle, and Click-through recovery.
  - Transparent first-run privacy onboarding (`FirstRunWindow`).
  - Modern navigation shell with 5 primary destinations + Settings, dynamic storage and connectivity banners, and live settings synchronization.

## Validated on real Windows

- **Host Environment:** Microsoft Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors, .NET 10.0.12 runtime. Developer evidence only; not a release port authorization.
- **Observed:**
  - 233 passed checks, 1 intentional skip, 0 failed checks via `check.ps1`; 241 passed checks, 1 skip, 0 failed via `check.ps1 -Windows`. Exit code: 0.
  - Post-change UI smoke test passed with all 18 native assertions verified (`True`). Exit code: 0. Window geometry: 1160x780. Artifact: `artifacts/smoke-20261012-023350`.

## Clock and shutdown truth limits

- Production `MeasurementClock` uses .NET `Stopwatch`/Windows QPC for elapsed time, UTC for history placement, and Windows unbiased interrupt time to identify a missed suspend notification.
- Stable lifecycle gaps persist observed UTC endpoints and monotonic duration. A wall-clock jump produces a zero-width gap event plus a new clock segment. Affected calendar summaries report `ClockUncertain` and the UI suppresses the coverage percentage. Distinct valid readings can have repeated UTC labels across segments; exact bytes are preserved without falsely claiming globally disjoint UTC labels.
- Normal quit keeps a structured `CollectorShutdownResult`, traces failures, and does not assert clean durability without successful flush barriers and healthy storage.
- Controlled hardware sleep/hibernate, timezone-only reporting changes, system clock changes on a live Windows host, Windows 11, and full AC-M05/M08/S05 acceptance remain designated as `RELEASE-GATE PENDING` for Wave 5 (Release Engineering). Gate D is passed.

