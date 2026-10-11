# ADR-011: Lifecycle clock discontinuity and shutdown truth

## Status

Accepted with limits for V1.0 code integration after Director, Architect, and QA review. The V1 clock/shutdown product gate remains blocked by the evidence and invariant limits below.

## Context

PRD §7.2 requires monotonic elapsed time for durations and UTC for history. The existing collector retained pause/suspend monotonic anchors but calculated persisted gap duration from UTC. A backward clock jump produced a one-millisecond substitute; a forward jump inflated missing elapsed time. Shutdown also discarded persistence exceptions in `Dispose()`.

## Decision

- `MeasurementClock` reads .NET `Stopwatch` (Windows QPC) for monotonic elapsed time and `DateTimeOffset.UtcNow` for calendar placement. On Windows it also reads unbiased interrupt time, which excludes suspend, to detect a missed power notification. The precise API is attempted first, then the standard unbiased API. Missing awake-time capability is explicit (`null`).
- A stable pause or suspend gap stores its observed UTC endpoints and a separate duration derived from monotonic endpoints. Explicit power events close the sleep gap and rebaseline counters. A material excess of QPC time over awake time infers an unobserved sleep when no event arrived; the first post-resume counter observation establishes a baseline and no crossing delta is ingested.
- If UTC and monotonic elapsed time materially disagree, the gap is a zero-width historical event at its observed start UTC, with its actual monotonic duration and `utc_discontinuous` confidence. It does **not** claim a calendar-duration span. A new `ClockSegmentRecord` starts at the observed end UTC and monotonic anchors. Subsequent valid readings use that segment and a new counter baseline.
- Affected history queries report `ClockUncertain`, set `IsEstimated`, retain separately measured bytes, and suppress a precise coverage percentage in the UI. Observed UTC coverage duration is a union of labeled spans, so repeated UTC labels do not double-count the displayed duration.
- Normal shutdown admits any active gap, confirms it through a flush barrier, admits the clean session end, then confirms a second flush. The collector retains `CollectorShutdownResult` and traces failures for diagnostics. The collector storage phase uses a two-second deadline, including queue admission.

## Limits and non-claims

- A backward UTC jump can give two distinct, valid observations identical UTC labels. Exact bytes, truthful observed UTC placement, and globally non-overlapping UTC rows cannot all be guaranteed simultaneously. Clock segments disambiguate them; history marks the affected calendar range uncertain. No bytes are merged or fabricated to force global UTC disjointness.
- UTC boundaries within an unobserved clock-change gap are unknown. A zero-width marker is an event, not a claim that the lost observation lasted zero milliseconds.
- Clock source calls are sequential, not atomic. Short sleeps below the detection threshold and missing unbiased-clock capability rely on explicit power events. Hardware sleep/hibernate transitions and Windows 11 compatibility require controlled lab evidence.
- The collector's storage barrier is bounded; `ApplicationContext` and the SQLite writer's final disposal are not yet covered by an overall process-exit deadline.
- The Quit path does not currently display the retained shutdown result after the window closes. An attempted modal warning blocked unattended WPF smoke and was removed. A bounded user-facing diagnostic path remains open.
- A timezone-only reporting change does not alter `UtcNow` and needs separate reporting-revision evidence under AC-M08.
