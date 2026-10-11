# SpeedMeter Pro — Independent V1 Foundation Gate A Forensic Audit Report

**Audit Type:** Independent V1 Foundation Gate A Architecture, Accounting, Durability & Scope Audit  
**Authority:** `Premium-Speed-Meter-PRD.md`  
**Date:** 2026-10-11  
**Status:** **GATE A VERIFIED AFTER REPAIRS — WAVE 2 AUTHORIZED**  
**Audited Target:** `CANONICAL PERSISTENT ACCOUNTING STREAM STABLE`  
**Test Suite Status:** 116/116 Checks PASS (Exit Code: 0) | UI Smoke PASS (Exit Code: 0)

---

## 1. Executive Summary & Gate Decision

The Independent V1 Foundation Audit Team conducted an adversarial forensic audit of the SpeedMeter Pro V1 persistent accounting foundation. The audit inspected the entire data path:
```text
Windows Counters (GetIfTable2)
      ↓
Canonical Scope Engine (AutoPrimary / RouteProvider)
      ↓
Headless Collector Pipeline
      ↓
Validated Byte Delta (CheckedIntegerMath)
      ↓
Accounting Interval Generation (UsageIntervalRecord)
      ↓
Storage Queue (Bounded Channel backpressure)
      ↓
SQLite Transaction (WAL + STRICT schema + Atomic Checkpoint)
      ↓
Process Crash / Restart Boundary (Zero-Overlap Crash Gap Recovery)
      ↓
Historical Aggregation Query (Application-side Checked Summation)
```

### Audit Verdict
**`GATE A VERIFIED AFTER REPAIRS — WAVE 2 AUTHORIZED`**

Initial forensic analysis uncovered six defects, including two `GATE-A-BLOCKER` architectural flaws:
1. **DEF-01 (BLOCKER):** Unclean shutdown crash gap calculation previously defaulted to `StartedAtUtcMs` of the crashed session, creating a temporal overlap against valid interval records already durably committed to SQLite.
2. **DEF-02 (BLOCKER):** Collector failed to surface storage degradation to the user-facing snapshot pipeline, violating the PRD core tenet of *"NO SILENT BYTE LOSS"*.
3. **DEF-03 (MAJOR):** SQLite migration runner silently permitted databases with unknown future schema versions (> 1) without rejecting them, risking schema corruption.
4. **DEF-04 (MAJOR):** Delta counter capacity overflow (`StorageCapacityExceededException`) was caught without rebaselining the measurement engine, permitting cascading delta distortion.
5. **DEF-05 (MAJOR):** Usage interval and counter checkpoint records were enqueued as independent storage commands, permitting split transactions across batch boundaries.
6. **DEF-06 (MAJOR):** Queue capacity of 10,000 commands permitted ~2.8 hours of buffered drift before surfacing backpressure.

All six defects were repaired, verified with 10 newly introduced forensic adversarial test fixtures (expanding the suite from 106 to 116 checks), and proven on Windows 10 x64 (Build 19045) across out-of-process process restart and abrupt process termination boundaries.

---

## 2. Forensic Defect Catalog & Repairs

### DEF-01 (GATE-A-BLOCKER): Crash Gap Overlap Invariant Violation
- **Severity:** Blocker
- **Component:** `SpeedMeter.Application.Collector`, `SpeedMeter.Storage.SqliteStorageEngine`
- **Defect Mechanism:** In unclean shutdown recovery, `gapStartMs` was hardcoded to `last.StartedAtUtcMs`. For a session that ran from $t=10000$ to $t=12000$ with valid intervals committed, and crashed at $t=12001$, restarting at $t=20000$ generated a crash gap covering $t \in [10000, 20000]$. As a consequence, valid intervals ($[10000, 11000]$ and $[11000, 12000]$) and the unobserved gap overlapped for 2,000 ms, corrupting historical duration accounting.
- **Remediation:** Added `GetLastDurableTimestampUtcMsAsync(string sessionId)` to `IStorageEngine` and `SqliteStorageEngine`, executing:
  ```sql
  SELECT MAX(ts) FROM (
      SELECT MAX(end_utc_ms) AS ts FROM usage_intervals WHERE session_id = @sid
      UNION ALL
      SELECT MAX(observed_at_utc_ms) AS ts FROM counter_checkpoints WHERE session_id = @sid
  );
  ```
  `Collector` now computes `gapStartMs = lastDurableMs ?? last.StartedAtUtcMs`. If valid intervals were persisted, the crash gap starts strictly at the end of the last durable interval ($t=12000$).
