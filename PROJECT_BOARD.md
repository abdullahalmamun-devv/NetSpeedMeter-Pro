# Current milestone

**V1.0 Gate E1 passed; RC.2 consumer experience closure in progress; Gate E2 not yet authorized.** Wave 5 Release Engineering requirements were implemented in RC.1:
- Pinned standalone canonical installer `SpeedMeterPro-1.0.0-rc.1-Setup.exe` (149,004,864 bytes per RC.1 evidence) with transactional staging (`.staging_<guid>`), upgrade backup (`.backup_<guid>`), and graceful shutdown handshake (`Local\SpeedMeterPro.Shutdown.<UserSid>` and `WM_CLOSE`, zero `Stop-Process -Force`) authored per ADR-012.
- Secondary self-contained `win-x64` archive (`SpeedMeterPro-1.0.0-rc.1-win-x64.zip`, 75,300,502 bytes compressed) and secondary framework-dependent distribution (`SpeedMeterPro-1.0.0-rc.1-win-x64-fdd.zip`, 1,686,245 bytes compressed) authored per ADR-012 and ADR-013.
- Zero-UAC standard user installation into `%LocalAppData%\Programs\SpeedMeterPro`, Start Menu integration, and clean uninstaller with user data retention vs complete purge (`-PurgeUserData`).
- Multi-stage update trust pipeline (ADR-014) with pinned RSA trust anchor, deterministic pipe-delimited canonical manifest payload serialization, package SHA-256 verification, Authenticode binary signature and publisher enforcement with dual trust modes (`Production` vs `DevelopmentTest`), quarantine staging, and downgrade rejection.
- Durability and disaster recovery: Pre-migration SQLite snapshot creation on live WAL writer using native `SqliteConnection.BackupDatabase` and rollback recovery drill with 100% data fidelity.
- Whole-application shutdown durability (ADR-015): 3.0-second maximum ceiling (`DisposeWithTimeout`), `SessionEnding` handling, and non-blocking operational diagnostics.
- Bounded operational logger (`OperationalLogger`) with 5 MB ceiling, 7-day retention, and automatic redaction of IPv4 addresses, MAC addresses, and user paths; sanitized diagnostic support bundle export (`DiagnosticBundleService`) with live redaction preview.
- Authenticode signing (`scripts/sign.ps1`), SPDX 2.3 SBOM (`artifacts/release/SpeedMeterPro-1.0.0-rc.1-sbom.spdx.json`), and Third-Party Notices (`SpeedMeterPro-1.0.0-rc.1-THIRD-PARTY-NOTICES.txt`).
- Master release candidate orchestrator (`scripts/release.ps1`) generating verified checksums (`RELEASE-HASHES.sha256`) and executing live silent installation/uninstallation drills.
- Authoritative documentation: `docs/COMPATIBILITY.md`, `docs/PRIVACY.md`, `docs/HELP.md`, `docs/KNOWN-ISSUES.md`, `docs/RELEASE-NOTES-v1.0.0.md`, and `docs/evidence/RELEASE-EVIDENCE-v1.0.0-rc.1.md`.
- Baseline verification: 250 passed, 1 intentional skip, 0 failed (`check.ps1`); Windows checks: 258 passed, 1 skip, 0 failed (`check.ps1 -Windows`); WPF smoke: 18/18 native assertions True. `GATE E1 INDEPENDENTLY VERIFIED — CONSUMER EXPERIENCE CLOSURE AUTHORIZED`. Gate E2 GA blocked awaiting physical field validation. Updated 12 October 2026 (Asia/Dhaka).

# In progress

| Task | Owner | Reviewer | Dependency | Acceptance |
|---|---|---|---|---|
| V1-CONS-UPDATE-001 | Update/Application + UI | Security, Accessibility, QA, Director | Gate E1, ADR-014 | Opt-in daily GitHub signed-manifest checks, manual check, secure one-click update and restart |
| V1-CONS-INSTALL-001 | Windows Release/Installer | Accessibility, QA, Director | Gate E1, ADR-012/015 | Graphical per-user install/uninstall with default data retention and explicit purge |
| V1-CONS-CRASH-001 | Reliability/Crash UX | Security, Accessibility, QA, Director | Gate E1, ADR-015 | Bounded redacted local crash reports and next-launch recovery without upload |
| V1-CONS-RELEASE-001 | Director/Release | Independent QA, Security, Accessibility | Three consumer systems | RC.2 clean source, regression, consumer integration, artifact and evidence |

# Queued behind RC.2 consumer closure

| Task | Owner | Reviewer | Dependency | Acceptance |
|---|---|---|---|---|
| V1-GA-001 | Field / Reliability Engineer | QA + Director | RC.2 consumer closure passed | 7-day soak stability verification and 24-hour mixed network workload run on physical hardware |
| V1-GA-002 | Platform / QA Engineer | QA + Director | RC.2 consumer closure passed | Windows 11 target validation matrix (26H2, 25H2, 26H1, 24H2) live hardware power transition verification |
| V1.5-PLAN-001 | Project Director | Software Architect + Product | V1.0 gate progression | Author V1.5 plan only after V1.0 gate progression permits it |

