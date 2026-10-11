# P0-PERSISTENCE-DESIGN

TASK ID: P0-PERSISTENCE-DESIGN
OWNER: Storage / Data Engineer + Software Architect
REVIEWER: QA Engineer + Project Director
PRD REFERENCES: F08, F09, F10; AC-U01–U06; §§21.4, 22
RELEASE: P0 feasibility
DEPENDENCIES: P0-ARCH-001, P0-MEASURE-001, P0-SCOPE-001
OBJECTIVE: Finalize production storage architecture, SQLite schema DDL, WAL transaction boundary, ulong representation, and migration safety before V1 implementation
IN SCOPE:
- Detailed schema DDL for intervals, scope revisions, network profiles, rollups, and quotas
- Lossless 64-bit unsigned representation resolving SQLite signed INTEGER limitation
- Single-writer queue and WAL transaction batching policy
- Explicit coverage gap recording (crash, sleep, pause)
- Rollup aggregation invariants (exact unrounded integer summation)
- Migration safety and rollback design
OUT OF SCOPE:
- SQLite package installation and binary compilation (belongs to V1.0 data wave)
EXPECTED FILES:
- docs/architecture/STORAGE-ARCHITECTURE.md
- docs/handoffs/P0-PERSISTENCE-DESIGN.md
- docs/reviews/P0-PERSISTENCE-DESIGN.md
ACCEPTANCE CONDITIONS:
- Schema satisfies all PRD Section 22 tables and foreign key constraints
- Lossless handling of ulong counters without floating-point conversion
- Clear gap and network profile mapping semantics
STATUS: In progress
