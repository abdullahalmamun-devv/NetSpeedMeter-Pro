# P0 architecture assessment

Owner: Software Architect. Reviewer: Project Director. Task: P0-ARCH-001.
Date: 11 October 2026. Status: Proposed; awaiting Director review. This is an assessment of the inspected first-slice baseline, with explicitly identified next changes. It does not declare P0 passed.

## Evidence and current structure

The assessment inspected all source/project files, build/check/smoke scripts, README, existing P0/validation notes, and PRD sections 7, 8, 21, 22 and 29. The prior validation report records a local Windows 10 build 19045 run; this assessment did not repeat that experiment. It is not Windows 11 evidence.

| Baseline component | Observed implementation | Boundary assessment |
|---|---|---|
| `SpeedMeter.Core` | `MeasurementEngine`, immutable `Reading`/`AdapterCounter` records, formatting | No WPF, native API, storage or network dependency; retain this boundary. |
| `SpeedMeter.Windows` | `GetIfTable2`, GUID identity, 64-bit octet counters, explicit native allocation cleanup | Real provider; assumes validated x64 layout. Exceptions currently express availability, and counter recreation cannot be proven from GUID alone. |
| `SpeedMeter.App` | WPF shell, floating widget, WinForms tray; `MonitorSession` owns collection | Works as a prototype composition; collector currently hardcodes provider/clock and belongs outside WPF. |
| `SpeedMeter.Checks` | Executable assertions and optional real Windows counter comparison | Useful genuine checks; not a discoverable unit-test adapter. A successful empty `dotnet test` is not evidence of tests executing. |
| Storage / helper / IPC | Absent | Planned; do not scaffold empty subsystems or claim durability/isolation. |

Baseline measurements use explicit single-interface selection. No automatic route selection, aggregation, internet-only classification, persisted usage, quota, ETW provider or docked surface exists. Retain existing code rather than rewriting the product into a larger framework.

## Proposed structure and dependency rules

```text
SpeedMeter.App          -> Application, Windows, Core
SpeedMeter.Application  -> Core
SpeedMeter.Windows      -> Core
SpeedMeter.Core         -> framework-only primitives
Future Storage         -> Core / narrowly required Application contracts
Tests                  -> components under test
```

Add `SpeedMeter.Application` only to house the actual collector, clock seam and delivery lifecycle being extracted. Domain and application remain logical responsibilities; an additional Domain project is unnecessary at this size. App is the composition root that selects a real provider and passes it into the collector. Core owns measurement records, arithmetic and provider-neutral contracts. Windows owns native error translation, layout checks and platform identity. Application owns scheduling, scope commands and publication. App renders immutable state and invokes commands. Storage owns the eventual one-writer ledger; never make Core depend on SQLite.

No dependency from Core/Application to WPF, `Dispatcher`, Windows-specific interop, UI text controls or concrete provider classes. No provider-specific Win32 errors interpreted by UI. UI formatting may remain a pure helper while presentation grows; it must never change accounting. Avoid a dependency injection framework, generic repository layer, event bus or process split until an actual need is demonstrated.

## First-slice contract requirements

The following describes semantics, not a second competing API implementation. Director owns final contract names and integration.

| Contract | Minimum semantics | Failure/testing seam |
|---|---|---|
| Adapter counter provider | Provider method/version, typed outcome, current immutable adapter samples, observed identity/generation confidence | Injectable fixture provider; no fabrication on failure. |
| Provider outcome | `Available`, `Partial`, `Unsupported`, `PermissionDenied`, `TemporarilyUnavailable`, `Failed`; safe diagnostic code/detail | Empty available inventory differs from denied enumeration. Partial data must identify which observations are safe to use. |
| Clock | UTC plus monotonic offset/epoch; monotonic source used for rates | Injected clock supports delays, nonfinite values, wall-clock reversal and lifecycle fixtures. Prefer framework `TimeProvider` if it meets the needed shape; no redundant clock wrappers. |
| Collector | `Starting`, `Running`, `Paused`, `Suspended`, `Degraded`, `Failed`; explicit start/stop and cancellation | Repeated start/dispose defined, pause/resume deterministic, exceptions cannot leave stale live status indefinitely. |
| Measurement output | Snapshot/session/measurement epoch identity; selected interface; provider source epoch where known; monotonic interval and UTC; raw exact totals/deltas; nullable rates; coarse/gap flags | Same snapshot gives all views the same underlying metrics. Invalid data never becomes idle zero. |
| Display delivery | One latest pending snapshot; immutable payload with selected identity and provider/collector state | Stalled UI cannot create an unbounded dispatcher queue or backpressure collection. |
| Future accounting delivery | Unique interval key, raw bytes, coverage and scope revision; serialized, loss-aware ingestion | Display coalescing never discards accounting records. Storage saturation produces an explicit gap/failure. |

