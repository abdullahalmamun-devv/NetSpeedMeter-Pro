# P0 scope and decisions

This file tracks implementation against PRD revision 2.0. The PRD remains the release scope authority. Starting with a small vertical slice does not defer any V1 Must requirement.

The first slice builds and runs. See `VALIDATION.md` for the 15 passing core/Windows checks and real UI smoke results. Remaining checkboxes below are intentionally open.

## Decisions for the first slice

| Decision | Rationale / consequence |
|---|---|
| Use .NET 10, WPF, x64 | Implements proposed D04 baseline; footprint still needs measurement. SDK 10.0.401 is pinned. |
| Use GetIfTable2 interface counters | Real standard-user observations with explicit boundaries. Native structure size is checked and a managed API cross-check verifies layout/counters. |
| Require explicit adapter selection | Auto routes/VPN topology are not yet proven. No false default-route or deduplicated-total claim. |
| Floating widget first | Taskbar-adjacent geometry must pass D03; this build makes no docking claim. |
| In-memory session only | Persistence semantics will be designed and tested before quotas/history. |
| No third-party packages yet | Reproducible offline restore with the pinned SDK; SQLite follows the persistence decision. |
| No repository hosting or publication | All work is local. |

## Remaining P0 gates

- [ ] IPv4/IPv6 default-route selection, full/split VPN and virtual-switch tests.
- [ ] Safe taskbar-adjacent placement and floating fallback on the target Windows matrix.
- [ ] ETW feasibility: TCP/UDP, IPv4/IPv6, PID lifetime, permissions, event loss and independent completeness.
- [ ] Reference CPU/RAM/startup measurement including all processes.
- [ ] Real sleep/hibernate, adapter removal, Explorer restart and mixed-DPI recovery tests.
- [ ] Confirm supported Windows builds, owners and architecture decisions.

## Next implementation sequence

1. Complete counter/scope and lifecycle feasibility with recorded Windows evidence.
2. Decide taskbar strategy and run the optional app-attribution feasibility spike.
3. Add SQLite interval/coverage ledger with exact aggregation, crash recovery and explicit network membership.
4. Add history, quotas and exports; then the quick panel and remaining V1 UX.
5. Add consented probes, signed distribution and full release QA.

## PRD clarifications to resolve before affected features

- Sampling/window minimums must be compatible: 5-second probing cannot supply 20 attempts in 60 seconds; 10-second probing cannot supply 60 attempts in five minutes. Validate the setting combinations rather than allowing perpetual warming-up states.
- Persist time-bound network membership for intervals before building per-network reports.
- App attribution needs both mapping accuracy and independent collection-completeness evidence.
- Split acceptance cases spanning releases, and map feature IDs to tests and evidence.

## Current limitations

No persisted gaps/history or wall-clock mapping segments exist yet. The current session engine excludes known interruptions from accounting and uses a monotonic clock for rates. The graph is a bounded recent view. Widget position and settings are not persisted. Fullscreen auto-hide, click-through, high-contrast theme adaptation and complete accessibility review remain work. A second launch explains how to reopen the existing tray instance; it does not yet activate the first instance through IPC.
