# SpeedMeter Pro — Storage Architecture Draft

Date: 11 October 2026 (Asia/Dhaka). Owner: Auditor A/J for the authorized P0 audit repair. Reviewer: Audit Director + QA. Status: **PROPOSED — DESIGNED / NOT YET IMPLEMENTED**.

No Storage project, SQLite package, database writer, production migration or recovery integration exists. This is a corrected prerequisite design; no V1 persistence feature is declared Done. The independent audit records the superseded Accepted specification, original DDL failures and observed limitations in [REQUIREMENTS-ADR-AUDIT.md](../audit/REQUIREMENTS-ADR-AUDIT.md). Prior handoff/review approval is historical and is superseded by this review request.

## Ownership, bounds and durability

One future background owner performs writes with parameterized commands and transactional idempotent ingestion. The command channel must have a configured capacity; display coalescing cannot drop accounting. Saturation, disk-full, cancellation or capacity failure must enter history-not-saving, preserve bounded live operation and record missing durable coverage. No synchronous DB operation runs on the UI or collector command lock.

Read connections use bounded, cancellable transactions. A five-second busy timeout is a candidate bound, not a solution to checkpoint starvation; writer/checkpoint policies and WAL growth thresholds need measurement. Configure incremental vacuum before creating a new database, with migration-specific handling for an existing database.

Proposed local-filesystem connection policy:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA auto_vacuum = INCREMENTAL;
```

FULL preserves the PRD 22.1 proposed durability baseline. An alternative requires measured results and a recorded Director/product decision. WAL/NORMAL can lose committed transactions after power/system failure; consistency and durability are distinct. [SQLite synchronous documentation](https://www.sqlite.org/pragma.html#pragma_synchronous)

A commit acknowledgment means a successful transaction under the configured durability policy. In-memory collection acceptance is separate. Batch deadline is at most 30 seconds, not merely 30 samples; pause/normal shutdown flush best-effort through the writer within a bounded deadline. Suspend does not guarantee time to flush. Disclose uncommitted loss and preserve the original database when corruption is detected. No unconditional zero-corruption guarantee is made.

## Exact integer range policy

Provider checkpoints use fixed **eight-byte big-endian BLOBs**, with a checked length guard. No alternate high/low encoding remains implicit. A future codec must round-trip 0, Int64.MaxValue, 2^63 and UInt64.MaxValue; it is not implemented by this specification.

Raw validated interval directions use signed INTEGER only after checked range validation. If an interval exceeds Int64.MaxValue, the storage boundary rejects the whole interval atomically as a declared capacity failure, retains live-domain unsigned totals, and marks unavailable durable coverage. It must never wrap, truncate, substitute zero or commit one direction only. This explicit unsupported-capacity policy must be reviewed before promising complete historical accounting for arbitrary synthetic unsigned intervals. A future lossless splitting policy would require its own interval-identity and coverage-once review; none is implicitly implemented here.

All report totals are accumulated using exact arbitrary-precision application arithmetic over validated rows. SQLite SUM may overflow even when every row fits. Cached rollups are persisted only after checked range validation; over-capacity cached summaries remain unavailable and raw valid rows are preserved for exact queries. Never use REAL or total() to bypass byte overflow. [SQLite aggregate documentation](https://www.sqlite.org/lang_aggfunc.html)

## Proposed core ledger DDL

This DDL is a **draft for isolated schema review**, not a complete V1 production migration. It specifies the prerequisites for a selected adapter ledger. Later quota/alert/settings/app/probe/test datasets remain outside this DDL until their own contracts and release work. SQLite build/package/version support remains undecided.

```sql
CREATE TABLE schema_migrations (
    version INTEGER NOT NULL PRIMARY KEY CHECK(version > 0),
    migration_id TEXT NOT NULL UNIQUE,
    checksum TEXT NOT NULL,
    applied_at_utc_ms INTEGER NOT NULL
) STRICT;

CREATE TABLE collection_sessions (
    session_id TEXT NOT NULL PRIMARY KEY,
    monotonic_epoch TEXT NOT NULL,
    started_at_utc_ms INTEGER NOT NULL,
    ended_at_utc_ms INTEGER,
    stop_reason TEXT,
    ended_cleanly INTEGER NOT NULL DEFAULT 0 CHECK(ended_cleanly IN (0,1))
) STRICT;

