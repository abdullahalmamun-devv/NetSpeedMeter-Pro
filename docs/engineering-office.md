# Engineering office

The Project Director owns dependency order, assignments, review, integration and release gates. Specialists report evidence and limitations. An APPROVED review permits integration consideration; it does not itself declare a feature Done.

## Role roster

| Department | Activation / ownership |
|---|---|
| Product / PRD | Feature, release and acceptance traceability; `docs/requirements` |
| Architecture | Contracts, dependency boundaries and ADR proposals; `docs/architecture`, `docs/decisions` |
| Windows Platform | Native APIs, routes, identity, shell, sessions and capability outcomes |
| Network Measurement | Counter epochs, integer deltas, coverage, clocks, snapshots and domain fixtures |
| Storage / Data | Activate after P0 contracts: SQLite, migrations, rollups, recovery, billing/export |
| Desktop UI / UX | Snapshot consumers, widget/quick panel/dashboard, settings and interaction |
| Accessibility | Independent keyboard/Narrator/contrast/DPI/localization review |
| QA / Test | Independent correctness fixtures, integration/recovery/load evidence |
| Security / Privacy | Privilege/user isolation, request inventory, IPC/import/update review |
| Performance / Reliability | Measured overhead, bounded queues, startup and long-run evidence |
| Release / Build | Reproducible build, packaging/signing/update/uninstall evidence |
| Documentation / Maintenance | Board/status, limitations, architecture/testing/release records |

Four concurrent slots are available including the Director. First wave: Product, Architect, Measurement. Windows, QA and Docs are queued and assigned as capacity becomes available. Independent tasks run concurrently; shared source/contract changes are serialized.

## Assignment format

Each file in `docs/tasks/` records TASK ID, OWNER, REVIEWER, PRD REFERENCES, RELEASE, DEPENDENCIES, OBJECTIVE, IN SCOPE, OUT OF SCOPE, EXPECTED FILES, ACCEPTANCE CONDITIONS, TEST REQUIREMENTS, OUTPUT, STATUS. The board is the concise current-state index.

## Handoff and review

`docs/handoffs/` records TASK, STATUS, IMPLEMENTED, FILES CHANGED, DESIGN DECISIONS, TESTS EXECUTED, ACTUAL RESULTS, KNOWN LIMITATIONS, RISKS, RECOMMENDED NEXT STEP. `docs/reviews/` records APPROVED or CHANGES REQUESTED with concrete file/test evidence.

Measurement and Windows changes need Architect + QA review. Database work needs QA; privilege/IPC/import/update work also needs Security. UI needs Accessibility + Product. The Director independently inspects source/evidence and runs integration checks.

## Evidence and progression

Use real Windows observations, deterministic fixtures, explicit error states and relevant performance/privacy/accessibility evidence. No silent Must deferrals. Preserve user-owned files and existing work. Experimental tooling remains separate from production providers. No production fixtures or fake readings. P0 remains open until its actual gate evidence exists.
