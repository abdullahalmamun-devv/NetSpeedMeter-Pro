# P0-ARCH-001 handoff

TASK: P0-ARCH-001 — architecture assessment and decision drafts.

STATUS: Review requested. Only the Project Director may accept architecture or declare integration.

IMPLEMENTED:
- Inspected complete baseline source, projects and build/test/smoke scripts; read PRD measurement, scope, architecture, storage and release-gate contracts.
- Documented real baseline, dependency rules, minimal provider/clock/collector seams, findings, and future persistence/identity constraints.
- Created ADR-001 and draft ADR-002 through ADR-007; each is Proposed with evidence and required validation separated.
- Sent early contract advice to Measurement Engineer and Director; no production code changed by Architect.

FILES CHANGED:
- `docs/architecture/README.md`
- `docs/decisions/ADR-001-runtime-ui.md`
- `docs/decisions/ADR-002-traffic-counter-provider.md`
- `docs/decisions/ADR-003-measurement-scope.md`
- `docs/decisions/ADR-004-taskbar-docked-strategy.md`
- `docs/decisions/ADR-005-storage.md`
- `docs/decisions/ADR-006-collector-ui-boundary.md`
- `docs/decisions/ADR-007-app-attribution.md`
- `docs/handoffs/P0-ARCH-001.md`

DESIGN DECISIONS:
- Recommended retaining existing Core/Windows/App and adding Application only for the real collector extraction. No unnecessary domain, infrastructure, IPC or helper projects.
- Proposed typed provider outcomes, separate lifecycle/measurement states, exact source/session/epoch semantics, latest-only display and loss-aware future accounting.
- Identified stale queued scope labels, UI-delivery chart timing, provider generation limitations and singleton identity limits.
- All decisions require Director acceptance; release scope remains unchanged.

TESTS EXECUTED:
- Source and PRD inspection; documentation structure and links checked after creation.
- No code build, executable fixture run, Windows experiment or benchmark executed by this documentation task. Existing validation is cited as historical evidence only.

ACTUAL RESULTS:
- Baseline is a genuine native counter-to-widget prototype with important missing P0 evidence; no invented production values found in inspected pipeline.
- No release gate declared passed. Findings are baseline observations to re-review after concurrent repairs.

KNOWN LIMITATIONS:
- Architecture has not yet reviewed the final concurrent Measurement/Windows/Application implementations.
- Runtime patch support, Windows 11 matrix, topology, taskbar, app attribution, storage durability, accessibility, performance and soak evidence remain open.

RISKS:
- Unknown provider source generation must not be promoted to a proven OS recreation detector.
- UI coalescing must not discard accounting; snapshots must not mix old readings with newly selected identity.
- Provider `ulong` counters require an explicit checked SQLite representation before persistence.

RECOMMENDED NEXT STEP:
- Director reviews Proposed ADRs and assigns final names/ownership for collector/provider seams; Architect peer-reviews Measurement changes, then collector/Windows integration. QA independently executes fixtures and records actual results.
