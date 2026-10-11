# SpeedMeter Pro — V1 Foundation Gate A Verification Matrix

**Gate:** V1 Foundation Gate A (Accounting, Scope, Storage, Crash & Durability)  
**Date:** 2026-10-11  
**Decision:** **GATE A VERIFIED AFTER REPAIRS — WAVE 2 AUTHORIZED**  
**Evidence Baseline:** Windows 10 x64 (Build 19045), .NET 10.0.12, 116 Checks Passed, UI Smoke Passed  

---

## 1. Traceability & Verification Matrix

| Claim / Requirement | PRD Ref | Primary Implementation | Verification Fixture | Observed Evidence | Verdict |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Monotonic Accounting & Integer Deltas** | §20, §22 | `SpeedMeter.Core.MeasurementEngine` | `AC-M01`, `AC-M02`, `AC-M03`, `AC-M08` | Monotonic clock elapsed seconds used; counter resets handled via Discontinuity; wall-clock shifts do not affect rate. | **PASS** |
| **Signed Integer Capacity Guard** | §22 | `SpeedMeter.Storage.CheckedIntegerMath` | `V1-STORE: CheckedIntegerMath enforces signed capacity guards` | Throws `StorageCapacityExceededException` when value exceeds $2^{63}-1$. Prevents silent database overflow. | **PASS** |
| **8-Byte Big-Endian BLOB Codec** | §22 | `SpeedMeter.Storage.BigEndianCodec` | `V1-STORE: 8-byte big-endian BLOB codec roundtrips full 64-bit unsigned range` | Roundtrips $0, 1, 2^{63}-1, 2^{63}, 2^{64}-1$ losslessly. Rejects byte arrays of invalid length. | **PASS** |
| **Application-Side Checked Summation** | §22 | `SpeedMeter.Storage.SqliteStorageEngine` | `GetTotalBytesAsync` | Zero SQL `SUM()` queries. Ingested bytes aggregated via `CheckedIntegerMath.CheckedAdd`. | **PASS** |
| **Strict Relational Normalization** | §22 | `SpeedMeter.Storage.SqliteMigrations` | `V1-STORE: Daily rollup uniqueness enforces compound PRIMARY KEY` | 7 `STRICT` tables; foreign keys enforced; compound primary key on daily rollups prevents null duplicate bug. | **PASS** |
| **AutoPrimary Route Following** | §13 | `SpeedMeter.Application.Collector`, `SpeedMeter.Core.ScopePolicy` | `V1-SCOPE: AutoPrimary follows route changes with clean rebaseline` | Switching primary default route triggers new ScopeRevision, rebaselines MeasurementEngine without speed spikes. | **PASS** |
| **Route Flapping Debounce** | §13 | `SpeedMeter.Core.ScopeStabilizer` | `V1-SCOPE: Route flapping debounce suppresses spurious revisions` | 2-second debounce filter blocks rapid transient routing switches; commits revision only after link stabilization. | **PASS** |
| **Dual-Stack Disjoint Fail-Closed** | §13 | `SpeedMeter.Core.ScopePolicy` | `V1-SCOPE: Dual-stack disjoint egress flags ambiguity and fails closed` | Conflicting IPv4 and IPv6 egress routes set `IsAmbiguous = true` and clear active adapter IDs. Never blindly merges. | **PASS** |
| **Zero-Overlap Crash Gap Invariant (DEF-01)** | §20, §22 | `SpeedMeter.Application.Collector`, `SpeedMeter.Storage.SqliteStorageEngine` | `V1-AUDIT: Crash gap start matches last durable interval end with zero history overlap` | Invariant query `SELECT COUNT(*) FROM usage_intervals i JOIN usage_intervals g ... WHERE overlap` returns 0. | **PASS** |
| **Empty Session Crash Gap Fallback** | §20, §22 | `SpeedMeter.Application.Collector` | `V1-AUDIT: Empty crashed session falls back to session start for crash gap` | Sessions crashing prior to any persisted interval recover full span from `StartedAtUtcMs` to new session start. | **PASS** |
| **Future Schema Incompatibility Rejection (DEF-03)** | §22 | `SpeedMeter.Storage.SqliteMigrations` | `V1-AUDIT: Incompatible future schema version is rejected without mutating database` | Schema with `user_version = 999` detected; throws `IncompatibleSchemaException`; health set to `Degraded`; DB untouched. | **PASS** |
| **Atomic Interval & Checkpoint Ingestion (DEF-05)** | §22 | `SpeedMeter.Storage.SqliteStorageEngine` | `V1-AUDIT: Interval and counter checkpoint committed atomically in single transaction` | Unified `IntervalCmd` writes interval record and counter checkpoint inside the exact same `SqliteTransaction`. | **PASS** |
| **Duplicate Interval Idempotence** | §22 | `SpeedMeter.Storage.SqliteStorageEngine` | `V1-AUDIT: Duplicate interval ID is rejected idempotently by primary key constraint` | Re-inserting identical `interval_id` fails cleanly without corrupting transaction state or creating duplicates. | **PASS** |
| **Out-of-Process Durability Across Restart** | §20, §22 | `SpeedMeter.Checks.V1FoundationFixtures` | `V1-AUDIT: Child process out-of-process restart preserves exact bytes and metadata` | Child process writes and exits cleanly. Parent process reads exact session metadata, intervals, and byte totals. | **PASS** |
| **Forced Process Crash Recovery (DEF-01)** | §20, §22 | `SpeedMeter.Checks.V1FoundationFixtures` | `V1-AUDIT: Forced FailFast process crash recovers accurate crash gap without overlap` | Child process crashes via `Process.Kill()`. Parent recovers crash gap starting at last committed interval with 0 overlap. | **PASS** |
| **Truthful Degraded Storage State (DEF-02)** | §12, §22 | `SpeedMeter.Application.Collector` | `V1-AUDIT: Degraded storage truthfully transitions collector snapshot to Degraded state` | When storage health is `Degraded`, snapshot transitions to `CollectorState.Degraded` and exposes `StorageStatus`. | **PASS** |
| **Write Queue Saturation Backpressure (DEF-06)** | §22 | `SpeedMeter.Storage.SqliteStorageEngine` | `V1-AUDIT: Queue capacity saturation transitions storage health to Degraded` | Bounded queue depth exceeding 90% triggers `StorageHealth.Degraded` and records saturated reason. | **PASS** |
| **Hardware Counter Glitch / Capacity Guard (DEF-04)** | §20, §22 | `SpeedMeter.Application.Collector` | `V1-AUDIT: Capacity overflow delta forces engine discontinuity rebaseline and gap record` | Counter delta $> 2^{63}-1$ triggers `Discontinuity` rebaseline and logs explicit `gap_reason = "capacity_exceeded"`. | **PASS** |
| **Windows Win32 Adapter Parity** | §13 | `SpeedMeter.Windows.AdapterProvider` | `Windows native layout and counter parity with managed OS API` | `GetIfTable2` counters validated against 35 adapters; rx/tx byte parity verified against managed OS API. | **PASS** |
| **Windows Route Enumeration** | §13 | `SpeedMeter.Windows.RouteProvider` | `Windows RouteProvider egress and candidate enumeration` | Evaluated 4 candidate adapters; verified IPv4/IPv6 default route detection against Windows IP routing table. | **PASS** |
| **Taskbar Companion Docking & Fallback** | §17 | `SpeedMeter.Windows.TaskbarCompanion` | `Windows TaskbarCompanion query and placement behavior` | Edge bounds detected; safe floating fallback validated when session geometry is headless or unavailable. | **PASS** |
| **Resource Ceiling & Performance** | §23 | `SpeedMeter.Checks.PerfFixtures` | `PERF: 100 immediate samples and test-process working-set ceiling` | Sample latency avg 1.408 ms (< 5 ms budget); Heap delta 1,790 KB (< 2 MB budget); Working set 47 MB (< 120 MB budget). | **PASS** |

---

## 2. Invariant Proof Summary

1. **Zero Gap Overlap Invariant:**  
   $$\forall i \in \text{usage\_intervals} \, (\text{is\_gap}=0), \, \forall g \in \text{usage\_intervals} \, (\text{is\_gap}=1): [i.\text{start}, i.\text{end}] \cap [g.\text{start}, g.\text{end}] = \emptyset$$  
   *Verified by SQL self-join query returning 0 records across all tests.*

2. **Checked Aggregation Invariant:**  
   $$\text{TotalRx} = \sum_{\text{is\_gap}=0} \text{CheckedAdd}(\text{BytesRx})$$  
   *Verified by zero SQL `SUM()` usage in production codebase and strict `CheckedIntegerMath` assertions.*

3. **Truthful State Invariant:**  
   $$\text{StorageHealth} \neq \text{Healthy} \implies \text{CollectorState} = \text{Degraded}$$  
   *Verified by `TestCollector_SurfacesStorageDegradationOnSnapshot`.*
