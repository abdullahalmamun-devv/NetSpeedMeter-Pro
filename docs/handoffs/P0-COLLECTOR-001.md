# P0-COLLECTOR-001 handoff

TASK: P0-COLLECTOR-001 — Application Collector extraction, lifecycle state management, and bounded snapshot delivery.

STATUS: Review requested. Only the Project Director may accept application layer integration.

IMPLEMENTED:
- Created headless `Collector` in `src/SpeedMeter.Application/Collector.cs` with injected `IAdapterCounterProvider` and `IMeasurementClock`.
- Implemented explicit collector states: `Starting`, `Running`, `Paused`, `Suspended`, `Degraded`, `Failed`, `Stopped`.
- Implemented latest-only display snapshot pattern with monotonic sequence numbers, serializing all domain state updates without unbounded queues.
- Dispatched `Updated` consumer callbacks strictly outside the internal synchronization lock, preventing thread deadlocks between UI and collector workers.
- Refactored `src/SpeedMeter.App/MonitorSession.cs` to delegate cleanly to `SpeedMeter.Application.Collector`.
- Fixed namespace collision between `SpeedMeter.Application` and `System.Windows.Application` in `App.xaml.cs` and `MainWindow.xaml.cs`.
- Re-verified entire pipeline via live UI smoke test `scripts/smoke.ps1`: real WPF dashboard and widget rendered, snapshots matched, and pause/resume lifecycle verified.

FILES CHANGED:
- `src/SpeedMeter.Application/Collector.cs`
- `src/SpeedMeter.Application/MeasurementClock.cs`
- `src/SpeedMeter.App/MonitorSession.cs`
- `src/SpeedMeter.App/App.xaml.cs`
- `src/SpeedMeter.App/MainWindow.xaml.cs`
- `docs/handoffs/P0-COLLECTOR-001.md`

DESIGN DECISIONS:
- Zero WPF or UI dependencies in `SpeedMeter.Application`.
- Collector handles provider failures gracefully: unexpected exceptions yield `CollectorState.Failed` and rebaseline on recovery rather than crashing the background worker.
- Display snapshot holds single latest reference (`CollectorSnapshot Latest`), preventing memory growth during UI lag or slow render ticks.

TESTS EXECUTED:
- `scripts/build.ps1`: 0 warnings, 0 errors across 5 projects.
- `scripts/check.ps1 -Windows`: 44/44 checks passed.
- `scripts/smoke.ps1`: 8/8 valid readings, snapshot matches, units match, pause preserves totals, reset while paused works, resume is live.

ACTUAL RESULTS:
- Complete end-to-end pipeline functioning: Real Windows Counter Provider -> Headless Collector -> Measurement Domain -> Immutable Snapshot -> WPF UI.

KNOWN LIMITATIONS:
- Single in-process architecture for P0. Persistent storage (SQLite) is not yet wired to interval snapshots (queued for V1.0 data wave).

RISKS:
- UI thread stalls must not block collector sampling (guaranteed by background task and lock-free notifications).

RECOMMENDED NEXT STEP:
- Architect and QA review `P0-COLLECTOR-001`. Director approves and integrates.
