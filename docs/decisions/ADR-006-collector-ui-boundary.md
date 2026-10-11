# ADR-006: Collector and UI lifecycle boundary

## Status

Accepted — Project Director & Independent Audit review, 11 October 2026. Audit judgment: ACCEPTED. Headless collector separation from UI is verified. Concurrency defects MT-01 (subscriber exception isolation) and MT-02 (callback deadlock avoidance on dispose) were repaired and independently verified in test runner and UI smoke. Bounded latest-only UI snapshot queue preserves internal exact accounting.

Superseded claim preserved: previously “Accepted — P0 Gate review. Verified with headless SpeedMeter.Application.Collector, lock-free dispatch, and UI smoke test.” Calling subscribers outside a lock did not prove subscriber isolation or bounded worker/shutdown behaviour until MT-01 and MT-02 repairs were integrated and verified. See [audit](../audit/REQUIREMENTS-ADR-AUDIT.md).

## Context

PRD 21 permits P0 agent/domain/UI in one process if lifecycle/resource tests justify it. MonitorSession composes the headless Collector with a real Windows provider and injectable clock; dashboard close hides the window while collection continues. The read-only audit found the provider call under the command lock and synchronous subscribers capable of stopping/stalling the worker. Latest-only UI delivery and stale publication need their own regression evidence. Subsequent repair status belongs in the consolidated audit evidence, not in an assumed approval here.

## Decision

Keep one standard-user process for P0. Extract actual collector orchestration to an Application component with injected provider and clock, explicit lifecycle states and cancellable shutdown. Compose it in App. Serialize domain state changes; external reads and consumer callbacks must not hold the command lock. Reject reads superseded by a scope/lifecycle generation change. Publish immutable canonical snapshots through latest-only UI delivery while preserving exact accounting internally. Chart timing comes from observation time, not delayed UI delivery time.

## Alternatives

A separate agent process now adds IPC, packaging, security and resource overhead without current evidence of need. Keeping orchestration directly in the Window blocks deterministic headless lifecycle tests. Making the collector depend on Dispatcher solves threading at the expense of the architecture boundary.

## Consequences

No IPC security claim is needed for the current in-process boundary. UI/process crash still stops monitoring and creates a coverage gap; independent process resilience is unimplemented. If later evidence justifies a separate process, use versioned typed messages, authenticated user/session ACLs, bounded traffic and the existing domain semantics. The existing username-based mutex is not proof of authenticated user isolation.

## PRD Impact

21.2–21.4 and snapshot consistency in 7.5. Preserve collector `Suspended` separately from user `Paused`, and degraded provider availability separately from idle network traffic.

## Validation Required

Injected provider/clock tests for pause/suspend/reset/stop, blocked read and stale completion; repeated start/dispose; provider failure/recovery; latest-only display under dispatcher stall; no accounting loss; dashboard hidden lifecycle; shutdown bound; real sleep/resume and resource profiles. Separate-process decision remains open until evidence supports it.
