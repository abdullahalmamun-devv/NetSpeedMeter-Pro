# Implementation status

**Current release:** 0.1.0 development prototype; no GA release.  
**Current milestone:** V1.0 Production Engineering — FOUNDATION WAVE 1 COMPLETE · INTEGRATION GATE A PASSED.  
**Updated:** 11 October 2026 (Asia/Dhaka).

## Build / test status

- **Build:** Succeeded with 0 warnings, 0 errors across 6 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 106/106 deterministic checks passing via `scripts/check.ps1 -Windows`:
  - 14 baseline arithmetic and boundary checks
  - 21 epoch, large counter, and delta discontinuity fixtures (including mandatory 125 MB / 10s = 12.5 MB/s and 100 Mbps)
  - 7 application collector lifecycle and resilience fixtures
  - 11 scope engine, transition, metric priority, and fail-closed aggregate fixtures
  - 6 taskbar companion geometry and fallback fixtures
  - 3 application attribution ProcessIdentity and NotMeasured capability fixtures
  - 12 independent audit regression fixtures in `AuditFixtures.cs` (throwing/slow observers, latest coalescing, callback dispose, typed degradation, defensive copy, and past snapshot immutability)
  - 10 Windows audit regression fixtures in `WindowsAuditFixtures.cs` (WS-01/02/03/07/10 fail-closed scope rules, duplicate identity rejection, and immutable membership)
  - 10 V1 Foundation Wave 1 fixtures in `V1FoundationFixtures.cs`:
    - `V1-SCOPE`: AutoPrimary follows route changes with clean rebaseline and preserved session bytes
    - `V1-SCOPE`: Route flapping debounce suppresses spurious revisions and commits after stabilization
    - `V1-SCOPE`: Dual-stack disjoint egress flags ambiguity and fails closed
    - `V1-STORE`: 8-byte big-endian BLOB codec roundtrips full 64-bit unsigned range losslessly
    - `V1-STORE`: CheckedIntegerMath enforces signed capacity guards and prevents overflow
    - `V1-STORE`: Daily rollup uniqueness enforces compound PRIMARY KEY without null duplicate bug
    - `V1-STORE`: Exact bytes, gap records, and clean session metadata survive process restart
    - `V1-LIFECYCLE`: Unclean shutdown is detected and recorded as a crash gap on restart
    - `V1-INTEGRATION`: Collector + RouteProvider + StorageEngine end-to-end persists exact interval bytes and checkpoints
    - `V1-RESILIENCE`: Storage degradation does not crash collector or drop live observations
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
  - Versioned migrations (`SqliteMigrations`) enforcing `STRICT` tables with foreign keys and compound primary keys. Resolved the nullable-network SQLite issue by inserting sentinel network profile `'unidentified'` with `network_profile_id TEXT NOT NULL`.
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
  - Live primary egress Wi-Fi interface resolved without external network probes.
  - Microbenchmark: ReadSnapshot avg 1.087 ms, SampleOnce avg 1.049 ms; process working set 40 MB. Soak / long-run validation pending.
  - Zero packet injection or external probes.
  - Live UI renders, pause excludes gaps, reset zeroes session, resume establishes live state.

## Integration Gate A Verdict

- **Verdict:** **PASSED / DECLARED STABLE**.
- Canonical persistent accounting stream is stable.
- Canonical scope routing with flapping protection is integrated and verified.
- SQLite storage engine with single writer, WAL, lossless ulong counter checkpoints, checked signed math, and compound PK uniqueness is integrated and verified.
- Unclean exit and restart recovery are verified.
- Storage failure resilience without collector crashes is verified.
- All 106 automated checks pass; UI smoke passes without regression.