CREATE TABLE clock_segments (
    segment_id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
    monotonic_anchor_ticks INTEGER NOT NULL CHECK(monotonic_anchor_ticks >= 0),
    utc_anchor_ms INTEGER NOT NULL,
    ticks_per_second INTEGER NOT NULL CHECK(ticks_per_second > 0),
    timing_confidence TEXT NOT NULL,
    change_reason TEXT
) STRICT;

CREATE TABLE network_profiles (
    profile_id TEXT NOT NULL PRIMARY KEY,
    friendly_name TEXT NOT NULL,
    os_profile_key TEXT,
    identity_confidence TEXT NOT NULL,
    created_at_utc_ms INTEGER NOT NULL
) STRICT;

-- Explicit unresolved bucket; reporting aliases never change original interval identity.
INSERT INTO network_profiles VALUES('unidentified','Unidentified network',NULL,'unresolved',0);

CREATE TABLE scope_revisions (
    revision_id TEXT NOT NULL PRIMARY KEY,
    scope_id TEXT NOT NULL,
    revision_number INTEGER NOT NULL CHECK(revision_number > 0),
    scope_mode TEXT NOT NULL,
    source_method TEXT NOT NULL,
    source_version TEXT NOT NULL,
    boundary_definition TEXT NOT NULL,
    validity TEXT NOT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    UNIQUE(scope_id, revision_number)
) STRICT;

CREATE TABLE scope_membership (
    membership_id TEXT NOT NULL PRIMARY KEY,
    scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
    interface_id TEXT NOT NULL,
    observed_incarnation TEXT,
    network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
    session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
    starts_at_monotonic_ticks INTEGER NOT NULL CHECK(starts_at_monotonic_ticks >= 0),
    ends_at_monotonic_ticks INTEGER,
    CHECK(ends_at_monotonic_ticks IS NULL OR ends_at_monotonic_ticks >= starts_at_monotonic_ticks)
) STRICT;

CREATE TABLE counter_checkpoints (
    checkpoint_id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
    interface_id TEXT NOT NULL,
    source_method TEXT NOT NULL,
    observed_source_epoch TEXT,
    observed_sequence INTEGER NOT NULL CHECK(observed_sequence >= 0),
    monotonic_ticks INTEGER NOT NULL CHECK(monotonic_ticks >= 0),
    observed_at_utc_ms INTEGER NOT NULL,
    counter_rx BLOB NOT NULL CHECK(length(counter_rx) = 8),
    counter_tx BLOB NOT NULL CHECK(length(counter_tx) = 8),
    UNIQUE(session_id, interface_id, source_method, observed_sequence)
) STRICT;

CREATE TABLE usage_intervals (
    interval_id TEXT NOT NULL PRIMARY KEY,
    source_interval_key TEXT NOT NULL UNIQUE,
    session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
    clock_segment_id TEXT NOT NULL REFERENCES clock_segments(segment_id),
    scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
    network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
    start_utc_ms INTEGER NOT NULL,
    end_utc_ms INTEGER NOT NULL,
    monotonic_duration_ticks INTEGER,
    bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
    bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
    is_coarse INTEGER NOT NULL DEFAULT 0 CHECK(is_coarse IN (0,1)),
    is_gap INTEGER NOT NULL DEFAULT 0 CHECK(is_gap IN (0,1)),
    gap_reason TEXT,
    timing_confidence TEXT NOT NULL,
    CHECK(monotonic_duration_ticks IS NULL OR monotonic_duration_ticks >= 0),
    CHECK((is_gap = 0 AND gap_reason IS NULL AND monotonic_duration_ticks IS NOT NULL AND monotonic_duration_ticks > 0)
       OR (is_gap = 1 AND gap_reason IS NOT NULL AND bytes_rx = 0 AND bytes_tx = 0))
) STRICT;

CREATE INDEX idx_intervals_scope_time ON usage_intervals(scope_revision_id,start_utc_ms,end_utc_ms);
CREATE INDEX idx_intervals_network_time ON usage_intervals(network_profile_id,start_utc_ms,end_utc_ms);

