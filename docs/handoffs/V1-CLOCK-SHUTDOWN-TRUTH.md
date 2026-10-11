# V1 clock and shutdown truth handoff

**Task:** V1-CLOCK-SHUTDOWN-TRUTH
**Status:** Code integrated; product gate blocked; Wave 4 not started.
**Owners:** Network Measurement / Application, QA, Project Director.
**Reviewers:** Architect / Windows reliability, QA, Project Director.
**PRD:** §7.2–7.4, §22.1–22.2, AC-M05, AC-M08, AC-S05.

## Repository and baseline

Starting branch `main`, HEAD and `origin/main` both `2c8e3373a6c9b408cd9dccb46d6ed8f3bc791083`; working tree clean. Standard restore/check script initially failed before fixtures because the NuGet vulnerability service was unreachable (`NU1900` treated as error). An offline restore with `NuGetAudit=false` succeeded. The unchanged executable baseline then passed 183, skipped 1 intentional check, failed 0; WPF smoke passed 18/18.

## Implemented

- Pause/suspend gap duration now derives from monotonic anchors; observed UTC endpoints are used for stable historical placement.
- UTC discontinuities produce a zero-width gap event retaining monotonic duration and a successor `ClockSegmentRecord` anchored at observed resume time. Counter deltas crossing a detected jump are discarded and a new baseline is captured.
- Production `MeasurementClock` reads QPC elapsed time and Windows unbiased interrupt time. The latter excludes sleep, allowing a missed power notification to be inferred and the crossing byte delta excluded. The observed Windows 10 host needed the standard API fallback because the precise entry point was absent.
- History uses `ClockUncertain` for queries intersecting a discontinuity, retains exact recorded bytes, merges repeated UTC coverage spans, and suppresses a precise coverage percentage in Overview/History.
- Collector shutdown waits for a gap flush, then session-end flush, with one two-second storage deadline; failures remain in `CollectorShutdownResult` and Trace diagnostics. SQLite flush queue admission is bounded, persisted counters update after commit, and degraded status survives disposal.

## Files changed

`Collector.cs`, `MeasurementClock.cs`, `ApplicationContext.cs`, `HistoricalRange.cs`, `SqliteStorageEngine.cs`, `MonitorSession.cs`, `MainWindow.xaml.cs`, `HistoryViewModel.cs`, `OverviewViewModel.cs`, `V1ClockShutdownFixtures.cs`, `Program.cs`, README, board/status, ADR-011, task/review/handoff records.

## Tests and observed results

- Final Release solution build: 0 warnings, 0 errors, 6 projects.
- Final `scripts/check.ps1 -Windows` with process-local `NuGetAudit=false`: 197 PASS, 1 intentional SKIP, 0 FAIL, exit 0. The direct executable runner also passed. Fourteen new fixtures cover stable and jumping pause/suspend, repeated UTC labels, byte conservation, segment rotation, inferred sleep, pause-during-suspend precedence, native awake-clock availability, and gap/end/flush/hanging shutdown faults.
- Post-change `scripts/smoke.ps1`: exit 0, 18/18 native assertions true, artifact `artifacts/smoke-20261011-230102`.
- An earlier post-change smoke timed out because a modal quit warning blocked unattended exit. The warning was removed; structured result and Trace diagnostic remain. The successful rerun confirms the nonmodal path.
- Independent Architect/Windows reliability source review found no code integration blocker, with the limits below.

## Known limitations and next gate evidence

- A backward UTC jump can repeat valid timestamp labels across distinct clock segments. Preserving both exact measured byte streams and observed UTC labels necessarily creates numerical UTC overlap. ADR-011 marks affected queries uncertain and prevents coverage double counting; the handoff's literal global no-overlap requirement cannot be met without discarding bytes or fabricating timestamps.
- Real Windows sleep/hibernate and clock/timezone changes on hardware were not exercised. Windows 11 lanes are untested. The awake-time heuristic has a one-second threshold and clock reads are not atomic.
- Quit has a structured failure result and trace, but no bounded user-facing failure notification. `ApplicationContext`/SQLite writer final disposal has no overall two-second bound; the two-second claim applies to Collector's storage barrier only.
- AC-M05/M08/S05 and the clock/shutdown product gate remain open. Do not start Wave 4.
