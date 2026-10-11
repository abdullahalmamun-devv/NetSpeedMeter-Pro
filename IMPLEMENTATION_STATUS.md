# Implementation status

**Current release:** 0.1.0 development prototype; no GA release.  
**Current milestone:** V1.0 Production Engineering — WAVE 2 (USAGE, BILLING, QUOTA, LIVE GRAPH & SETTINGS) COMPLETE · GATE B VERIFIED · PRODUCT SURFACE WAVE AUTHORIZED.  
**Updated:** 11 October 2026 (Asia/Dhaka).

## Build / test status

- **Build:** Succeeded with 0 warnings, 0 errors across 6 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 137/137 deterministic checks passing via `scripts/check.ps1 -Windows`:
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
  - 21 V1 Wave 2 fixtures in `V1Wave2Fixtures.cs`:
    - `W2-SPLIT`: IntegerSplitter partition conservation property across arbitrary boundaries ($\sum \text{allocated} \equiv \text{original}$)
    - `W2-SPLIT`: IntegerSplitter exact remainder allocation and boundary matching
    - `W2-SPLIT`: IntegerSplitter Int128 arithmetic eliminates 64-bit multiplication overflow
    - `W2-CALENDAR`: CalendarRangeResolver Today Yesterday Week Month boundaries
    - `W2-CALENDAR`: Billing cycle day 31 clamps in February without mutating requested cycle day
    - `W2-CALENDAR`: Billing cycle February leap year (29) vs common year (28) clamping
    - `W2-CALENDAR`: DST spring forward (23h) and fall back (25h) local day boundary resolution
    - `W2-CALENDAR`: Fixed-offset non-DST timezone (Asia/Dhaka) sanity resolution
    - `W2-HIST`: No double counting across adjacent half-open ranges (A + B == C)
    - `W2-HIST`: UsageSummary tracks observed and missing duration with typed CompletenessState
    - `W2-HIST`: History availability boundary distinguishes unrecorded history from missing gaps
    - `W2-HIST`: Keyset cursor pagination over intervals provides stable deterministic traversal
    - `W2-HIST`: Daily rollup generation is idempotent and reconciles exactly with raw intervals
    - `W2-QUOTA`: QuotaEvaluator calculates remaining bytes and overage beyond 100% accurately
    - `W2-QUOTA`: Opening balance improves effective quota calculation without inflating coverage
    - `W2-QUOTA`: Warning threshold crossing emits in deterministic order and handles threshold jumping
    - `W2-QUOTA`: Quota threshold deduplication persists in SQLite and survives storage restart
    - `W2-GRAPH`: LiveGraphBuffer memory is strictly bounded and downsampling preserves peak spikes
    - `W2-SETTINGS`: SettingsStorage persists atomically with versioned schema and sanitization
    - `W2-SETTINGS`: SettingsStorage corrupt file recovery creates backup and loads safe defaults
    - `W2-CONCURRENCY`: Active background Collector persistence runs alongside concurrent queries without stall
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 4 live Windows IP Helper, RouteProvider, and TaskbarCompanion checks
- **UI Smoke:** Real WPF shell and productionized floating widget smoke test passing via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`).

## Implemented verified paths

- **SpeedMeter.Core:**
  - Strict monotonic timing domain, 64-bit checked integer deltas, atomic RX/TX overflow handling, `CounterEpoch` and `MeasurementEpoch` boundaries, decimal rate and byte formatters (`MetricFormat`).
  - Extended `Reading` with `IntervalBytesRx`, `IntervalBytesTx`, and `IntervalDurationSeconds`.
  - Added `MeasurementEngine.RebaselineForScopeChange(string? newScopeId)` allowing scope switches to establish clean baselines with 0 throughput spikes while preserving prior cumulative session totals.
  - Added `IRouteProvider` abstraction and dual-stack route/metric metadata on `AdapterScopeCandidate`.
  - Added `TopologyState.DualStackDisjoint` check in `ScopePolicy` that fails closed without automatic aggregation when IPv4 and IPv6 egress paths diverge.
  - Implemented `ScopeStabilizer` with configurable debounce (3.0s default) to suppress route flapping and spurious revision increments.
  - **Wave 2 Additions:**
    - `HistoricalRange`: Readonly struct with canonical half-open `[StartUtcMs, EndUtcMs)` semantics, non-negative duration enforcement, overlap detection, and duration calculation.
    - `CalendarRangeResolver`: Timezone-aware local boundary resolution for Today, Yesterday, This Week, This Month, and Billing Cycle. Handles DST (23h/25h), non-DST (Asia/Dhaka), and dynamic billing day clamping (e.g. Feb 28/29) without mutating user preferences.
    - `IntegerSplitter`: Cumulative monotonic mapping $\text{CumulativeBytes}(t) = \lfloor \frac{(\text{Int128})B \times (t - t_{start})}{D} \rfloor$ guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) with 0 remainder loss. Native .NET 10 `Int128` arithmetic prevents 64-bit multiplication overflow.
    - `QuotaDomain`: `QuotaRule`, `QuotaDirectionMode`, `QuotaPeriodType`, `QuotaEvaluation`, and `QuotaEvaluator.Evaluate`. Supports warning threshold evaluation, threshold jumping, overage beyond 100%, and opening balance isolation from interval history.
    - `LiveGraphBuffer`: Pre-allocated circular ring buffer with fixed capacity (bounded memory, zero steady-state heap allocations) and peak-preserving downsampling.
    - `SettingsDomain`: `SpeedMeterSettings` schema version 1, value sanitization, and `SettingsStorage` providing atomic write (`.tmp` -> replace) and corruption resilience (`.corrupt.<ts>.json` backup + safe defaults).
- **SpeedMeter.Windows:**
  - `AdapterProvider` implementing `IAdapterCounterProvider` with typed `ReadSnapshot()` mapping (`Available`, `Unsupported`, `PermissionDenied`, `Failed`). Zero probes, zero elevated privileges.
  - `RouteProvider`: Native Win32 `GetBestInterface` egress resolution, managed dual-stack gateway inspection, and heuristic VPN/virtual adapter classification.
  - `TaskbarCompanion`: `SHAppBarMessage` geometry query, auto-hide detection, Explorer restart notification subscription, safe docked coordinate calculation, and automatic floating fallback.
  - `AppAttributionSpike`: `ProcessIdentity` (PID + StartTimeUtc) immune to PID recycling, `ApplicationIdentity` grouping, and security evaluation proving Kernel ETW elevation requirements.
- **SpeedMeter.Storage:**
  - Dedicated storage layer consuming `Microsoft.Data.Sqlite 9.0.2` and security-patched `SQLitePCLRaw.bundle_e_sqlite3 2.1.12` (resolving GHSA-2m69-gcr7-jv3q).
  - Single background writer over a bounded `Channel<IStorageCommand>`.
  - WAL mode, `PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;`.
  - `BigEndianCodec`: Exact 8-byte big-endian encoding and decoding for full 64-bit unsigned `ulong` provider counters.
  - `CheckedIntegerMath`: Guarded signed 64-bit conversion and checked application-side accumulation preventing SQL `SUM()` overflow.
  - Versioned migrations (`SqliteMigrations`): Schema version 1 (intervals, sessions, checkpoints, gaps) and Schema version 2 (`quota_threshold_events` with `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_percent)`).
  - **Wave 2 Query Methods:**
    - `GetHistoryAvailabilityAsync`: Identifies first recorded timestamp and distinguished unrecorded history from missing gaps.
    - `GetUsageSummaryAsync`: Computes exact byte usage, observed duration, missing gap duration, and typed `CompletenessState` across any `HistoricalRange` using `IntegerSplitter` in domain logic with 0 raw SQL `SUM()` calls.
    - `GetPagedIntervalsAsync`: Keyset cursor pagination over intervals providing stable deterministic traversal.
    - `ComputeDailyRollupsAsync`: Idempotent calculation of daily rollups with complete raw interval reconciliation.
    - `RecordQuotaThresholdEventAsync` and `GetRecordedThresholdsAsync`: Persistent threshold alert deduplication.
  - Graceful degradation: If storage fails or is closed, engine transitions to `StorageHealth.Degraded` without crashing the collector or dropping live sampling.
- **SpeedMeter.Application:**
  - Headless `Collector` with lifecycle states (`Starting`, `Running`, `Paused`, `Suspended`, `Degraded`, `Failed`, `Stopped`), injectable monotonic clock, lock-free consumer notification, and single latest snapshot bounding.
  - Integrated `IRouteProvider`, `ScopePolicy`, `ScopeStabilizer`, and `IStorageEngine`.
  - Dynamic `AutoPrimary` evaluation on sample loop.
  - Persists valid intervals and checkpoints; records explicit gap records for user pause, system sleep, and unclean shutdown.
- **SpeedMeter.App:**
  - Productionized Minimal floating widget (`WidgetWindow.cs`): 8 states, tabular numbers, keyboard nudging (accessible positioning), lock toggle, opacity levels, click-through with notification area tray recovery route.
  - WPF dashboard shell consuming immutable snapshots via lightweight `MonitorSession`, close-to-tray lifecycle, live chart, and pause/resume/reset controls.

## Validated on real Windows

- **Host Environment:** Microsoft Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors, .NET 10.0.12 runtime. Developer evidence only; not a release port authorization.
- **Observed:**
  - Native `MIB_IF_TABLE2` alignment (1352 bytes per row) matches managed IP statistics across 35 adapters.
  - RouteProvider observed default route candidate: Wi-Fi.
  - Storage persistence and migration 2 validated under concurrent collector writes and read queries.
  - 137 automated checks passing with exit code 0.
