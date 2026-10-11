# Independent requirements, architecture decisions and storage audit

Date: 11 October 2026 (Asia/Dhaka). Owner: Auditor A/J. Reviewer and integration authority: Audit Director.
Task: read-only P0 forensic phase. Dependencies: authoritative PRD revision 2.0 and the existing repository. Scope: all 64 feature rows, 98 acceptance rows, open decisions, ADR-001–007, storage design, board/status, tasks, handoffs and reviews against source. Owned file: this audit note only. Original documents, source and test code were not changed in this phase. Acceptance: explain what exists, what was claimed, what is proven and what remains unproven, with reproducible evidence.

## Summary

The feature and acceptance registers reproduce the authoritative PRD correctly. The open-decision register does not. The P0 passed claim is not supported by its own required feasibility evidence. Five Accepted ADR headers overstate implementation or verification; the storage design is incomplete and has independently reproduced schema defects. Storage is **DESIGNED / NOT YET IMPLEMENTED**, with the design requiring revision before use as a production contract.

This audit does not declare the whole audit gate; only the Director can do so. Recommendation from this workstream: keep P0 open, repair the documented prerequisite defects, reconcile the records, and do not start V1 feature development.

## What exists, claimed, proven, unproven

| Area | Exists | Claimed | Proven by this workstream | Unproven / limitation |
|---|---|---|---|---|
| Requirements | F01–F64 and all 98 AC rows | Complete original registers | Independent exact-field comparison found 64/64 features, 98/98 ACs and zero source-text differences | Associations are analytical traceability, not execution records |
| Decisions | OPEN-DECISIONS plus seven ADRs | P0 decisions resolved / Accepted architecture | Source and PRD comparison reveals ID substitution, unsupported priority changes and status contradictions | No valid resolution of supported-builds D02, provider D05 or resource-target D14 |
| Production layers | Core, Windows, Application, App, Checks | Five projects built and integrated | Solution/project references confirm the architecture split | Build and 69-check reproduction belongs to Director/QA evidence, not this read-only inspection |
| Scope | Policy model and heuristic route candidate provider | Canonical scope engine integrated, zero double counting | They exist as callable code; production Collector selects one adapter ID | No App/Collector call of RouteProvider or ScopePolicy; no production aggregate accounting or scope revision input |
| Taskbar | Geometry/query helper | Safe companion and Explorer subscription | Helper exists; GetTaskbarCreatedMessageId registers a message number | No App consumer of helper/message or docked window integration was found |
| App research | Identity records, PID/time comparison, token-role check | ETW elevation and feasibility confirmed | PID/time comparison code exists | No trace startup, event parser, protocol workloads, loss measurement or actual provider privilege experiment |
| Storage | Markdown specification and six-table DDL | Accepted production architecture, lossless ulong, migration/gap safety | DDL executes in an isolated in-memory SQLite connection; defects reproduced below | No Storage project, package, writer, repository, migration, integration or recovery test |
| Performance | Fifty-read latency loops and 100-read burst | CPU <0.1%, 28 MB under 50 MB PRD target, zero handle leak | Source methodology is identifiable | CPU never measured; WPF absent from that process; no before/after handles/threads; PRD targets differ |
| Windows | Prior Windows 10 build 19045 evidence | Passed conditionally on Windows 11 lab | Documents consistently identify the observed host | It cannot validate Windows 11 25H2, mixed DPI or shell/lifecycle behaviour |

## Register verification

Independent command read PRD and maps without running the generator (the generator overwrites files). It selected rows using `^\| F\d\d \|` and `^\| AC-[A-Z]\d\d \|`, required exactly one mapped row per original ID, compared the feature capability/target/qualification fields and compared the AC original condition/pass fields case-sensitively.

Observed result, exit 0:

```json
{"PrdFeatures":64,"MappedFeatures":64,"PrdAcceptance":98,"MappedAcceptance":98,"Diffs":[]}
```

FEATURE-MAP and ACCEPTANCE-MAP explicitly describe an initial baseline and keep full release acceptance open. They do not falsely mark acceptance scenarios passed. That qualification is appropriate, though baseline evidence is stale after later work. A script count of 98 mappings is not proof of 98 executable tests. F60/F61 have no dedicated AC and correctly refer to configuration/release gates.

