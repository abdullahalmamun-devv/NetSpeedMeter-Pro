# P0-PERSISTENCE-DESIGN handoff

TASK: P0-PERSISTENCE-DESIGN — SQLite storage contracts, schema DDL, ulong representation, and migration architecture.

STATUS: Review requested. Only the Project Director may accept storage contracts.

IMPLEMENTED:
- Authored production storage specification in `docs/architecture/STORAGE-ARCHITECTURE.md`:
  - **Concurrency & Ownership:** Single dedicated background writer thread reading from a thread-safe `Channel`, multiple concurrent read-only WAL readers.
  - **Pragmas:** Configured `journal_mode = WAL`, `synchronous = NORMAL`, `foreign_keys = ON`, `busy_timeout = 5000`.
  - **Lossless ulong Handling:** Fixed-width 8-byte blob serialization for cumulative lifetime counters, signed 64-bit SQLite integer for interval deltas (capacity > 9 Exabytes per interval). Floating-point byte storage explicitly banned.
  - **Production DDL:** Complete table schemas for `schema_migrations`, `network_profiles`, `scope_revisions`, `usage_intervals`, `daily_rollups`, and `quota_rules`.
  - **Gap Representation:** Explicit `is_gap = 1` rows for sleep/resume, user pause, and unclean shutdown spans, preserving coverage boundary integrity without fabricating zero traffic.
  - **Rollup Invariants:** Rollups computed strictly from raw unrounded integer sums.
  - **Migration Safety:** Transactional migrations with pre-migration database snapshot backup and clean rollback.

FILES CHANGED:
- `docs/architecture/STORAGE-ARCHITECTURE.md`
- `docs/tasks/P0-PERSISTENCE-DESIGN.md`
- `docs/handoffs/P0-PERSISTENCE-DESIGN.md`

DESIGN DECISIONS:
- Adopts dedicated single writer thread to eliminate SQLite write-lock contention.
- Uses SQLite WAL mode to support fluid UI querying without blocking accounting writes.
- Enforces strict foreign keys between usage intervals and scope revisions.

TESTS EXECUTED:
- DDL review and mathematical constraint verification against SQLite specifications.

ACTUAL RESULTS:
- Complete storage architecture delivered and aligned with PRD §§21–22.

KNOWN LIMITATIONS:
- Native SQLite library integration (e.g. `Microsoft.Data.Sqlite`) will be performed during V1.0 Wave 1.

RECOMMENDED NEXT STEP:
- Architect and Director review `P0-PERSISTENCE-DESIGN`. Director updates ADR-005 to Accepted.