Source counter generation, collection measurement epoch and user session are different identities. A user session may span pause/resume while retaining its valid totals; each resumed collection establishes a fresh baseline. A provider-generated epoch is evidence of what the provider observed, not a promise to detect an unobserved remove/recreate event. Unknown source epoch stays unknown. Changing selected adapter starts the existing transient session afresh; future historical scope definitions must remain immutable.

The provider read operation needs cancellation/deadline semantics. A synchronous native API may not support in-flight cancellation; document that limitation rather than suggesting a cancellation token can abort it. Avoid holding a collector command lock during external reads or invoking arbitrary subscribers under that lock. Capture a generation before a read and reject its result if scope/pause/reset changed while it ran.

## Baseline findings and acceptance for repairs

| Finding observed during initial inspection | Required response / owner |
|---|---|
| `MeasurementEngine` had no explicit snapshot/session/source epoch metadata; elapsed subtraction and coverage accumulation could become nonfinite despite finite inputs. | Measurement Engineer adds identities and atomic validation; Architect + QA review exact totals and discontinuity fixtures. |
| `MonitorSession` constructs concrete provider/Stopwatch, collapses availability into an unavailable text string, and uses paused state for suspend. | Director extracts collector seam and typed capability/lifecycle states; Windows owner maps actual outcomes. |
| Provider reads and `Updated` callbacks occur while holding the monitor lock. | Collector isolates native reads and subscriber delivery; fixtures exercise slow reads and concurrent commands. |
| `MainWindow.OnUpdated` enqueues every reading without a bound. | Latest-only delivery; test a stalled display consumer while measurement totals continue. |
| A queued old reading can use the newly selected adapter label. | Snapshot carries scope identity, and stale-generation updates are rejected; regression test switch with pending update. |
| `TrafficChart` uses UI delivery time from a separate Stopwatch. | Chart uses snapshot monotonic time, keeps actual gaps, and does not compress a dispatcher stall into fabricated timing. |
| Native GUID identity has no observed generation tracking; table size assertion is only part of ABI evidence. | Windows conformance records layout/identity limits, observed transitions, standard-user behavior; real reconnect/recreation remains a hardware test. |
| Singleton name uses `Environment.UserName`, not authenticated SID. | Do not use this as proof of user isolation. Before IPC/helper work, design authenticated user/session ACLs and SID-bound ownership. No current claim of a data leak. |
| Manual state and smoke logic sit in `MainWindow`; fixed colors and work-area placement are not full accessibility/multi-monitor compliance. | Preserve useful P0 surface; separate test orchestration when UI expands; required review and Windows matrix remain open. |

The original documentation is historical evidence, not proof the findings remain unfixed after this wave. Final integration status belongs in `IMPLEMENTATION_STATUS.md` and the task handoffs. Re-review the resulting files before closing any finding.

## Storage and future scope constraints

Before history implementation, preserve time-bound network membership at the raw interval or immutable mapping layer. Do not rebuild history by joining yesterday's interval to today's adapter profile. Keep identity aliases/reporting merges separate from raw accounting. SQLite INTEGER has a signed 64-bit range, unlike the provider's unsigned 64-bit counters: choose a checked schema policy explicitly; never cast a raw `ulong` into a negative SQL value. Interval deltas can use a validated signed range with a documented split or rejection policy; source checkpoints require a lossless representation.

Before auto scope, prove route selection across IPv4/IPv6, simultaneous egress, full/split VPN, Wi-Fi/Ethernet switch and virtual switch. Do not sum arbitrary interfaces. Before ETW, establish source completeness, loss reporting, PID creation identity and user/session isolation independently of per-observation attribution success.

## Decision register and validation

[ADR-001](../decisions/ADR-001-runtime-ui.md) records runtime/UI, followed by proposed decisions for [counter provider](../decisions/ADR-002-traffic-counter-provider.md), [scope](../decisions/ADR-003-measurement-scope.md), [taskbar](../decisions/ADR-004-taskbar-docked-strategy.md), [storage](../decisions/ADR-005-storage.md), [process boundary](../decisions/ADR-006-collector-ui-boundary.md), and [app attribution](../decisions/ADR-007-app-attribution.md).

All start Proposed. Only Director review can accept them, and acceptance of a P0 implementation choice does not waive remaining validation or advance a release gate. Build and executable fixtures verify the integrated changes; provider parity, real OS lifecycle, topology, taskbar, accessibility, resources and soak tests remain separate evidence.
