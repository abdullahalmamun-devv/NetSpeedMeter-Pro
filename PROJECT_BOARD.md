# Current milestone

**V1.0 Production Engineering — WAVE 2 COMPLETE · GATE B VERIFIED · 137 AUTOMATED CHECKS PASSING · PRODUCT SURFACE WAVE AUTHORIZED.** Historical range queries, cumulative monotonic `IntegerSplitter` with exact partition conservation ($\sum \text{allocated} \equiv \text{original}$), timezone-aware `CalendarRangeResolver` with DST and non-mutating billing cycle clamping, persistent quota deduplication with SQLite Migration 2, opening balance isolation, memory-bounded peak-preserving `LiveGraphBuffer`, and atomic schema-versioned `SettingsStorage` with corruption recovery implemented and verified. 137 checks passing with exit code 0. UI smoke test passing with exit code 0. Project Director: lead agent. Updated 11 October 2026 (Asia/Dhaka).

# In progress

None. Wave 2 domain, data, and storage contracts complete and verified.

# Ready (Next Wave: V1.0 Wave 3 — Product Surface & Desktop Presentation)

| Task | Owner | Reviewer | Dependency | Acceptance |
|---|---|---|---|---|
| V1-DASH-001 | Desktop UI / UX | Accessibility + Product | V1-USAGE-001, V1-QUOTA-001, V1-LIVEGRAPH-001 | Main dashboard historical view, range selector (Today/Yesterday/Week/Month/Cycle/Custom), quota cards, and settings dialog |
| V1-TRAY-001 | Windows Platform / UI | Architect + QA | P0-TASKBAR-001, V1-SETTINGS-001 | Taskbar-adjacent companion window docking, edge alignment, and multi-monitor DPI |
| V1-EXPORT-001 | Storage / Data Engineer | Product + QA | V1-USAGE-001 | Keyset-paged CSV and JSON export contracts with metadata disclosure |

# Review

None currently.

# Done

| Task | Owner | Reviewer | Completion Evidence |
|---|---|---|---|
| V1-USAGE-001 | Storage / Measurement | Architect + QA + Director | `HistoricalRange` canonical half-open `[start, end)` domain struct, `IntegerSplitter` monotonic cumulative allocation with `Int128` precision, `CompletenessState`, `UsageSummary`, and 0 SQL `SUM()` queries. Verified by `W2-SPLIT` and `W2-HIST` fixtures. |
| V1-BILLING-001 | Measurement / Product | Architect + QA + Director | `CalendarRangeResolver` for Today, Yesterday, Week, Month, and Billing Cycle; dynamic day clamping (day 31 in Feb clamps to 28/29 without permanent mutation); DST (23h spring forward, 25h fall back) and Asia/Dhaka handling; opening balance isolation. Verified by `W2-CALENDAR` fixtures. |
| V1-QUOTA-001 | Measurement / Storage | Architect + QA + Director | `QuotaDomain` rules, `QuotaEvaluator` with `Int128` guards, threshold jumping monotonic order, and persistent deduplication ledger via SQLite Migration 2 `quota_threshold_events` (`UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_percent)`). Verified by `W2-QUOTA` fixtures. |
| V1-LIVEGRAPH-001 | Application / UI Engineer | Architect + QA + Director | `LiveGraphBuffer` pre-allocated circular ring buffer with bounded memory, zero steady-state allocations, and peak-preserving downsampling. Verified by `W2-GRAPH` fixtures. |
| V1-SETTINGS-001 | Application Engineer | Architect + QA + Director | `SpeedMeterSettings` schema version 1, value sanitization, `SettingsStorage` atomic file replacement (`.tmp` -> replace), and corrupt file recovery (`.corrupt.<ts>.json` backup + safe defaults). Verified by `W2-SETTINGS` fixtures. |
| V1-QA-002 | QA Engineer | Director | 21 new comprehensive adversarial fixtures in `V1Wave2Fixtures.cs`. Full test suite: 137 checks passing with exit code 0. UI smoke test passing. |
| V1-AUDIT-001 | Audit Team / Director | Software Architect + QA | Independent forensic audit of V1 accounting, scope, storage, and crash recovery. Resolved DEF-01 through DEF-06. 116 automated checks pass. Verified by child process restart and crash tests. `GATE A VERIFIED AFTER REPAIRS — WAVE 2 AUTHORIZED`. |
| V1-SCOPE-001 | Windows Platform + Measurement | Architect + QA + Director | Canonical `RouteProvider` (dual-stack IPv4/IPv6), `ScopeStabilizer` (3.0s debounce), and dynamic `AutoPrimary` integrated into `Collector` and `MeasurementEngine.RebaselineForScopeChange`. Zero throughput spikes, preserved cumulative session bytes. Verified by fixtures. |
| V1-STORE-001 | Storage / Data Engineer | Architect + QA + Director | `SpeedMeter.Storage` project: `Microsoft.Data.Sqlite 9.0.2`, `SQLitePCLRaw.bundle_e_sqlite3 2.1.12` (GHSA-2m69-gcr7-jv3q resolved), single writer via bounded `Channel`, WAL mode, `PRAGMA synchronous = FULL; foreign_keys = ON; busy_timeout = 5000;`, 8-byte big-endian BLOB counter checkpoints, checked signed integer math, compound PK without null duplicate bug. Verified by restart byte preservation and rollup uniqueness fixtures. |
| V1-LIFECYCLE-001 | Application Engineer + Storage | QA + Director | Session, clock segment, scope revision records, and explicit gap records (`user_paused`, `system_sleep`, `unclean_shutdown`). Crash gap recovery on restart verified by `TestStorage_UncleanShutdownRecoveryRecordsCrashGap`. Storage degradation resilience verified. |
| V1-QA-001 | QA Engineer | Director | 106 automated checks passing in `scripts/check.ps1 -Windows` across core arithmetic, epochs, scope domain, taskbar geometry, attribution spike, audit regressions, and 10 V1 foundation fixtures. |
| V1-PERF-001 | Performance Engineer | Director + QA | `ReadSnapshot` avg 1.087 ms, `SampleOnce` avg 1.049 ms, process working set 40 MB, heap delta 1782 KB over 100 samples. Microbenchmark verified. |
| P0-PRODUCT-001 | Product Analyst | Director | `FEATURE-MAP.md` (64 features), `ACCEPTANCE-MAP.md` (98 AC entries), `OPEN-DECISIONS.md`. Reviewed and approved. |
| P0-ARCH-001 | Software Architect | Director | `docs/architecture/README.md`, ADR-001 & ADR-006 (Accepted), ADR-002 (Accepted with limits), ADR-003 & ADR-005 (Accepted in V1), ADR-004, ADR-007 (Proposed). Boundaries established. |
| P0-MEASURE-001 | Network Measurement Engineer | Architect + QA + Director | Core `MeasurementEngine`, monotonic deltas, checked 64-bit totals, 125MB/10s fixture (12.5 MB/s & 100 Mbps). Reviewed and approved. |
| P0-WINDOWS-001 | Windows Platform Engineer | Architect + QA + Director | `AdapterProvider` implements `IAdapterCounterProvider` with typed `ReadSnapshot()`. Native Win32 GetIfTable2 verified (35 adapters, Windows 10 build 19045 developer evidence). |
| P0-COLLECTOR-001 | Application Engineer / Director | Architect + QA | Headless `Collector` with lifecycle states (`Starting` to `Stopped`), thread-safe lock-free notifications, MT-01/MT-02 repaired. UI smoke test verified. |
| P0-QA-001 | QA Engineer | Director | Baseline P0 checks passing in `scripts/check.ps1 -Windows`. Reviewed and approved. |
| P0-DOCS-001 | Docs Engineer / Director | Product + Director | `PROJECT_BOARD.md`, `IMPLEMENTATION_STATUS.md`, task handoffs, reviews, and audit verification reports. |
| P0-SCOPE-001 | Windows + Measurement | Architect + QA | `ScopeDomain.cs`, `RouteProvider.cs`, `ScopeFixtures.cs`: standalone scope fixtures and fail-closed aggregate invariants. |
| P0-WIDGET-001 | Desktop UI / UX | Accessibility + Product | `WidgetWindow.cs` Minimal layout: 8 states, tabular numbers, keyboard nudging, lock, opacity, click-through with tray recovery. UI smoke test passed. |
| P0-TASKBAR-001 | Windows Platform | Architect + UI + QA | `TaskbarCompanion.cs`, `TaskbarFixtures.cs`: AppBar geometry, auto-hide floating fallback; DeskBand rejected for product reliability. |
| P0-APPATTR-001 | Windows + Research | Security + Architect + QA | `AppAttributionSpike.cs`, `AppAttributionFixtures.cs`: ProcessIdentity (PID + StartTime) defeats PID reuse; least-privilege alternatives remain unresolved research for V1.5. |
| P0-PERF-001 | Performance Engineer | Director + QA | `PerfFixtures.cs`: microbenchmark latency and memory observations. |
| P0-PERSISTENCE-DESIGN | Storage + Architect | Director + QA | Architecture specification superseded by full V1 `SpeedMeter.Storage` implementation. |