The exact-copy comparison is narrower than validating every analytical association. For example, AC-P03 adds F62 category tags even though the original condition is process grouping; a separate category-tag test will still be needed. Broad F01/performance associations likewise do not establish complete feature acceptance. This is a traceability limitation, not a missing original AC row.

## Findings from the read-only phase

### REQ-01 — Decision IDs and priorities changed without product authority

Severity: **P0-MAJOR**. Previous claim: open decisions and product decisions are correctly mapped/resolved.

Observed reality: PRD lines 1751–1763 define D02 as supported Windows builds, D03 as docking, D04 as runtime/UI, D05 as per-app provider/privileges, D14 as final resource targets. OPEN-DECISIONS lines 11–35 calls D02 counter-provider selection, D05 storage, D14 app attribution. PROJECT_BOARD lines 53–57 calls D02 VPN accounting, D04 selected-adapter disappearance, D05 click-through and D14 app attribution. These are different decisions under reused IDs.

OPEN-DECISIONS lines 53–55 assigns Must/Should priorities to F26/F56 despite the feature register explicitly identifying priority ambiguity, then describes F63 as portable. F63 is diagnostic report (PRD line 252); portable is F45. Section references also drift: taskbar is section 9, networks are section 8, quality is section 14, app research is section 13. The purported five-minute rolling jitter/60-attempt requirement is actually the optional score gate in section 14.5, and the high-ping 30-second/10-reply incompatibility in section 15.2 is omitted.

PRD impact: D02/D05/D14 prerequisites can disappear behind unrelated resolutions; scope priorities could be silently changed. Proposed repair: restore exact original D01–D14 labels, preserve unresolved status/owner/gate, use separate engineering IDs for additional choices, correct F63 and references, retain ambiguity without authoring new priorities. Verification: independent comparison against PRD 30.2 and feature rows after editing. Final status: **OPEN — no repair performed in this phase**.

### REQ-02 — P0 exit is claimed despite missing required experiments

Severity: **P0-MAJOR**. Previous claim: PROJECT_BOARD line 3 and IMPLEMENTATION_STATUS line 4 say P0 gate passed, with all workstreams complete.

Observed reality: PRD 29.1 (lines 1668–1674) requires demonstrated canonical VPN/multi-adapter behaviour, safe taskbar approach on chosen builds, standard/elevated observations, TCP/UDP/IPv4/IPv6 app prototype and loss reporting, stack decisions, hardware/method/results and owned remaining decisions. Existing app spike has no network provider, scope is disconnected from production collection, taskbar helper is disconnected from App, and the resource method differs from section 23. Existing docs/P0-STATUS.md continues to show those gates open. There is no approved PRD feature-promise revision closing these evidence gaps.

Proposed repair: retract passed/completed claims; record demonstrated primitives separately from unexecuted feasibility; state audit gate separately from original product P0 gate. The audit request allows Windows 11 environment carryover only where architecture does not depend on unresolved behaviour; it does not turn missing app/provider/topology experiments into observations. Verification: requirement-by-requirement gate matrix with executable/Windows evidence or an explicit bounded decision. Final status: **OPEN**.

### REQ-03 — Storage rollup uniqueness and accounting types are not enforced

Severity: **P0-MAJOR** for the accepted design before V1 builds upon it; there is no production database corruption to report.

