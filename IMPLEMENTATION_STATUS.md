# Implementation status

**Current release:** 0.1.0 development prototype; no GA release.  
**Current milestone:** V1.0 Production Engineering — INDEPENDENT GATE B FORENSIC AUDIT COMPLETE · GATE B VERIFIED AFTER REPAIRS · WAVE 3 AUTHORIZED.  
**Updated:** 11 October 2026 (Asia/Dhaka).

## Build / test status

- **Build:** Succeeded with 0 warnings, 0 errors across 6 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 148 passed, 1 skipped (explicit `SkipCheckException` test), 0 failed via `scripts/check.ps1 -Windows`:
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
  - 28 V1 Wave 2 & Gate B audit fixtures in `V1Wave2Fixtures.cs`:
    - `W2-SPLIT`: IntegerSplitter partition conservation property across arbitrary boundaries ($\sum \text{allocated} \equiv \text{original}$)
    - `W2-SPLIT`: IntegerSplitter exact remainder allocation and boundary matching
    - `W2-SPLIT`: IntegerSplitter Int128 arithmetic eliminates 64-bit multiplication overflow
    - `W2-SPLIT`: IntegerSplitter seeded randomized property testing across 10,000 cases (remedies DEF-B05/conservation)
    - `W2-CALENDAR`: CalendarRangeResolver Today Yesterday Week Month boundaries
    - `W2-CALENDAR`: Billing cycle day 31 clamps in February without mutating requested cycle day
    - `W2-CALENDAR`: Billing cycle February leap year (29) vs common year (28) clamping
    - `W2-CALENDAR`: DST spring forward (23h) and fall back (25h) local day boundary resolution
    - `W2-CALENDAR`: Fixed-offset non-DST timezone (Asia/Dhaka) sanity resolution
    - `W2-CALENDAR`: Dynamic non-one-hour DST resolution (Lord Howe Island 30m delta, remedies DEF-B01)
    - `W2-CALENDAR`: Missing environment timezone reports explicit SKIP instead of false pass (remedies DEF-B09)
    - `W2-HIST`: No double counting across adjacent half-open ranges (A + B == C)
    - `W2-HIST`: Strict boundary inequality excludes boundary-touching intervals (remedies DEF-B06)
    - `W2-HIST`: GetTotalBytesAsync allocates partial ranges using IntegerSplitter (remedies DEF-B05)
    - `W2-HIST`: Coverage denominator truthfully reflects unexplained missing spans (remedies DEF-B07)
    - `W2-HIST`: Temporal overlap between consecutive intervals flags StorageDegraded (remedies DEF-B07)
    - `W2-HIST`: History availability boundary distinguishes unrecorded history from missing gaps
    - `W2-HIST`: Keyset cursor pagination over intervals provides stable deterministic traversal
    - `W2-HIST`: Daily rollup generation preserves distinct scope and network dimensions (remedies DEF-B08)
    - `W2-QUOTA`: QuotaEvaluator calculates remaining bytes and overage beyond 100% accurately
    - `W2-QUOTA`: Disabled quota rule truthfully preserves recorded bytes without zeroing (remedies DEF-B03)
    - `W2-QUOTA`: Basis points validation, PRD defaults, and canonical billing cycle identity (remedies DEF-B04/B13)
    - `W2-QUOTA`: Opening balance improves effective quota calculation without inflating coverage
    - `W2-QUOTA`: Warning threshold crossing emits in deterministic order and handles threshold jumping
    - `W2-QUOTA`: Quota threshold deduplication persists in SQLite and survives storage restart
    - `W2-GRAPH`: LiveGraphBuffer memory is strictly bounded and downsampling preserves peak spikes
    - `W2-GRAPH`: LiveGraphBuffer rejects out-of-order points and resolves duplicate timestamps (remedies DEF-B12)
    - `W2-SETTINGS`: SettingsStorage persists atomically with versioned schema and sanitization
    - `W2-SETTINGS`: SettingsStorage rejects future schema version without modifying file (remedies DEF-B02)
    - `W2-SETTINGS`: SettingsStorage persists monitor identity and work area anchors (remedies DEF-B11)
    - `W2-SETTINGS`: SettingsStorage corrupt file recovery creates backup and loads safe defaults
    - `W2-SERVICES`: Application services (History, Quota, Settings, Graph) end-to-end integration (remedies DEF-B10)
    - `W2-CONCURRENCY`: Active background Collector persistence runs alongside concurrent queries without stall
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 4 live Windows IP Helper, RouteProvider, and TaskbarCompanion checks
- **UI Smoke:** Real WPF shell and productionized floating widget smoke test passing via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`).

## Implemented verified paths

- **SpeedMeter.Core:**
  - Strict monotonic timing domain, 64-bit checked integer deltas, atomic RX/TX overflow handling, `CounterEpoch` and `MeasurementEpoch` boundaries, decimal rate and byte formatters (`MetricFormat`).
  - `HistoricalRange`: Readonly struct with canonical half-open `[StartUtcMs, EndUtcMs)` semantics, non-negative duration enforcement, overlap detection, and duration calculation.
  - `CalendarRangeResolver`: Timezone-aware local boundary resolution for Today, Yesterday, This Week, This Month, and Billing Cycle. Dynamically derives transition deltas from `TimeZoneInfo.GetAdjustmentRules()` (verified on Lord Howe Island 30m delta). Provides `GetDayRange` as single source of truth for calendar days.
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
  - Headless `Collector` with lifecycle states, lock-free consumer notifications, AutoPrimary route selection, and debounced scope transitions.
- **SpeedMeter.Windows & App:**
  - Native Win32 `GetIfTable2` adapter enumeration (35 adapters observed).
  - RouteProvider dual-stack egress candidate enumeration.
  - TaskbarCompanion AppBar query and floating widget fallback.
  - Minimal floating widget with keyboard nudging, lock, opacity, click-through with tray recovery, and WPF shell.

## Validated on real Windows

- **Host Environment:** Microsoft Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors, .NET 10.0.12 runtime. Developer evidence only; not a release port authorization.
- **Observed:**
  - 148 passed checks, 1 skipped check, 0 failed checks. Exit code: 0.
  - UI smoke test passed with all state invariants verified. Exit code: 0.