- **Verification:** Verified by `TestStorage_CrashGapDoesNotOverlapValidHistory` and `TestStorage_ForcedProcessCrashRecoversAccurateGap`. The adversarial SQL query:
  ```sql
  SELECT COUNT(*) FROM usage_intervals i
  JOIN usage_intervals g ON i.is_gap = 0 AND g.is_gap = 1
  WHERE i.start_utc_ms < g.end_utc_ms AND i.end_utc_ms > g.start_utc_ms;
  ```
  returns **0** across all recovery scenarios.

### DEF-02 (GATE-A-BLOCKER): Storage Health Masking in Snapshot Pipeline
- **Severity:** Blocker
- **Component:** `SpeedMeter.Application.CollectorSnapshot`, `SpeedMeter.Application.Collector`
- **Defect Mechanism:** `CollectorSnapshot` omitted storage engine health indicators. When SQLite suffered disk saturation, write errors, or degradation, the Collector continued publishing `CollectorState.Running`, presenting an illusion of normal operation while data was actively lost.
- **Remediation:** 
  1. Extended `CollectorSnapshot` with `public StorageStatus? StorageStatus { get; init; }`.
  2. Updated `Collector.SampleOnce()` and `Publish()` to evaluate storage health: if `storage.Status.Health != StorageHealth.Healthy`, the collector truthfully transitions to `CollectorState.Degraded`.
- **Verification:** Verified by `TestCollector_SurfacesStorageDegradationOnSnapshot`. A database configured in degraded mode immediately forces `snap.State == CollectorState.Degraded`.

### DEF-03 (GATE-A-MAJOR): Future Incompatible Schema Rejection
- **Severity:** Major
- **Component:** `SpeedMeter.Storage.SqliteMigrations`
- **Defect Mechanism:** `ApplyMigrations` did not inspect `PRAGMA user_version` or guard against databases migrated by future application versions. Running on a future schema version (e.g. version 2 or 999) silently proceeded as if unmigrated.
- **Remediation:** Defined `IncompatibleSchemaException`. Updated `GetLatestAppliedVersion` to inspect both `schema_migrations.version` and `PRAGMA user_version`. If `currentVersion > CurrentSchemaVersion`, it immediately throws `IncompatibleSchemaException`. `SqliteStorageEngine` catches this, transitions to `StorageHealth.Degraded`, and preserves the database without mutation.
- **Verification:** Verified by `TestStorage_IncompatibleNewerSchemaRejected`. Database with `user_version = 999` is rejected, `user_version` remains 999, and storage health is marked Degraded.

### DEF-04 (GATE-A-MAJOR): Capacity Overflow Rebaseline & Explicit Gap Recording
- **Severity:** Major
- **Component:** `SpeedMeter.Application.Collector`
- **Defect Mechanism:** When counter deltas exceeded signed 63-bit integer capacity (`StorageCapacityExceededException`), `Collector.PersistInterval` swallowed the exception without rebaselining the `MeasurementEngine`, causing future intervals to calculate corrupted rates against the abnormal counter value.
- **Remediation:** Handled `StorageCapacityExceededException` by invoking `engine.Interrupt(ReadingState.Discontinuity, ...)` and immediately enqueuing an explicit gap record (`gap_reason = "capacity_exceeded"`).
- **Verification:** Verified by `TestStorage_CapacityExceededTriggersRebaselineAndGap`. Delta exceeding $2^{63}-1$ forces engine rebaselining and writes an explicit gap record with 0 valid bytes.