# Blocked / external evidence

- Windows 11 compatibility matrix: host reports Windows 10 build 19045 (developer observation only, not release port approval). Windows 11 primary targets (26H2, 25H2), explicit compatibility lane (26H1), and transitional lane (24H2) require verification on designated test environments.
- Sleep/resume hardware transitions and real Explorer restarts require controlled lab runs so as not to interrupt live host operations.

# Risks

- Frequent network route flapping (Wi-Fi ↔ Ethernet bouncing) is mitigated by `ScopeStabilizer` (3.0s debounce); monitored for production tuning.
- Taskbar-adjacent companion window may collide with dynamically expanding centered taskbar icons on compact Windows 11 screens; floating fallback remains primary safety valve.
- Kernel ETW per-app attribution in tested standard-user mode yields no byte traffic; least-privilege alternatives require investigation before V1.5.

# Product decisions resolved

- D02 (VPN accounting): Tunnel traffic is isolated; physical transport bytes are never automatically summed with tunnel bytes.
- D03 (Taskbar strategy): SpeedMeter Pro will not depend on DeskBand; taskbar-adjacent companion accepted with floating widget fallback.
- D04 (Selected adapter missing): Invariant enforced; missing selected adapter never silently switches.
- D05 (Click-through recovery): Tray notification menu provides independent disable click-through route.
- D06 (Storage architecture): Single writer, WAL mode, FULL synchronous, foreign keys, 8-byte big-endian BLOB counter checkpoints, checked signed integer arithmetic, unrounded summation.
- D07 (Scope stability): Dynamic AutoPrimary routing with 3.0s debounce defeats flapping; scope revisions rebaseline cleanly without speed spikes.
- D08 (Historical splitting & timezones): Canonical half-open intervals with Int128 monotonic cumulative allocation; partition conservation guaranteed; UTC persistence with reporting timezone translation.
- D09 (Quota deduplication): SQLite Migration 2 compound uniqueness ensures warning thresholds fire at most once per cycle across restarts; opening balances isolated from interval byte ledger.
- D10 (Settings & live graph): JSON settings with atomic file replacement and corrupt recovery; bounded circular ring buffer with peak-preserving downsampling.
- D14 (App attribution): Gated to V1.5; requires least-privilege research or administrative helper evaluation.
