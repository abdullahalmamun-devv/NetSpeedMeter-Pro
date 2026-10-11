# P0-TASKBAR-001 handoff

TASK: P0-TASKBAR-001 — Safe taskbar-adjacent/docked strategy, shell geometry, auto-hide, and floating fallback.

STATUS: Review requested. Only the Project Director may accept taskbar strategy decisions.

IMPLEMENTED:
- Researched Windows 10 and Windows 11 shell integration boundaries:
  - DeskBand API (`ITrayDeskBand`, `IDeskBand2`) was completely deprecated and removed by Microsoft in Windows 11. Any attempt to build an in-taskbar deskband toolbar fails on Windows 11.
  - Explorer process injection or undocumented shell window hooking is strictly prohibited due to stability, anti-cheat/antivirus flagging, and Windows servicing fragility.
  - The viable, safe strategy is an external **Taskbar-Adjacent Companion Window** or the primary **Floating Desktop Widget**.
- Implemented `TaskbarCompanion` in `src/SpeedMeter.Windows/TaskbarCompanion.cs`:
  - Queries taskbar geometry via `SHAppBarMessage` (`ABM_GETTASKBARPOS`).
  - Detects taskbar edge: `Bottom`, `Top`, `Left`, `Right`.
  - Detects auto-hide mode: `ABM_GETSTATE` with `ABS_AUTOHIDE`.
  - When auto-hide is active, docking is prohibited and `UseFloatingFallback = true` is enforced to prevent shell occlusion and cursor capture issues.
  - Computes non-overlapping adjacent docked coordinates along the taskbar boundary.
  - Subscribes to Explorer shell restart notifications via `RegisterWindowMessage("TaskbarCreated")`.
- Authored test fixtures in `tests/SpeedMeter.Checks/TaskbarFixtures.cs`:
  - Verified placement mathematics across all 4 screen edges (`Bottom`, `Top`, `Left`, `Right`).
  - Verified auto-hide fallback trigger.
  - Verified headless/invalid query fallback trigger.
- Verified live behavior in `Program.cs`: confirms fallback to safe floating placement when running in a headless or non-desktop environment.

FILES CHANGED:
- `src/SpeedMeter.Windows/TaskbarCompanion.cs`
- `tests/SpeedMeter.Checks/TaskbarFixtures.cs`
- `tests/SpeedMeter.Checks/Program.cs`
- `docs/tasks/P0-TASKBAR-001.md`
- `docs/handoffs/P0-TASKBAR-001.md`

DESIGN DECISIONS:
- Never inject DLLs into `explorer.exe`.
- DeskBand is officially rejected for Windows 11 compatibility; taskbar-adjacent companion is accepted as the docking strategy with floating widget as first-class fallback.
- Auto-hide taskbar mode unconditionally forces floating fallback.

TESTS EXECUTED:
- `scripts/check.ps1 -Windows`: 63/63 checks passed with exit code 0.

ACTUAL RESULTS:
- Geometry calculations verified to pixel precision.
- Safe floating fallback verified when taskbar is absent or auto-hidden.

KNOWN LIMITATIONS:
- Windows 11 centered taskbar icons: a companion window docked adjacent to the system tray may overlap dynamically expanding centered app icons if screen resolution is low.

RISKS:
- Multi-monitor setups with secondary taskbars require per-monitor enumeration.

RECOMMENDED NEXT STEP:
- Architect and UI Engineer review `P0-TASKBAR-001`. Director approves and updates ADR-004.
