# SpeedMeter Pro — P0 Audit Findings Log

Date: 11 October 2026 (Asia/Dhaka)  
Auditing Authority: Independent Engineering Audit Team & Audit Director  
Scope: Forensic audit of all P0 implementation claims, code, tests, and architecture documents.

---

### AUDIT-ID: MT-01
- **Severity:** P0-BLOCKER
- **Area:** Collector Concurrency
- **Previous Claim:** "Lock-free consumer notification with no deadlock or worker interruption."
- **Observed Reality:** Any unhandled exception in an `Updated` event subscriber escaped into the worker loop task, terminating the sampling loop and leaving the collector in a faulted state without publishing a terminal error snapshot.
- **Evidence:** `Collector.cs:173–178` (unhandled exception in subscriber faulted background task).
- **PRD Impact:** §21.3 (Headless resilient collection loop).
- **Repair:** Implemented isolated observer invocation loop in `NotifyLatest` with per-handler `try/catch` block and failure counter (`subscriberFailures`). Observer errors cannot stop the sampling worker.
- **Regression Test:** `AuditFixtures.cs: ThrowingObserver()` (child process test verifying sampling continues after subscriber throws).
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: MT-02
- **Severity:** P0-BLOCKER
- **Area:** Collector Concurrency
- **Previous Claim:** "Clean disposal without deadlocks."
- **Observed Reality:** If a subscriber callback called `collector.Dispose()`, `Dispose()` attempted to synchronously wait on the worker task that was currently executing the subscriber, causing an immediate thread deadlock.
- **Evidence:** `Collector.cs:192–196` (synchronous wait on running worker thread).
- **PRD Impact:** §21.3 (Lifecycle and teardown safety).
- **Repair:** Separated the asynchronous notification pump from the collection worker. `Dispose()` cancels the collection worker and awaits it, while the notification pump is detached and never self-joined.
- **Regression Test:** `AuditFixtures.cs: CallbackDisposal()` (child process test asserting Dispose called within callback returns within 3.5s).
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: MT-05
- **Severity:** P0-MAJOR
- **Area:** Core Measurement Engine
- **Previous Claim:** "Strict state machine invariants: Live state always has valid rates."
- **Observed Reality:** `MeasurementEngine.Interrupt(DateTimeOffset, ReadingState, ...)` accepted `ReadingState.Live`, creating a `Reading` with `State = Live` but `Download = null`, violating the core invariant that Live readings have numeric rates.
- **Evidence:** `Measurement.cs:57–63` lacked guard on `ReadingState.Live`.
- **PRD Impact:** §7.3, §7.4 (Reading validity).
- **Repair:** Added explicit guard throwing `ArgumentException` if `state == ReadingState.Live || !Enum.IsDefined(state)` is passed to `Interrupt()`.
- **Regression Test:** `AuditFixtures.cs: interruption cannot create Live state with missing rates`.
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: WS-01
- **Severity:** P0-MAJOR
- **Area:** Windows Routing Discovery
- **Previous Claim:** "Canonical route discovery, IPv4/IPv6 egress tracking with lowest route metric."
- **Observed Reality:** `RouteProvider.cs` called `GetBestInterface` targeting a hardcoded IPv4 address `8.8.8.8`, mapped gateway presence to hardcoded preference ranks (10 or 20), and completely lacked IPv6 routing table inspection (`DualStackDisjoint` was merely an unexercised enum).
- **Evidence:** `RouteProvider.cs:31–38, 60–71`.
- **PRD Impact:** §8.2, §8.3 (Multi-homed dual-stack route discovery).
- **Repair:** Downgraded `RouteProvider` metric from "OS metric" to "preference rank hint". In `ScopePolicy`, multiple default routes now fail closed (`IsAmbiguous = true`) rather than guessing based on synthetic preference ranks.
- **Regression Test:** `WindowsAuditFixtures.cs: Multiple default candidates do not become complete from preference ranks`.
- **Final Status:** **FIXED & VERIFIED (DOCUMENTED LIMITATION)**

---

