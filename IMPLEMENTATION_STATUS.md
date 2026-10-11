# Implementation status

**Current release:** 0.1.0 development prototype; no GA release.  
**Current milestone:** V1.0 Production Engineering — GATE C FULLY CLOSED · NEXT V1 WAVE AUTHORIZED.  
**Updated:** 11 October 2026 (Asia/Dhaka).

## Build / test status

- **Build:** Succeeded with 0 warnings, 0 errors across 6 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 165 passed, 1 skipped (explicit `SkipCheckException` test), 0 failed via `scripts/check.ps1 -Windows`:
  - 14 baseline arithmetic and boundary checks
  - 21 epoch, large counter, and delta discontinuity fixtures (including mandatory 125 MB / 10s = 12.5 MB/s and 100 Mbps)
  - 7 application collector lifecycle and resilience fixtures
  - 11 scope engine, transition, metric priority, and fail-closed aggregate fixtures
  - 6 taskbar companion geometry and fallback fixtures
  - 3 application attribution ProcessIdentity and NotMeasured capability fixtures
  - 12 independent audit regression fixtures in `AuditFixtures.cs` (throwing/slow observers, latest coalescing, callback dispose, typed degradation, defensive copy, and past snapshot immutability)
  - 10 Windows audit regression fixtures in `WindowsAuditFixtures.cs` (WS-01/02/03/07/10 fail-closed scope rules, duplicate identity rejection, and immutable membership)
  - 10 V1 Foundation Wave 1 fixtures in `V1FoundationFixtures.cs` (AutoPrimary, ScopeStabilizer, BLOB codecs, checked math, rollup uniqueness, crash gaps, degradation resilience)
  - 10 V1 Foundation Gate A forensic audit fixtures in `V1FoundationFixtures.cs` (zero overlap crash gaps, child process restart, FailFast recovery, degraded storage snapshot, queue saturation)
  - 28 V1 Wave 2 & Gate B audit fixtures in `V1Wave2Fixtures.cs` (IntegerSplitter Int128 conservation, calendar/billing cycle day 31 clamping, Lord Howe Island 30m DST, half-open boundaries, coverage denominator, quota basis points deduplication, bounded live graph buffer, atomic settings persistence)
  - 17 V1 Wave 3 & Gate C closure fixtures in `V1Wave3Fixtures.cs`:
    - `W3-EXPORT`: Keyset-paged CSV export with metadata disclosure and formula injection prevention (`=`, `+`, `-`, `@`, `\t`, `\r` prepended with `'`)
    - `W3-EXPORT`: JSON export schema validity, explicit nulls, and typed numerics
    - `W3-EXPORT`: Atomic file export leaves target untouched on failure or cancellation (`.tmp` atomic replacement)
    - `W3-HIST`: HistoryViewModel query cancellation token CTS prevents stale query race conditions
    - `W3-QUOTA`: Basis points formatting and bidirectional parsing with PRD defaults
    - `W3-QUOTA`: Billing cycle preview dates reflect month clamping without mutating requested cycle day
    - `W3-SETTINGS`: SettingsViewModel atomic persistence and sanitization round-trip
    - `W3-GRAPH`: Live graph downsampling preserves gap breaks without connecting lines across gaps
    - `W3-APPCONTEXT`: ApplicationContext end-to-end integration wires Collector to LiveGraph and Storage
    - `W3-QUICKPANEL`: QuickPanelWindow positioning clamps strictly within work area across physical and cursor anchors
    - `W3-PLACEMENT-MULTI-MONITOR`: PlacementHelper handles multi-monitor coordinates deterministically across 5 heterogeneous topologies (Cases A through E)
    - `W3-TRAY-REORDER-SEMANTIC`: Semantic tray item recovery operates independently of menu order (zero positional index reliance)
    - `W3-QUICKPANEL-NATIVE-HWND`: Native HWND placement conforms strictly to physical work area queried via `GetWindowRect`
    - `W3-TASKBAR-DPI-CONSISTENCY`: TaskbarCompanion scales DIPs to physical pixels without coordinate mixing
    - `W3-SETTINGS-SYNC`: Settings changes propagate dynamically to Collector.Interval
    - `W3-ARCH-BOUNDARY`: SpeedMeter.App does not reference SpeedMeter.Storage or Microsoft.Data.Sqlite
    - `W3-APPS-TRUTHFUL`: Apps page exposes truthful V1.5 gating without simulated processes
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 4 live Windows IP Helper, RouteProvider, and TaskbarCompanion checks
- **UI Smoke:** Real WPF shell, production navigation, Quick Panel, and floating widget smoke test passing via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`) with all 13 native assertions True.

## Implemented verified paths

- **SpeedMeter.Core:**
  - Strict monotonic timing domain, 64-bit checked integer deltas, atomic RX/TX overflow handling, `CounterEpoch` and `MeasurementEpoch` boundaries, decimal rate and byte formatters (`MetricFormat`).
  - `HistoricalRange`: Readonly struct with canonical half-open `[StartUtcMs, EndUtcMs)` semantics, non-negative duration enforcement, overlap detection, and duration calculation.
  - `UsageIntervalRecord`: Pure immutable domain record for interval usage accounting, isolating domain and presentation from storage mechanics.
  - `CalendarRangeResolver`: Timezone-aware local boundary resolution for Today, Yesterday, This Week, This Month, and Billing Cycle. Dynamically derives transition deltas from `TimeZoneInfo.GetAdjustmentRules()`.
  - `IntegerSplitter`: Cumulative monotonic mapping $\text{CumulativeBytes}(t) = \lfloor \frac{(\text{Int128})B \times (t - t_{start})}{D} \rfloor$ guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) with 0 remainder loss across 10,000 randomized property tests.
  - `QuotaDomain`: `QuotaRule`, `QuotaDirectionMode`, `QuotaPeriodType`, `QuotaEvaluation`, and `QuotaEvaluator.Evaluate`. Warning thresholds standardized to integer basis points (`int ThresholdBps`, where 8000 = 80.00%, 10000 = 100.00%). PRD §11.2 defaults (`[8000, 9000, 10000]`). Truthful non-zero recorded bytes when disabled. Canonical `BillingCycleIdentity.Create` and `CreateRevision`.
  - `LiveGraphBuffer`: Pre-allocated circular ring buffer with bounded memory. Monotonic timestamp order guards reject out-of-order points and resolve duplicate timestamps by sequence number. Peak-preserving downsampling.
  - `SettingsDomain`: `SpeedMeterSettings` schema version 1, value sanitization, monitor identity persistence (`WidgetMonitorDeviceName`, `WidgetMonitorBounds`), atomic write (`.tmp` -> replace), corrupt file recovery (`.corrupt.<ts>.json` backup + safe defaults), and strict rejection of future schema versions (`schemaVersion > CurrentSchemaVersion`).
- **SpeedMeter.Storage:**
  - Dedicated storage layer consuming `Microsoft.Data.Sqlite 9.0.2` and security-patched `SQLitePCLRaw.bundle_e_sqlite3 2.1.12`.
  - Single background writer over bounded `Channel`.
  - WAL mode, `PRAGMA synchronous = FULL; foreign_keys = ON; busy_timeout = 5000;`.
  - BigEndianCodec, CheckedIntegerMath.
  - Migration 2: Table `quota_threshold_events` with integer compound uniqueness `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`.
  - Strict inequality half-open range queries in `GetIntervalsAsync` and `GetPagedIntervalsAsync`.
  - Range-aware `GetTotalBytesAsync` using `IntegerSplitter`.
  - Truthful coverage denominator in `GetUsageSummaryAsync` accounting for unexplained holes. Consecutive interval temporal overlap detection flags `StorageDegraded`.
  - Idempotent `ComputeDailyRollupsAsync` preserving actual `(scope_id, network_profile_id)` dimensions.
- **SpeedMeter.Application:**
  - `IUsageHistoryService` / `UsageHistoryService`: Period summaries, custom ranges, paged intervals, and availability.
  - `IQuotaService` / `QuotaService`: Cycle quota evaluation, threshold event recording, and persistent alert deduplication.
  - `ISettingsService` / `SettingsService`: Centralized settings state, atomic disk persistence, and `SettingsChanged` event notifications.
  - `ILiveGraphService` / `LiveGraphService`: In-memory graph sampling and downsampled window snapshots.
  - `IExportService` / `ExportService`: Keyset-paged CSV export with metadata disclosure header comments and spreadsheet injection neutralization; valid JSON export with schema v1.0 and explicit nulls; atomic `.tmp` replacement.
  - `ApplicationContext`: Composition root wiring Collector, Storage, History, Quota, Settings, LiveGraph, and Export services. Storage-free constructor overload completely insulates UI from `SpeedMeter.Storage` assembly. Dynamic `SettingsChanged` syncs `Collector.Interval`.
  - Headless `Collector` with lifecycle states, lock-free consumer notifications, AutoPrimary route selection, and debounced scope transitions.
- **SpeedMeter.Windows & App:**
  - Design tokens (`Tokens.xaml`, `ThemeDark.xaml`) with directional download `#3B82F6` and upload `#14B8A6`. Tabular numerals across all metrics.
  - Complete MVVM presentation architecture: `MainViewModel`, `OverviewViewModel`, `LiveTrafficViewModel`, `HistoryViewModel`, `QuotaViewModel`, `SettingsViewModel`, `QuickPanelViewModel`, `NetworkToolsViewModel`, `AppsViewModel`. Strict presentation boundary: zero direct references to `SpeedMeter.Storage` or `Microsoft.Data.Sqlite` in `SpeedMeter.App.dll`.
  - Modern navigation shell (`MainWindow.xaml`) with 5 primary destinations (Overview, Live Traffic, Apps, History, Network Tools) + Settings/About, dynamic `StorageDegradedBanner`, and live settings synchronization.
  - `PlacementHelper`: Multi-monitor coordinate and per-monitor DPI DIP conversion using Win32 `MonitorFromPoint` and `GetDpiForMonitor`.
  - `QuickPanelWindow`: Compact popup window with multi-monitor work-area clamping, Escape auto-dismiss, and pause toggle.
  - Native Win32 `GetIfTable2` adapter enumeration (35 adapters observed).
  - RouteProvider dual-stack egress candidate enumeration.
  - TaskbarCompanion AppBar query and floating widget fallback.
  - Minimal floating widget with keyboard nudging, lock, opacity, click-through with typed tray recovery (`DisableClickThroughMenuItem`), and WPF shell.

## Validated on real Windows

- **Host Environment:** Microsoft Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors, .NET 10.0.12 runtime. Developer evidence only; not a release port authorization.
- **Observed:**
  - 159 passed checks, 1 skipped check, 0 failed checks. Exit code: 0.
  - UI smoke test passed with all 10 native assertions verified (`True`). Exit code: 0. Window geometry: 1160x780. Click-through tray recovery item index 4 verified.