### DEF-05 (GATE-A-MAJOR): Atomic Interval and Checkpoint Ingestion
- **Severity:** Major
- **Component:** `SpeedMeter.Storage.SqliteStorageEngine`
- **Defect Mechanism:** `IngestIntervalAsync` and `RecordCheckpointAsync` were enqueued as independent commands to the write channel. In high-throughput or batch flushes, an interval could commit in one transaction while its counter checkpoint committed in a subsequent transaction, violating relational coupling.
- **Remediation:** Unified command model to `IntervalCmd(UsageIntervalRecord Record, CounterCheckpointRecord? Checkpoint)`. In `WriterLoopAsync`, both records are executed inside the exact same `SqliteTransaction`.
- **Verification:** Verified by `TestStorage_IntervalAndCheckpointCommittedAtomically`.

### DEF-06 (GATE-A-MAJOR): Storage Queue Saturation Backpressure
- **Severity:** Major
- **Component:** `SpeedMeter.Storage.SqliteStorageEngine`
- **Defect Mechanism:** Bounded channel capacity of 10,000 commands permitted up to 2.8 hours of in-memory buffering before backpressure occurred, hiding persistent disk failures.
- **Remediation:** Reduced default channel capacity to 500 commands. Added threshold check in `EnqueueCommandAsync`: when channel depth exceeds 90% capacity, storage health transitions to `StorageHealth.Degraded`.
- **Verification:** Verified by `TestStorage_QueueSaturationTriggersDegradedState`.

---

## 3. Audit Verification Across Core Dimensions

| Dimension | Inspection Criterion | Audit Findings & Evidence | Status |
| :--- | :--- | :--- | :--- |
| **1. Accounting Pipeline** | Zero byte loss, monotonic intervals, checked integer math | No raw SQL `SUM()` queries exist. Application aggregates via `CheckedIntegerMath.CheckedAdd`. 64-bit unsigned BLOB codec verified roundtrip. | **VERIFIED** |
| **2. Network Scope Engine** | AutoPrimary route tracking, debounce, disjoint failure | Tested with 2-second debounce, Ethernet/Wi-Fi route flapping, and disjoint dual-stack egress. Fails closed with `IsAmbiguous = true`. | **VERIFIED** |
| **3. SQLite Architecture** | Schema normalization, STRICT tables, WAL mode, Pragmas | 7 STRICT tables, 0 null-duplicate rollup vulnerabilities, foreign keys enabled (`PRAGMA foreign_keys = ON`), WAL journaling active. | **VERIFIED** |
| **4. Crash & Restart Durability** | Process crash recovery, zero gap overlap, clean restart | Verified across actual OS process boundaries (`RunChildProcess`) and simulated crash termination (`Process.Kill`). SQL overlap count is 0. | **VERIFIED** |
| **5. Queue & Backpressure** | Bounded buffering, failure propagation, degradation surface | Channel bounded to 500. Queue saturation and writer failures set `StorageHealth.Degraded`, propagating to UI snapshot. | **VERIFIED** |
| **6. Security & Dependencies** | CVE vulnerability scanning, package resolution | `.tools/dotnet/dotnet.exe list package --vulnerable` reported **0 vulnerabilities**. `SQLitePCLRaw.*` resolved uniformly to `2.1.12`. | **VERIFIED** |
| **7. Test Quality & Harness** | Executable proof without tautologies, failure propagation | 116 automated checks executed by native test runner (`check.ps1 -Windows`). Failure exit codes verified. No mocked fake passes. | **VERIFIED** |
| **8. Windows Platform & Perf** | Windows 10/11 native API parity, memory ceiling | `GetIfTable2` parity confirmed across 35 physical/virtual adapters. Sample latency avg 1.408 ms (< 5 ms budget). Heap delta < 2 MB. | **VERIFIED** |

---

## 4. Final Gate Conclusion & Authorization

All verification criteria for V1 Foundation Gate A have been satisfied. The persistent accounting stream is mathematically sound, relationally consistent, crash-resilient, and truthfully degraded under fault conditions.

**GATE A VERIFIED AFTER REPAIRS — WAVE 2 AUTHORIZED**
The engineering organization is authorized to proceed to **V1 Wave 2** (Usage History, Billing Cycles, Quotas, and Taskbar Companion Production Integration).