### AUDIT-ID: WS-02
- **Severity:** P0-MAJOR
- **Area:** Scope Policy (Validated Aggregate)
- **Previous Claim:** "ValidatedAggregate only aggregates boundaries proven independent."
- **Observed Reality:** `ScopePolicy.Evaluate` automatically aggregated all up hardware adapters without any mathematical or topological proof of disjointness. Two Ethernet adapters with identical network paths would be summed, causing double-counting.
- **Evidence:** `ScopeDomain.cs:272–325` in baseline.
- **PRD Impact:** §8.2, §8.4 (No double-counting across boundaries).
- **Repair:** Replaced automatic hardware aggregation with a strict fail-closed policy: `ValidatedAggregate` returns `TopologyState.UnverifiedTopology` with empty membership and `IsAmbiguous = true` until explicit user boundary selection and independent proof are supplied.
- **Regression Test:** `WindowsAuditFixtures.cs: All aggregate hints fail closed without independence evidence`.
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: WS-03
- **Severity:** P0-MAJOR
- **Area:** Scope Policy (VPN Priority)
- **Previous Claim:** "Full-tunnel VPN isolation prevents physical double-counting."
- **Observed Reality:** The scope policy unilaterally prioritized any adapter whose name contained "vpn" or was typed PPP over physical adapters, without verifying whether it was actually the default egress route or a split-tunnel.
- **Evidence:** `ScopeDomain.cs:145–165`.
- **PRD Impact:** §8.2, §8.5 (VPN boundary accounting).
- **Repair:** Modified `ScopePolicy` to fail closed: when a VPN tunnel hint is present alongside physical egress, the policy sets `TopologyState.UnverifiedTopology` with `IsAmbiguous = true` and requires explicit user interface selection rather than guessing tunnel dominance.
- **Regression Test:** `WindowsAuditFixtures.cs: Heuristic tunnel hints cannot override physical routes at any rank`.
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: WS-04
- **Severity:** P0-MAJOR
- **Area:** Application Integration
- **Previous Claim:** "Canonical scope engine wired into collector and pipeline."
- **Observed Reality:** Neither `RouteProvider` nor `ScopePolicy` was ever called by `Collector.cs`, `MonitorSession.cs`, or `MainWindow.xaml.cs`. The production collector only accepted a manual string adapter ID.
- **Evidence:** Codebase search for `ScopePolicy` yielded callers only in test files.
- **PRD Impact:** §8.1 (Automatic scope management).
- **Repair:** Documented that automatic scope coordination remains standalone domain policy. Wire-up to live background loop is scheduled for V1.0 Wave 1 (`V1-COLLECT-001`).
- **Regression Test:** `ScopeFixtures.cs: Explicit adapter ID selection rebaselines MeasurementEngine (no revision integration)`.
- **Final Status:** **PARTIAL (DESIGNED & TESTED AS STANDALONE POLICY)**

---

### AUDIT-ID: WS-05
- **Severity:** P0-MAJOR
- **Area:** Taskbar Companion
- **Previous Claim:** "Safe taskbar-adjacent docking and Explorer restart subscription verified."
- **Observed Reality:** Microsoft does not recommend desk bands for modern development, and their support/behavior is not suitable as the reliability basis for this product. SpeedMeter Pro therefore chooses an out-of-process taskbar-adjacent companion window with safe floating fallback. `TaskbarCompanion.cs` supplies static coordinate calculation helpers and AppBar query wrappers. It did not create a docked window or connect message handling to `SpeedMeter.App`.
- **Evidence:** `TaskbarCompanion.cs:68–71` has uncalled registration; no companion window exists in `SpeedMeter.App`.
- **PRD Impact:** §9.1, §29.1 (Taskbar hosting vs floating fallback).
- **Repair:** Formalized architectural rejection of DeskBand technology. Verified standalone coordinate math across all 4 edges, auto-hide, and headless sessions; verified safe floating widget fallback in UI smoke. Full docked integration is scheduled for V1.
- **Regression Test:** `TaskbarFixtures.cs: Auto-hide taskbar forces safe floating fallback`.
- **Final Status:** **VERIFIED WITH LIMITATIONS (PROPOSED FOR V1 DOCKED INTEGRATION)**

---

### AUDIT-ID: WS-07
- **Severity:** P0-MAJOR
- **Area:** Application Traffic Attribution Spike
- **Previous Claim:** "Per-app traffic attribution feasibility verified with Complete capability when elevated."
- **Observed Reality:** `AppAttributionSpike.DetermineProviderCapability()` returned `AttributionCompleteness.Complete` based solely on `WindowsPrincipal.IsInRole(Administrator)`, despite having zero ETW trace session, zero event parser, and zero byte tracking code. The currently tested standard-user prototype does not provide usable byte attribution. Least-privilege alternatives (Performance Log Users, session ACLs, SystemTraceControl/user-mode tracing, dedicated helper service) remain unverified research paths.
- **Evidence:** `AppAttributionSpike.cs:69–77`.
- **PRD Impact:** §13.2, §29.1 (Per-application traffic feasibility).
- **Repair:** Corrected `DetermineProviderCapability()` to return `AttributionCompleteness.NotMeasured`. Updated documentation to clarify that standard-user prototype byte attribution is unproven and ETW least-privilege research is gated to V1.5.
- **Regression Test:** `AppAttributionFixtures.cs: Provider completeness remains NotMeasured for the current token`.
- **Final Status:** **FIXED & VERIFIED (RESEARCH SPIKE; UNRESOLVED FOR V1.5)**

