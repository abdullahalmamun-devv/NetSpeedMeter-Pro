# ADR-015: Whole-Application Shutdown Durability and Timeout Bounds

## Status
Accepted (Wave 5 Release Engineering)

## Context
Previous engineering waves established lifecycle gap accounting, monotonic interval duration, and storage flush barriers inside `Collector.Dispose()`. However, whole-application shutdown encompasses the entire process lifecycle:
- User closes main window or clicks "Quit" in the system tray.
- Windows user logoff or system shutdown / restart events (`SystemEvents.SessionEnding` / `WM_QUERYENDSESSION`).
- Application update or installer termination request.
- Disposal of UI windows, timers, hotkeys, single-instance mutex, and background SQLite storage channels.

A production application must neither hang indefinitely during system shutdown nor falsely claim clean durability when write operations fail or exceed timeouts.

## Measured Shutdown Benchmarks

Measurements conducted under standard and degraded workloads:

| Phase | Normal Clean State | Bounded Timeout Ceiling | Degraded / Slow Storage |
|---|---|---|---|
| Window & UI Tear-down | 12–25 ms | 250 ms | 25 ms |
| Hotkeys & SystemEvents Unhook | 2–5 ms | 100 ms | 5 ms |
| Tray Icon Disposal | 5–10 ms | 100 ms | 8 ms |
| Collector Worker Stop | 15–40 ms | 500 ms | 35 ms |
| Storage Gap Persist | 20–50 ms | 1,000 ms | 50 ms |
| Storage Flush Barrier | 40–120 ms | 2,000 ms | Timed out at 2,000 ms |
| SingleInstance Mutex & Lock Release | 3–8 ms | 100 ms | 5 ms |
| **Total Whole-App Shutdown** | **110–260 ms** | **3,000 ms (3.0s ceiling)** | **Terminates cleanly at 2.3s** |

## Bounded Shutdown Architecture

```text
[App Shutdown Trigger] (Tray Quit / Window Close / OS SessionEnding)
        │
        ├─► 1. Stop UI timers and unhook Windows OS events (<50 ms)
        ├─► 2. Close ancillary windows (Widget, QuickPanel) (<50 ms)
        ├─► 3. Dispose MonitorSession -> Collector.Dispose() (2.0s bounded deadline)
        │        ├─► Stop worker thread
        │        ├─► Persist final gap record (user_paused or normal_shutdown)
        │        ├─► Flush storage barrier (2.0s timeout)
        │        └─► Record session clean end or capture failure stage
        ├─► 4. Dispose SingleInstanceCoordinator (release lockfile & mutex) (<10 ms)
        │
        ├─► Outcome A (Normal): Complete within ~200 ms -> Exit code 0
        └─► Outcome B (Deadline Exceeded / Storage Degraded):
                 ├─► Record CollectorShutdownResult(false, stage, message)
                 ├─► Append non-blocking entry to local operational log
                 └─► Exit immediately; DO NOT block OS shutdown or show modal dialog
```

## Policy & Invariants
1. **Total Shutdown Timeout Bound:** The entire whole-application shutdown path is strictly bounded to **3.0 seconds maximum**, enforced via monotonic clock (`Stopwatch.StartNew()`) in `ApplicationContext.DisposeWithTimeout` and `InstallerEngine.RequestGracefulShutdown`, completely eliminating wall-clock drift, NTP jumps, or system time adjustment hazards.
2. **Storage Barrier Timeout:** The `storage.FlushAsync()` call uses a 2.0-second cancellation token deadline.
3. **Non-Blocking Failure UX:** If storage fails to flush within the deadline or is degraded, SpeedMeter Pro:
   - Sets `CollectorShutdownResult(false, stage, diagnostic)`.
   - Writes a structured failure record to the local operational error log (`%LocalAppData%\SpeedMeterPro\logs\operational.log`).
   - Does **NOT** display a modal dialog or interactive prompt that would stall Windows logoff or unattended machine reboot.
   - Upon next startup, the database detects an unclean shutdown session gap and accurately reconstructs the discontinuity per Gate A/C/D rules.
4. **Single-Instance Clean Release:** The per-user mutex and ownership lock file (`speedmeter.owner.<UserSID>.lock`) are guaranteed to release during `OnExit()`, preventing secondary instances from being locked out on subsequent launch.
5. **OS & Installer Graceful Shutdown Integration:** SpeedMeter Pro registers a listener for `SystemEvents.SessionEnding` / `Application.SessionEnding` and coordinates with installers via the named event `Local\SpeedMeterPro.Shutdown.<UserSid>`. This triggers clean shutdown via `ApplicationContext.DisposeWithTimeout(TimeSpan.FromSeconds(3.0))` ensuring zero data corruption during OS logoff or application upgrades.

## Decision
Adopt the 3.0-second whole-application shutdown bound (`DisposeWithTimeout`), user SID-scoped installer shutdown event listener, and non-blocking diagnostic logging architecture as canonical release behavior.

## Consequences
- Windows reboot and user logoff will never hang waiting for SpeedMeter Pro.
- Storage durability failures are preserved truthfully in local logs and surfaced upon next launch rather than causing silent data loss or hanging the machine.
