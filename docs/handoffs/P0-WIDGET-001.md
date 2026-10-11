# P0-WIDGET-001 handoff

TASK: P0-WIDGET-001 — Production Minimal floating widget, accessibility compliance, and interaction recovery.

STATUS: Review requested. Only the Project Director may accept UI widget integration.

IMPLEMENTED:
- Productionized `WidgetWindow` in `src/SpeedMeter.App/WidgetWindow.cs` adhering to Minimal layout:
  - Download and Upload rate displays with tabular figures (`FontNumeralAlignment.Tabular`).
  - Full state handling across all 8 states: `Measuring`, `Live`, valid `Idle` / zero, `Paused`, `Suspended`, `Degraded`, `Unavailable`, `Failed`.
  - Invariant strictly preserved: Never displays zero when data is unavailable or paused; renders "↓ —", "↑ —" with explanatory status text.
- Interaction and safety features:
  - Drag-move enabled when unlocked; mouse dragging bounded by virtual screen work area.
  - Position lock toggle (`IsLocked`) via hotkey (`L`), context menu, and system tray menu. Suppresses dragging when locked.
  - Topmost toggle and opacity levels (100%, 80%, 60%).
  - Click-through architecture using Win32 `WS_EX_TRANSPARENT | WS_EX_LAYERED`.
  - Mandatory tray recovery route: added "Disable click-through" and "Toggle widget lock" to tray context menu in `MainWindow.xaml.cs`, ensuring user is never trapped when click-through is enabled.
  - Non-drag accessible keyboard nudging: Arrow keys (Left/Right/Up/Down) nudge widget position by 12 pixels.
  - Multi-monitor bounds check: `EnsureVisibleOnScreen()` prevents widget from being placed outside visible virtual screen coordinates.
- Accessibility Specialist review compliance:
  - Windows Automation properties: `AutomationProperties.SetName` and `AutomationProperties.SetHelpText` configured for screen readers.
  - Clear descriptive labels: "Download: {rate}", "Upload: {rate}", "Status: {detail}".
  - Eliminated live announcement spam: screen reader does not announce every 1-second sampling tick.

FILES CHANGED:
- `src/SpeedMeter.App/WidgetWindow.cs`
- `src/SpeedMeter.App/MainWindow.xaml.cs`
- `docs/tasks/P0-WIDGET-001.md`
- `docs/handoffs/P0-WIDGET-001.md`

DESIGN DECISIONS:
- Recovery from click-through is placed in the persistent notification area tray menu; tray menu operates independently of widget hit-testing.
- Keyboard nudging provides a compliant non-drag positioning alternative for motor-impaired users.
- Tabular figures prevent layout jitter during rapid transfer rate fluctuations.

TESTS EXECUTED:
- `scripts/build.ps1`: 0 warnings, 0 errors.
- `scripts/smoke.ps1`: UI smoke run completed with valid readings, screenshot capture of widget and dashboard, pause preservation, reset, and resume.

ACTUAL RESULTS:
- Floating widget renders cleanly and captures real traffic telemetry.
- Smoke screenshot verified in artifacts.

KNOWN LIMITATIONS:
- User-customized position is stored in-memory for the current process session; permanent settings persistence across restarts is queued for SQLite settings storage.

RISKS:
- None identified; tray recovery prevents trapped overlay scenarios.

RECOMMENDED NEXT STEP:
- Accessibility Specialist and QA review `P0-WIDGET-001`. Director approves and integrates.