Previous claim: production DDL, exact integer rollups, foreign-key integrity, lossless accounting. Observed reality: STORAGE-ARCHITECTURE lines 73–85/94–103 allow nullable `network_profile_id` inside the rollup composite primary key, INTEGER affinity without strict/check guards, and no consistency constraints for gap rows. The original DDL independently accepted two otherwise identical NULL-network rollups, real/text byte fields, negative bytes/duration, and a gap with nonzero bytes/no reason. Duplicate unresolved-network rollups can duplicate reporting; type affinity alone does not prohibit REAL bytes. [SQLite CREATE TABLE documentation](https://www.sqlite.org/lang_createtable.html) describes NULL uniqueness semantics.

Proposed repair: define an explicit unresolved-network key and enforce uniqueness; specify validated nonnegative integer byte/duration/gap invariants and storage type enforcement; define writer validation and regression evidence rather than assuming DDL alone proves domain safety. Verification: isolated duplicate, wrong-type, negative, invalid-gap insert rejection/replay tests. Final status: **OPEN; adversarial design reproduction completed**.

### REQ-04 — Unsigned range policy and checkpoint representation are unresolved

Severity: **P0-MAJOR** for design readiness.

Previous claim: accepted lossless ulong handling. Observed reality: STORAGE-ARCHITECTURE line 31 asserts interval/rollup bytes cannot exceed Int64.MaxValue, but no bound/split/rejection policy establishes that assertion; production Core permits exact unsigned values above it. The BLOB-or-high/low checkpoint proposal (line 33) has no checkpoint table, length invariant, codec or selection. Exact SQL SUM can itself overflow even when each row fits signed range. Independent `SUM(Int64.MaxValue,1)` raised `integer overflow`; binding 2^63 to SQLite INTEGER raised a range error. Switching to REAL or SQLite `total()` would violate exact accounting. [SQLite aggregate documentation](https://www.sqlite.org/lang_aggfunc.html) confirms integer overflow behaviour.

Proposed repair: choose one lossless checkpoint encoding; define the checked domain-to-storage boundary, bounded split/rejection and exact rollup accumulation policy. Do not add persistence implementation during this audit. Verification: codec design cases 0/Int64.MaxValue/2^63/UInt64.MaxValue, split/replay invariants and signed aggregate overflow. Final status: **OPEN**.

### REQ-05 — Accepted schema cannot preserve required history provenance

Severity: **P0-MAJOR** for design readiness.

Observed reality: the six-table DDL has network_profile_id on usage intervals, which is a useful step, but lacks session/clock mapping segment, source method/epoch/counter checkpoints and normalized time-bound interface membership. One network reference per aggregate interval has no rule for distinct networks among aggregate members or a profile change mid-sample. `daily_rollups.date_key` is labelled audit timezone and omits original UTC boundaries, saved reporting timezone/calendar revision and peak interval context. `quota_rules` has no revision, saved timezone, direction, immutable cycle, opening balance or fired-threshold state. No minute/hour retention/compaction contract, bounded channel or checkpoint/read-transaction strategy is specified. These omissions conflict with PRD 11.1–11.3, 21.4, 22.1–22.4 and ADR-005, and are not a demand to implement all later V1/V2 entities now.

Proposed repair: describe the core ledger/membership/provenance/durability prerequisites and mark later datasets out of current schema scope; downgrade “production schema finalized” until these contracts are resolved. Verification: design walk-through for two networks on one interface, two aggregate-member networks, UTC backward jump, DST/date allocation, archived timezone rebinning, reset versus history and replayed quota crossing. Final status: **OPEN**.

### REQ-06 — Durability and migration specification overstates guarantees

Severity: **P0-MAJOR** for design readiness.

Observed reality: STORAGE-ARCHITECTURE line 15 chooses NORMAL while PRD line 1154 and ADR-005 line 13 retain FULL. WAL/NORMAL can preserve consistency while losing committed data after a power/system failure; it is not equivalent durability. The absolute zero-corruption claim at line 23 is not verified product recovery evidence. [SQLite synchronous documentation](https://www.sqlite.org/pragma.html#pragma_synchronous) distinguishes these guarantees.

The migration section (lines 134–135) calls for a backup copy then restoration without defining a consistent snapshot, writer stop/close, WAL sidecars, integrity validation or atomic recovery. It cannot establish safe rollback of valuable data. [SQLite Backup API](https://www.sqlite.org/backup.html) supplies a consistent snapshot mechanism. Startup gap from last committed end to current wall clock also requires clock/session confidence and clean-versus-unclean shutdown tracking; elapsed monotonic time cannot be inferred across process epochs.

Proposed repair: retain proposed FULL and measured durability targets, replace guarantees with specified failure/acknowledgment semantics, require consistent backup and fail-safe migration recovery, and mark all recovery experiments pending. Verification: future crash/power, WAL backup, disk-full, migration failure and clock-reversal drills before durability claims. Final status: **OPEN**.

### REQ-07 — Accepted ADRs contain materially unfulfilled decisions

Severity: **P0-MAJOR**. ADR-specific evidence and required downgrades appear below. Production Collector performs provider read under its command lock (Collector.cs lines 127–131), invokes subscribers synchronously (157), and can await its worker during dispose (196). ADR-006 line 13 explicitly requires external reads outside that lock, generation rejection and bounded delivery. A single Latest property is not proof arbitrary subscribers cannot stall the worker. Other auditors own executable concurrency reproductions and repair proposals.

ScopeDomain and RouteProvider are only called by fixtures/checks, not Collector/App. MeasurementEngine accepts no CanonicalScopeDecision/revision, so scope-policy revision is not wired into a production rebaseline contract. Route candidates use synthesized 10/20 metrics, a fixed IPv4 destination and names/type heuristics, and auto VPN chooses tunnel rather than the PRD 8.2 preference for known physical egress. TaskbarCreated registration has no App invocation/message handler. These discrepancies make Accepted implementation claims unsupported even if individual fixture functions return the expected result.

Final status: **OPEN; independent source evidence supplied to Director**.

### REQ-08 — Performance claims refer to invented PRD budgets and incomplete evidence

Severity: **P0-MAJOR** for evidence used to pass P0.

P0-PERF task/review/handoff cite section 24 and a 50 MB/20 ms/25 ms budget. PRD resource targets are section 23: total-machine CPU, combined private working set 150 MiB background / 250 MiB dashboard, five-minute warm-up and ≥30-minute measurement. Fifty synchronous latency reads are preliminary microbenchmarks; deriving CPU <0.1% from latency/one-second cadence does not measure product CPU. The 100-read burst takes one end-only process handle/thread measurement; no start comparison or steady-state leak trend is asserted. It measures Checks working set, not WPF product combined private working set; its assertion is 300 MiB, not the quoted 50 MB.

Proposed repair: relabel measured loop latency as preliminary, remove CPU/leak/PRD-budget pass claims, record actual method and pending reference benchmark. Final status: **OPEN; no benchmark run by this workstream**.

### REQ-09 — Historical and current records are mixed

Severity: **P0-MINOR** for baseline snapshots; **P0-MAJOR** where a current headline authorizes progression.

The feature/acceptance map headers appropriately identify initial baseline, but task files still say queued/in progress, handoffs still request review, architecture README calls all ADRs proposed, while the board says all reviewed/integrated. Review docs use sweeping “verified,” “immune,” “zero leak” and complete-accessibility statements that do not match the limited described tests. PRODUCT review mentions cases ending AC-X03 though X04–X06 exist; independent count confirms they are present. None of these text claims supplants evidence.

Proposed repair: preserve original experiment claims as historical records with an audit correction/qualification, reconcile current board/status, and link each acceptance claim to actual current evidence. Final status: **OPEN**.

### REQ-10 — Target environment remains absent

Severity: **VALIDATION-PENDING**. PRD 3.3 (line 170) specifies a Windows 11 25H2 reference lane; the board lists 22H2/23H2/24H2 instead. All local prior observed execution is Windows 10 19045. Keep actual Windows 10 developer evidence distinct from supported Windows product lanes and refresh the chosen matrix; do not approve the conditional Windows 10 product port through this audit. Real Explorer restart, mixed DPI/removal, Narrator/high contrast and hardware sleep remain pending until observed.

## ADR review at read-only baseline

| ADR | Previous status | Audit judgment | Recommended status | Reason |
|---|---|---|---|---|
| 001 Runtime/UI | Accepted | **SUPPORTED** as a stack decision | Accepted, explicitly conditional on supported target runtime/OS validation | Five-project framework/native/WPF split implements the chosen design; acceptance does not establish release support/resource gates |
| 002 Counter provider | Accepted | **PARTIALLY SUPPORTED** | Proposed / counter-provider choice retained, conformance pending | Real IP Helper path and typed availability exist; no observed lifecycle generation supplied to SourceEpoch, no 60-second independent accuracy or target matrix |
| 003 Scope/identity | Accepted | **PARTIALLY SUPPORTED** | Proposed / partial prototype | Selected-adapter production path is real; standalone policy is not production canonical scope, synthetic route metrics/IPv4 heuristic and unsigned membership contract remain unresolved |
| 004 Taskbar/fallback | Accepted | **INSUFFICIENT EVIDENCE** | Proposed / geometry spike only | Geometry and fallback functions exist; no App docking consumer, no TaskbarCreated message processing, no target shell experiment |
| 005 Storage | Accepted | **INCORRECT** as finalized specification | Proposed — designed / not implemented, revision required | NORMAL/FULL contradiction, independently reproduced duplicate/type/gap defects, unsigned/provenance/recovery contracts incomplete |
| 006 Collector/UI | Accepted | **PARTIALLY SUPPORTED** | Proposed — repair and independent revalidation required | Headless separation and callback-outside-lock exist; provider remains under lock, subscriber failure/slow callback and shutdown hazards require independent tests, external-read/generation requirements unfulfilled |
| 007 App attribution | Proposed | **INSUFFICIENT EVIDENCE** for feasibility, appropriate as research proposal | Proposed — unresolved provider/least-privilege research | PID/time fixture and token role query do not measure protocol events, loss/completeness, privilege alternatives or cross-user isolation |

An ADR may accept a carefully bounded technical choice without claiming its entire future validation passed. Current 002–006 headers explicitly assert implementation/verification and therefore need amendment. Only Director inspection/integration can update their statuses.

## Isolated storage reproduction

Ran the bundled Python executable supplied by load_workspace_dependencies, using built-in sqlite3 **3.53.1**, reading the original markdown and extracting its second SQL fence (DDL). Created only `:memory:` connection with foreign_keys ON; no production database, package, source or test was added.

Reproduction operations:

```python
import re, sqlite3, pathlib
text = pathlib.Path('docs/architecture/STORAGE-ARCHITECTURE.md').read_text(encoding='utf-8-sig')
ddl = re.findall(r'```sql\n(.*?)```', text, re.S)[1]
c = sqlite3.connect(':memory:')
c.execute('PRAGMA foreign_keys=ON')
c.executescript(ddl)
c.execute("INSERT INTO scope_revisions VALUES ('r','s','SelectedAdapter','SinglePrimary','a','[a]',0,'2026-10-11T00:00:00Z')")
for _ in range(2):
    c.execute('INSERT INTO daily_rollups(date_key,scope_id,network_profile_id,bytes_rx,bytes_tx) VALUES(?,?,?,?,?)',
              ('2026-10-11','s',None,100,200))
c.execute('INSERT INTO usage_intervals(interval_id,start_utc,end_utc,monotonic_duration,scope_revision_id,bytes_rx,bytes_tx,is_gap,gap_reason) VALUES(?,?,?,?,?,?,?,?,?)',
          ('bad','end-before-start','unparseable',-1,'r',-1,2,1,None))
c.execute('INSERT INTO usage_intervals(interval_id,start_utc,end_utc,monotonic_duration,scope_revision_id,bytes_rx,bytes_tx) VALUES(?,?,?,?,?,?,?)',
          ('badtype','x','y',1,'r',1.5,'bad'))
print(c.execute('SELECT count(*) FROM daily_rollups').fetchone())
print(c.execute("SELECT monotonic_duration,bytes_rx,bytes_tx,is_gap,gap_reason FROM usage_intervals WHERE interval_id='bad'").fetchone())
print(c.execute("SELECT typeof(bytes_rx),typeof(bytes_tx) FROM usage_intervals WHERE interval_id='badtype'").fetchone())
for statement, args in [('SELECT sum(v) FROM (SELECT 9223372036854775807 AS v UNION ALL SELECT 1)', ()),
                        ('SELECT ?', (1 << 63,))]:
    try:
        c.execute(statement, args).fetchone()
    except (sqlite3.OperationalError, OverflowError) as error:
        print(type(error).__name__, str(error))
```

Observed process exit 0 with deliberately caught expected range errors:

```text
tables: schema_migrations, network_profiles, scope_revisions, usage_intervals, daily_rollups, quota_rules
duplicate_null_network_rollups: 2
invalid_interval_accepted: [-1.0, -1, 2, 1, null]
byte_types_accepted: [real, text]
Integer rollup overflow: integer overflow
Unsigned bind overflow: Python int too large to convert to SQLite INTEGER
```

This establishes specific design defects, not storage integration or recovery evidence. The memory database did not exercise WAL filesystem semantics, crashes, process commits or a C# codec. Such behaviour remains untested and must be described accordingly.

## Handoff

Reviewed the full PRD (all 32 sections) and all existing requirement, architecture, ADR, task, handoff and review documents. Inspected concrete provider/scope/collector/UI/research/performance source relevant to their claims. Independently performed map text comparison and isolated SQLite DDL probes; all were read-only with respect to repository implementation. No broad suite or Windows acceptance run is claimed by this workstream; other auditors/Director own those receipts.

Pending Director action: approve and consolidate findings, authorize the smallest source/design/document repairs, independently verify corrected technical contracts, then decide the audit gate. No V1 feature expansion was performed.
