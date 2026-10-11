# V1-CLOCK-SHUTDOWN-TRUTH

| Field | Assignment |
|---|---|
| Release | V1.0 production-truth closure; Wave 4 remains gated |
| PRD references | §7.2–7.4, §22.1–22.2, AC-M05, AC-M08, AC-S05 |
| Dependencies | Accepted Gate A/B/C work and V1 core-truth commit `2c8e337`; reproducible pre-edit baseline |
| Objective | Make lifecycle elapsed time monotonic, represent UTC clock discontinuities without invented calendar spans, and surface normal-shutdown persistence failures. |
| Out of scope | Connectivity, ping, alerts, onboarding, installer, updater, attribution, new visual features. |

## Assignments

| Task | Owner | Reviewer | Scope and files | Acceptance and tests | Handoff |
|---|---|---|---|---|---|
| V1-CLOCK-001 | Network Measurement / Application | Architect + QA + Director | `Collector.cs`, `MeasurementClock.cs`; clock anchors, gap markers, segment rotation, missed-suspend fallback | Stable and adversarial UTC jumps retain monotonic duration; valid bytes never cross a discontinuity; Windows clock-source evidence | `docs/handoffs/V1-CLOCK-SHUTDOWN-TRUTH.md` |
| V1-CLOCK-QA-001 | QA | Director | `V1ClockShutdownFixtures.cs`, check registration | Independent UTC, monotonic, awake-time movement; segment/overlap/history and shutdown failure fixtures; full executable checks and smoke | `docs/handoffs/V1-CLOCK-SHUTDOWN-TRUTH.md` |
| V1-SHUTDOWN-001 | Project Director / Application + Storage | Architect + QA | `Collector.cs`, `ApplicationContext.cs`, `SqliteStorageEngine.cs`, `MonitorSession.cs`, `MainWindow.xaml.cs` | Bounded collector storage barrier; truthful structured failure result, visible quit warning, no clean success assertion on injected gap/end/flush failures | `docs/handoffs/V1-CLOCK-SHUTDOWN-TRUTH.md` |
| V1-HISTORY-CLOCK-001 | Project Director / Storage + Domain | Architect + QA | `HistoricalRange.cs`, `SqliteStorageEngine.cs`, History/Overview ViewModels | Unmappable UTC span is an event marker; affected history reports clock uncertainty without a fabricated percentage; recorded bytes conserved | `docs/handoffs/V1-CLOCK-SHUTDOWN-TRUTH.md` |

## Gate

The Director alone decides integration and whether this closure passes after reviewing source, independent fixtures, native Windows evidence, and documented limitations. A successful build alone is insufficient.