# Review

RC.2 Security/Privacy and Accessibility re-reviews are recorded in `docs/reviews/` as CHANGES REQUESTED. The source findings were addressed and the clean-tree, test-signed RC.2 package passed its silent sandbox install/uninstall drill (`docs/evidence/RELEASE-EVIDENCE-v1.0.0-rc.2.md`). Actual RC.1→RC.2 updater/restart and data-preservation evidence, interactive keyboard/Narrator/DPI/High Contrast observations, packet-level privacy, and production signing/hosting remain open. Gate E1 remains the last passed gate; the Director has not accepted RC.2 consumer closure or authorized Gate E2.

# Done

| Task | Owner | Reviewer | Completion Evidence |
|---|---|---|---|
| V1-W5-GATE-E1 | Release Engineer / Office | Architect, QA, Director | V1 Wave 5 full release engineering closure: Dynamic version/commit model (1.0.0-rc.1 derived from MSBuild/git), standalone signed per-user installer `SpeedMeterPro-1.0.0-rc.1-Setup.exe`, transactional staging & rollback, graceful shutdown handshake with monotonic Stopwatch (zero `Stop-Process -Force`), dual trust mode Authenticode signing pipeline with native WinVerifyTrust P/Invoke, static embedded update trust anchors, live WAL online SQLite pre-migration backup via `SqliteConnection.BackupDatabase`, monotonic 3.0s whole-application shutdown bound, bounded sanitized operational logging, diagnostic bundle export with live redaction preview, SPDX 2.3 SBOM, third-party notices, release notes, compatibility matrix, and authoritative release hashes. 250 PASS / 1 SKIP / 0 FAIL (`check.ps1`); 258 PASS / 1 SKIP / 0 FAIL (`check.ps1 -Windows`); 18/18 WPF smoke assertions True (`smoke.ps1`). `GATE E1 INDEPENDENTLY VERIFIED — CONSUMER EXPERIENCE CLOSURE AUTHORIZED`. Gate E2 GA blocked pending physical soak. |
| V1-W4-GATE-D | Project Director / Office | Architect, QA, Privacy | V1 Wave 4 full capability closure: Connectivity 3-axis engine, bounded reachability probes, truthful network details, Native WLAN API P/Invoke metadata correlation by adapter GUID and connected BSSID matching (6-byte MAC comparison without list ordering assumptions or frequency borrowing), NLM COM profile name discovery, ping (10 attempts PRD default) & DNS tools, public IP lookup with disclosure, V1 alerts with deduplication, first-run privacy onboarding, light/dark/system themes, multi-layout widget, single-instance User SID mutex & file lock activation with clean exit code 0, low-resource UI throttling, startup/hotkeys, history pruning, authentic V2->V3 SQLite migration fidelity, and atomic config export/import. 233 PASS, 1 SKIP, 0 FAIL (`check.ps1`); 241 PASS, 1 SKIP, 0 FAIL (`check.ps1 -Windows`); 18/18 WPF smoke assertions True. `GATE D INDEPENDENTLY VERIFIED — WAVE 5 RELEASE ENGINEERING AUTHORIZED`. |
| V1-CLOCK-CODE-001 | Project Director + Network Measurement + QA | Architect + QA | Monotonic lifecycle gap duration, zero-width UTC discontinuity marker, new clock segments, inferred sleep fallback, history uncertainty, and structured shutdown result integrated. Release build 0 warnings/errors; 197 passed, 1 intentionally skipped, 0 failed; WPF smoke 18/18. Code integration accepted; product authority decision accepted ADR-011 safeguards. |
| V1-TRUTH-001 | Independent Audit Director | Software Architect + QA | Historical closure of scope UI and history error UX (183 passed, 1 skipped; pre-change smoke 18/18). |
| V1-AUDIT-003 | Independent Audit Director | Software Architect + QA | Final Gate C native coordinate and UI truth closure: single native HWND positioning authority, elimination of `GetDpiForMonitor` in favor of `GetDpiForWindow`, explicit `DesignWidthDip`/`DesignHeightDip` constants, degradation axis decoupling (`StorageHealth` vs measurement availability), individual primary navigation assertions. 169 passed, 1 skipped, 0 failed. UI smoke verified with 18/18 native assertions True. `GATE C FULLY AND CORRECTLY CLOSED`. |
| V1-DASH-001 | Desktop UI / UX | Accessibility + Product | Complete 5-destination navigation shell (`OverviewView`, `LiveTrafficView`, `AppsView`, `HistoryView`, `NetworkToolsView`, `SettingsView`), MVVM ViewModels, design tokens (`Tokens.xaml`, `ThemeDark.xaml`), tabular numerals, query cancellation token CTS in `HistoryViewModel`, basis points threshold formatting/parsing in `QuotaViewModel`, live graph windowing in `LiveTrafficViewModel`. Verified by fixtures and smoke test. |
| V1-TRAY-001 | Windows Platform / UI | Architect + QA | `QuickPanelWindow` with multi-monitor work-area clamping, auto-dismiss on deactivate/Escape, Tray menu semantic click-through recovery intact, minimal floating widget synchronized. Verified by `W3-QUICKPANEL` and `smoke.ps1`. |
| V1-EXPORT-001 | Storage / Data Engineer | Product + QA | `IExportService` / `ExportService`: Keyset-paged CSV export with metadata disclosure comments and formula injection neutralization (`=`, `+`, `-`, `@`, `\t`, `\r` prepended with `'`), valid typed-numeric JSON export with schema v1.0 and explicit nulls, atomic `.tmp` file replacement. Verified by `W3-EXPORT` fixtures. |
| V1-AUDIT-002 | Audit Team / Director | Software Architect + QA | Independent forensic audit of V1 Wave 2 domain contracts. Remediated 13 defects (DEF-B01 through DEF-B13). 148 passed, 1 skipped, 0 failed. UI smoke test passed. `GATE B VERIFIED AFTER REPAIRS — WAVE 3 AUTHORIZED`. |
| V1-USAGE-001 | Storage / Measurement | Architect + QA + Director | `HistoricalRange` canonical half-open `[start, end)` domain struct, `IntegerSplitter` monotonic cumulative allocation with `Int128` precision, `CompletenessState`, `UsageSummary`, and 0 SQL `SUM()` queries. Verified by `W2-SPLIT` and `W2-HIST` fixtures. |
| V1-BILLING-001 | Measurement / Product | Architect + QA + Director | `CalendarRangeResolver` for Today, Yesterday, Week, Month, and Billing Cycle; dynamic day clamping (day 31 in Feb clamps to 28/29 without permanent mutation); dynamic DST (Lord Howe Island 30m, US 23h/25h) and Asia/Dhaka handling; opening balance isolation. Verified by `W2-CALENDAR` fixtures. |
| V1-QUOTA-001 | Measurement / Storage | Architect + QA + Director | `QuotaDomain` rules, `QuotaEvaluator` with `Int128` guards, threshold jumping monotonic order, and persistent deduplication ledger via SQLite Migration 2 `quota_threshold_events` (`UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`). Truthful non-zero recorded bytes when disabled. Verified by `W2-QUOTA` fixtures. |
| V1-LIVEGRAPH-001 | Application / UI Engineer | Architect + QA + Director | `LiveGraphBuffer` pre-allocated circular ring buffer with bounded memory, monotonic timestamp guards, duplicate sequence handling, and peak-preserving downsampling. Verified by `W2-GRAPH` fixtures. |
| V1-SETTINGS-001 | Application Engineer | Architect + QA + Director | `SpeedMeterSettings` schema version 1, monitor device identity, `SettingsStorage` atomic file replacement (`.tmp` -> replace), future schema rejection without overwriting, and corrupt file recovery (`.corrupt.<ts>.json` backup + safe defaults). Verified by `W2-SETTINGS` fixtures. |
| V1-QA-002 | QA Engineer | Director | 28 comprehensive adversarial fixtures in `V1Wave2Fixtures.cs`. Full test suite: 148 checks passed, 1 skipped, 0 failed. UI smoke test passing. |
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

