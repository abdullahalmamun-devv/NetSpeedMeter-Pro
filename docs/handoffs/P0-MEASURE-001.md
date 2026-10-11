# P0-MEASURE-001 handoff

TASK: P0-MEASURE-001 — Core measurement engine, epoch model, and deterministic arithmetic fixtures.

STATUS: Review requested. Only the Project Director may accept measurement contracts or declare integration.

IMPLEMENTED:
- Implemented `MeasurementEngine`, `AdapterCounter`, `Reading`, `CounterEpoch`, `ReadingState`, and `MetricFormat` in `src/SpeedMeter.Core/Measurement.cs`.
- Strictly enforced monotonic time delta for rate calculations; wall-clock time (`utc`) is used purely as audit timestamp.
- Atomic 64-bit checked arithmetic for session counters: if either RX or TX overflows `ulong.MaxValue`, neither is committed and state transitions to `Discontinuity`.
- Negative deltas, counter resets, and source epoch transitions capture a new baseline and emit `Discontinuity` without generating spikes or accumulating unobserved intervals.
- Gaps and delays: elapsed intervals > 3× expected interval are flagged `Coarse`, preserving accumulated bytes without assuming regular pacing.
- Implemented decimal SI rate and byte formatting (1000 divisor: KB/s, MB/s, Gbps, etc.) per PRD §7.
- Developed 21 deterministic epoch and boundary fixtures in `tests/SpeedMeter.Checks/MeasurementFixtures.cs`, in addition to the 14 baseline checks in `Program.cs` (35 total checks).
- Mandatory fixture verified: 125,000,000 bytes over 10.0 seconds equals exactly 12,500,000 B/s (12.50 MB/s) and 100.00 Mbps.

FILES CHANGED:
- `src/SpeedMeter.Core/Measurement.cs`
- `tests/SpeedMeter.Checks/MeasurementFixtures.cs`
- `docs/handoffs/P0-MEASURE-001.md`

DESIGN DECISIONS:
- `CounterEpoch` struct tracks provider counter lifetime identity. When an adapter resets or is replaced, epoch change forces immediate rebaseline.
- `Reading` record is immutable with unique `SnapshotId` and session-scoped `MeasurementEpoch`.
- `MetricFormat` handles null and non-finite values safely returning em-dash ("—").

TESTS EXECUTED:
- Executed `scripts/check.ps1`: all 35 checks passed with exit code 0.
- Executed `scripts/check.ps1 -Windows`: all 36 checks passed with exit code 0.

ACTUAL RESULTS:
- 35/35 core checks pass deterministically without timing jitter or flake.
- All arithmetic edge cases (overflow, negative delta, non-finite inputs, zero interval) correctly rebaseline without state corruption.

KNOWN LIMITATIONS:
- Single-adapter scope is supported; multi-adapter aggregate and route-based auto-scope belong to subsequent tasks (`P0-SCOPE-001`).
- Per-application attribution domain is strictly separated and not mixed into adapter measurements.

RISKS:
- Downstream UI consumers must not attempt to round or smooth rates prior to accounting accumulation.

RECOMMENDED NEXT STEP:
- Architect and QA review `P0-MEASURE-001`. Director approves and integrates.
