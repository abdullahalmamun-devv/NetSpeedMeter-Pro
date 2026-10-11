# Implementation status

**Current release:** 0.1.0 development prototype; no GA release.  
**Current milestone:** V1.0 Wave 4 complete; Gate D formally passed. Ready for Wave 5: Release Engineering. The Wave 4 product-authority decision accepts repeated UTC labels across distinct recorded clock segments under the ADR-011 safeguards. Real sleep/clock/Windows 11 and whole-app shutdown evidence remain release-gate pending.
**Updated:** 12 October 2026 (Asia/Dhaka).

## Build / test status

- **Build:** Succeeded with 0 warnings, 0 errors across 6 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 233 passed, 1 intentional skip, 0 failed via `scripts/check.ps1`; 241 passed, 1 skip, 0 failed via `scripts/check.ps1 -Windows` with process-local `NuGetAudit=false`. Total evaluated: 242. Groups include:
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
  - 11 connectivity and reachability fixtures in `ConnectivityFixtures.cs` (AC-C01–C07: blocked ICMP with HTTPS, 3-round fail / 2-round recovery hysteresis, captive portal sign-in detection, sleep closure, metered suppression, proxy authentication, stale expiry, scope change cancellation, bounded concurrency)
  - 11 alert engine fixtures in `AlertFixtures.cs` (AC-N01–N07: local disconnect debounce, reachability multi-check evidence, high ping 30s mean, stale/paused suppression, restart deduplication, cooldown, quiet hours popup suppression, rule revision stability, quota event single consumption, disabled probe safety)
  - 21 network tools, privacy, and V1-Must closure fixtures in `NetworkToolsFixtures.cs` (AC-D01–D07: host normalization/anti-injection, A/AAAA DNS resolution, ping RTT min/max/mean without NaN, ping default 10 attempts per PRD contract, public IP disclosure requirement, HTTPS provider verification, egress NAT caveat, truthful link speed labeling, zero-traffic privacy invariant, native Windows WLAN API (wlanapi.dll) metadata correlation by adapter GUID and connected BSSID matching without ordering assumptions or frequency borrowing, NLM COM friendly profile name query, authentic storage migration v2->v3 fidelity, transaction-safe history pruning/clearing, single-instance per-user SID mutex and lockfile ownership with clean exit code 0, low-resource UI throttling)
  - 7 configuration export/import fixtures in `ConfigExportImportFixtures.cs` (AC-U09, AC-S09: atomic file export, round-trip fidelity, malformed rejection, future schema rejection, rule validation, privacy preservation invariant)
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 5 live Windows IP Helper, RouteProvider, TaskbarCompanion, and native WLAN BSSID correlation checks
- **UI Smoke:** Real WPF shell, production navigation across all 5 primary views individually, Quick Panel, and floating widget smoke passed via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`) with all 18 native assertions True (`artifacts/smoke-20261012-023350`).

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