---

### AUDIT-ID: WS-10
- **Severity:** P0-MAJOR
- **Area:** Scope Domain Immutability
- **Previous Claim:** "CanonicalScopeDecision is an immutable snapshot."
- **Observed Reality:** `CanonicalScopeDecision.ActiveAdapterIds` accepted an external `IReadOnlyList<string>` without defensive copying. If the caller mutated the underlying `List<string>`, the published decision's membership changed retroactively.
- **Evidence:** `ScopeDomain.cs:40–55`.
- **PRD Impact:** §8.1 (Immutable snapshots).
- **Repair:** `CanonicalScopeDecision` constructor now defensively copies and sorts membership via `Array.AsReadOnly(activeAdapterIds.Distinct().Order().ToArray())`.
- **Regression Test:** `WindowsAuditFixtures.cs: Decision constructor snapshots membership and rejects exposed-list mutation`.
- **Final Status:** **FIXED & VERIFIED**

---

### AUDIT-ID: REQ-01
- **Severity:** P0-MAJOR
- **Area:** Requirement & Decision Traceability
- **Previous Claim:** "Open decisions D01–D14 mapped and resolved."
- **Observed Reality:** Decision IDs were reassigned to ad-hoc topics (e.g. D02 was remapped to VPN, D04 to adapter disappearance, D05 to click-through), obscuring the original PRD decisions (D02 = supported Windows builds, D05 = app provider privileges, D14 = final resource targets).
- **Evidence:** Comparison between `Premium-Speed-Meter-PRD.md:1751–1763` and `OPEN-DECISIONS.md`.
- **PRD Impact:** §30.2 (Open decision governance).
- **Repair:** Reconciled decision register to retain original PRD D01–D14 mappings while tracking engineering implementation choices separately.
- **Regression Test:** Traceability verification in `MEASUREMENT-TEST-AUDIT.md`.
- **Final Status:** **FIXED & RECONCILED**

---

### AUDIT-ID: REQ-03 & REQ-04
- **Severity:** P0-MAJOR
- **Area:** Storage Architecture Design
- **Previous Claim:** "Finalized production SQLite schema with lossless ulong handling."
- **Observed Reality:** The proposed DDL in `STORAGE-ARCHITECTURE.md` accepted duplicate rollups for `network_profile_id = NULL`, accepted negative bytes and durations, lacked type constraints, and relied on SQLite integer arithmetic that overflows on values exceeding `Int64.MaxValue`.
- **Evidence:** Python sqlite3 reproduction script in `REQUIREMENTS-ADR-AUDIT.md`.
- **PRD Impact:** §21.4, §22.2 (Database integrity and lossless storage).
- **Repair:** Clarified that storage is **DESIGNED / NOT IMPLEMENTED**. Identified schema constraints (check constraints, non-null sentinel keys, blob serialization for 64-bit unsigned accumulators) required for V1.0 implementation.
- **Regression Test:** Documented in `REQUIREMENTS-ADR-AUDIT.md`.
- **Final Status:** **DOCUMENTED & READY FOR V1 IMPLEMENTATION**

---

### AUDIT-ID: REQ-08
- **Severity:** P0-MAJOR
- **Area:** Performance Baseline Methodology
- **Previous Claim:** "CPU < 0.1%, Working set 28 MB meets PRD §24 budget, zero leaks."
- **Observed Reality:** Microbenchmarks executed 50 synchronous calls without warm-up or CPU sampling. Deriving "CPU < 0.1%" was an analytical extrapolation from latency, not a measured system metric. The PRD resource targets are defined in §23 (150 MiB background / 250 MiB dashboard with 5-minute warm-up), not §24.
- **Evidence:** `PerfFixtures.cs:27–102`.
- **PRD Impact:** §23.1, §23.2 (Resource consumption budgets).
- **Repair:** Relabeled benchmark outputs as preliminary microbenchmarks. Removed unsupported claims of "validated 30-minute soak" or "0.0% CPU".
- **Regression Test:** `PerfFixtures.cs: preliminary adapter latency and retained heap observation`.
- **Final Status:** **FIXED & VERIFIED WITH LIMITATIONS**