- V1 clock/shutdown gate: backward wall-clock jumps can repeat valid UTC labels across distinct clock segments; exact bytes and observed UTC cannot simultaneously satisfy a global zero-overlap UTC-row invariant. ADR-011 preserves both rows and marks affected summaries `ClockUncertain`. This conflicts with the handoff's literal no-valid-valid-overlap acceptance. Real sleep/hibernate, timezone-only reporting event, whole-app storage-disposal bound, and user-facing shutdown diagnostic path remain open.
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
- D08 (Historical splitting & timezones): Canonical half-open intervals with Int128 monotonic cumulative allocation; partition conservation guaranteed; UTC persistence with reporting timezone translation; dynamic non-1-hour DST resolution; strict half-open inequality queries.
- D09 (Quota deduplication): SQLite Migration 2 integer basis points (`threshold_bps INTEGER NOT NULL`) compound uniqueness ensures warning thresholds fire at most once per cycle across restarts; PRD defaults (80%, 90%, 100%); truthful non-zero recorded usage when disabled.
- D10 (Settings & live graph): JSON settings with atomic file replacement and corrupt recovery; strict future schema rejection (`schemaVersion > CurrentSchemaVersion`); monitor device identity persistence; monotonic circular ring buffer guards.
- D14 (App attribution): Gated to V1.5; requires least-privilege research or administrative helper evaluation.
