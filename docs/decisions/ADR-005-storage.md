# ADR-005: Local usage ledger and storage ownership

## Status

Accepted — Formally approved by Project Director following V1 Foundation Wave 1 implementation and verification on 11 October 2026.

Implementation completed in `SpeedMeter.Storage`:
- Package: `Microsoft.Data.Sqlite 9.0.2` and security-patched `SQLitePCLRaw.bundle_e_sqlite3 2.1.12` (resolving GHSA-2m69-gcr7-jv3q).
- Architecture: Dedicated `SpeedMeter.Storage` project with `SqliteStorageEngine` owning a single background writer consuming a bounded `Channel<IStorageCommand>`.
- SQLite pragmas: `PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;`.
- Lossless counter BLOBs: `BigEndianCodec` provides exact 8-byte big-endian encoding and decoding for full 64-bit unsigned `ulong` provider counters.
- Integer capacity guards: `CheckedIntegerMath` enforces checked signed 64-bit bounds and checked application-side summation (`CheckedAdd`) preventing SQLite `SUM()` overflow.
- Schema integrity: Versioned migrations (`SqliteMigrations`) enforce `STRICT` tables with foreign keys and compound primary keys. The nullable-network SQLite issue was resolved by using sentinel network profile row `'unidentified'` with `network_profile_id TEXT NOT NULL`.
- Lifecycle & recovery: Session records, clock segment records, scope revision records, and explicit gap records (`user_paused`, `system_sleep`, `unclean_shutdown`).
- Resilience: If storage fails or is closed, engine degrades to `StorageHealth.Degraded` without crashing the collector or dropping live sampling.
- Verified by automated fixtures: `V1-STORE: 8-byte big-endian BLOB codec roundtrips full 64-bit unsigned range losslessly`, `V1-STORE: CheckedIntegerMath enforces signed capacity guards and prevents overflow`, `V1-STORE: Daily rollup uniqueness enforces compound PRIMARY KEY without null duplicate bug`, `V1-STORE: Exact bytes, gap records, and clean session metadata survive process restart`, `V1-LIFECYCLE: Unclean shutdown is detected and recorded as a crash gap on restart`, `V1-INTEGRATION: Collector + RouteProvider + StorageEngine end-to-end persists exact interval bytes and checkpoints`, and `V1-RESILIENCE: Storage degradation does not crash collector or drop live observations`.

Superseded claim history preserved: originally proposed in P0, audited on 11 October 2026 as designed but not implemented, now fully implemented in `SpeedMeter.Storage` with automated test evidence and accepted.

## Context

PRD 22 proposes SQLite, WAL, foreign keys, one writer, versioned migrations, integer bytes and UTC timestamps. Current totals live only in RAM and disappear on exit. Future history must preserve coverage and source boundaries, including network changes on one interface.

## Decision

Plan one local SQLite write owner with parameterized statements and transactional, idempotent interval ingestion. Preserve unique interval IDs, source/scope revision, monotonic duration, UTC mapping, coverage gaps and immutable time-bound network membership. Retain the PRD proposed `synchronous=FULL` and at-most-30-second batches as candidates until measured and approved. Acknowledge durable commit separately from in-memory acceptance. Migrations require rollback/recovery evidence before use on valuable data.

Unsigned provider counters exceed SQLite's signed INTEGER range. The revised draft chooses fixed eight-byte big-endian BLOB checkpoints, guarded signed interval storage and checked capacity failure instead of truncation. Queries accumulate signed rows in exact arbitrary-precision application arithmetic; a cached signed rollup is written only when it fits. Over-capacity data produces an explicit history-not-saving/gap state while live accounting remains separate. These policies are design requirements, not an implemented codec/writer. Never unchecked-cast `ulong` or store accounting bytes as floating point. Keep aliases/reporting merges separate from original observations.

## Alternatives

JSON files for a long-lived ledger lack the required transactional/query behavior. Multiple ad hoc UI writers violate ownership. Persisting rounded UI totals breaks reconciliation. A live WAL database on a network share is outside the proposed supported storage contract.

## Consequences

Storage adds dependency/patch and migration responsibilities; empty NuGet sources must be intentionally revisited. Batched durability entails an explicit possible uncommitted span on crash; disclose it and preserve gaps. Display updates may be coalesced; accounting intervals may not be silently dropped.

## PRD Impact

Sections 21.4 and 22, history/quota/export contracts. A final schema must explicitly resolve interval-to-network membership omitted from the conceptual `UsageInterval` row rather than reconstructing it from mutable adapter state.

## Validation Required

Select/review patched SQLite package; exact raw/rollup reconciliation; unsigned range boundaries; duplicate replay; DST/timezone/clock segments; disk full/unwritable database; crash during batch/migration/compaction; backup/recovery; one writer and long-lived reader pressure; resource/retention benchmarks. No durability claim before these tests.