CREATE TABLE daily_rollups (
    start_utc_ms INTEGER NOT NULL,
    end_utc_ms INTEGER NOT NULL,
    reporting_timezone TEXT NOT NULL,
    calendar_rule_revision TEXT NOT NULL,
    scope_id TEXT NOT NULL,
    network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
    bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
    bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
    valid_duration_ms INTEGER NOT NULL CHECK(valid_duration_ms >= 0),
    gap_duration_ms INTEGER NOT NULL CHECK(gap_duration_ms >= 0),
    peak_rx_interval_id TEXT,
    peak_tx_interval_id TEXT,
    CHECK(end_utc_ms > start_utc_ms),
    PRIMARY KEY(start_utc_ms,end_utc_ms,reporting_timezone,calendar_rule_revision,scope_id,network_profile_id)
) STRICT;
```

STRICT and explicit NOT NULL avoid the original affinity/nullable-key failure. Every unresolved grouping uses the same real unresolved bucket key. Gap bytes of zero are a representation sentinel; is_gap excludes that row from valid observed coverage and live rate derivation. They are never presented as measured zero traffic. [SQLite CREATE TABLE documentation](https://www.sqlite.org/lang_createtable.html)

Schema guards do not establish every invariant. The future writer must validate that a clock segment belongs to the interval session, checkpoints and membership match source/scope, source interval keys are stable across replay, and byte/coverage updates are idempotent in one transaction. Only Director/QA evidence can approve ingestion correctness.

## Time, membership, rollups and gaps

UTC times are integer milliseconds; monotonic durations are ticks with a per-session/segment frequency. A process restart starts a different monotonic epoch. Clock changes produce mapping segments and uncertainty records; never subtract wall time to calculate rates or invent cross-process monotonic duration. Uncertain startup gap duration may be NULL with explicit confidence. A backward wall-clock jump must not become negative duration or erase traffic.

Each observed membership binds an interface incarnation, network profile and session interval. A profile change on the same adapter closes old membership and opens a new one. A sample crossing a reporting/network boundary preserves exact directional bytes with deterministic integer remainder allocation and an estimated-boundary flag. Unsupported attribution uses the unidentified bucket and visible uncertainty. A multi-interface aggregate with different networks needs validated member allocation; one mutable current-profile join cannot reconstruct it. That aggregate design remains gated.

Archived rollups retain exact original UTC boundaries, reporting timezone and calendar-rule revision. Fine-detail expiration cannot produce exact new timezone or clipped custom boundaries; retain original grouping or label estimates. Weighted rates and peak interval provenance are retained independently from formatting. Minute/hour retention, transactional compaction/delete and bounded checkpoints are further design/validation work, not delivered code.

Sleep, pause, collector failure, unclean exit and storage-not-saving are distinct missing-observation reasons. Close the last valid interval before a known interruption, rebaseline afterward and exclude gaps from coverage. Startup recovery classifies clean/unclean absence from durable session state and the last committed boundary without claiming the precise crash time. No sleep gap is a network outage.

Future quota schema must preserve rule revision, saved timezone, anchor, direction, exact limit, cycle boundaries, opening balance, measured bytes and fired-threshold ledger. Settings import, retention/deletion and old-version recovery require separate schemas/tests. This draft does not silently declare those missing V1 requirements complete.

## Migration and recovery requirements

Create a consistent pre-migration snapshot using SQLite Online Backup API (or a separately validated consistent mechanism); never copy only an open WAL database file. Stop/serialize the writer, finish bounded readers, validate backup integrity/version, and retain the active original until migration outcome is known. Versioned migrations run transactionally. On failure preserve the old compatible database and app state; do not overwrite active files while connections/WAL sidecars remain live. Any restoration must stop/close owners, validate the snapshot and perform a tested atomic switch. [SQLite Backup API](https://www.sqlite.org/backup.html)

No crash, disk-full, migration, restore or power-loss test has been executed against a product storage implementation. Future evidence must use nonempty old-version data and include unsigned codec/range cases, duplicate replay, UTC/DST boundaries, long readers/checkpoint pressure and failed commit/backup/recovery. Only then can durability and integration claims be made.

