# Current milestone

**V1.0 Production Engineering — FOUNDATION WAVE 1 COMPLETE · INTEGRATION GATE A PASSED.** Canonical persistent accounting stream declared stable. 106 automated tests passing with exit code 0. Canonical scope routing with flapping debounce and dual-stack handling integrated into live Collector. SQLite storage engine implemented with single background writer, WAL mode, PRAGMAs, lossless 8-byte big-endian BLOB counter checkpoints, checked signed integer math, compound primary keys without null duplication bug, versioned migrations, and unclean exit recovery. Storage degradation resilience verified without collector crash. Project Director: lead agent. Updated 11 October 2026 (Asia/Dhaka).

# In progress

None. V1 Foundation Wave 1 tasks are complete, reviewed, and integrated.

# Ready (Next Wave: V1.0 Wave 2 — Ledger, Quotas & Dashboard)

| Task | Owner | Reviewer | Dependency | Acceptance |
|---|---|---|---|---|
| V1-QUERY-001 | Storage / Data Engineer | Architect + QA | V1-STORE-001 | Implement historical aggregation queries, interval paging, and export contracts |
| V1-QUOTA-001 | Product / Measurement | QA + Architect | V1-STORE-001 | Quota rules engine, alert thresholds, grace periods, and period rollover |
| V1-DASH-001 | Desktop UI / UX | Accessibility + Product | V1-SCOPE-001, V1-STORE-001 | Main dashboard historical graphs, adapter selector refinement, and storage health indicator |
| V1-TRAY-001 | Windows Platform / UI | Architect + QA | P0-TASKBAR-001 | Taskbar-adjacent companion window docking, edge alignment, and multi-monitor DPI |

# Review

None currently.

# Done

| Task | Owner | Reviewer | Completion Evidence |
|---|---|---|---|
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
- D14 (App attribution): Gated to V1.5; requires least-privilege research or administrative helper evaluation.
