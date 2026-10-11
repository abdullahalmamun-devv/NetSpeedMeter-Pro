# SpeedMeter Pro

A native Windows network meter, started from `Premium-Speed-Meter-PRD.md`.
**Status: first P0 feasibility slice, not V1 and not a completed P0 gate.**

Initial verification: Release build passed with zero warnings/errors; 15 core/Windows checks passed; the real WPF smoke workflow passed. See `docs/VALIDATION.md` for environment, evidence and limits. Windows 11 validation remains pending.

## Run

Windows x64 and .NET SDK **10.0.401** are required. This workspace has a local SDK in `.tools/dotnet`; scripts use it first, falling back to an installed SDK. The app runs as a standard user.

```powershell
.\scripts\build.ps1
.\scripts\check.ps1 -Windows
.\scripts\run.ps1
```

Select your Wi-Fi or Ethernet adapter to start. The first sample establishes a baseline; the next displays a rate. **Show widget** opens the movable floating meter. Its right-click menu opens the dashboard or hides the widget; Enter opens the dashboard and Escape hides it. Dashboard close continues in the tray with an explanation; **Quit** stops collection.

## Implemented

- Native `GetIfTable2` adapter inventory and 64-bit counters; loopback excluded.
- Explicit single-adapter scope. No automatic route inference or multi-adapter summation.
- Actual monotonic elapsed-time rates, decimal byte/bit display, exact session byte totals.
- Baseline/reset/disconnect/pause handling; sleep/resume event handling.
- WPF dashboard, 60-second live graph, floating widget and notification-area controls.
- 0.5 / 1 / 2-second sampling, session reset, unit switching, adapter refresh.
- In-memory state only; no active network probes, accounts, analytics, startup registration or service installation.
- Dependency-free fixture runner and Windows counter comparison. Automated UI smoke mode renders a screenshot and records live-reading assertions.

## Boundaries

Adapter counters include LAN and potentially tunnel traffic; they are not an ISP billing record. Select a physical adapter for an explicit physical-boundary view. Changing scope starts a new in-memory session. Pause/quit/sleep create unobserved spans; bytes across these gaps are not reconstructed.

History, quotas, exports, settings persistence, automatic route selection, taskbar docking, app attribution, connection probes, installers and updates remain planned. The sidebar labels planned destinations; it does not simulate those features. No release-level accuracy, performance, accessibility or compatibility claims have been established. See `docs/P0-STATUS.md` for the next gates.

## Structure

- `src/SpeedMeter.Core`: provider-independent accounting and formatting.
- `src/SpeedMeter.Windows`: IP Helper interop and adapter observations.
- `src/SpeedMeter.App`: WPF UI and background collection lifecycle.
- `tests/SpeedMeter.Checks`: deterministic verification and optional real Windows checks.
- `docs`: decisions, scope and validation evidence.

Package sources are deliberately empty because this slice uses only SDK/framework libraries. Review and add sources when a dependency such as SQLite is introduced. Nothing is published or installed system-wide.

## UI smoke check

```powershell
.\scripts\smoke.ps1
```

This opens the real WPF dashboard briefly, explicitly selects the first available adapter for the smoke run (active hardware adapters sort first), collects actual counter readings, checks units/pause/reset/resume, writes `dashboard.png`, `widget.png` and `smoke.json` under a timestamped `artifacts` folder, and quits. It does not generate traffic. A machine without a usable adapter cannot pass the live-reading assertion.

## SDK setup on another machine

Install the official .NET 10 x64 SDK, or use Microsoft's `dotnet-install.ps1` with `-Version 10.0.401 -InstallDir .tools/dotnet -NoPath`. The local SDK is ignored by Git. SDK selection follows `global.json`.
