# P0-TASKBAR-001

TASK ID: P0-TASKBAR-001
OWNER: Windows Platform Engineer
REVIEWER: Software Architect + UI Engineer + QA Engineer
PRD REFERENCES: F03; AC-W01, AC-W02; §§9, 11
RELEASE: P0 feasibility
DEPENDENCIES: P0-ARCH-001
OBJECTIVE: Investigate safe taskbar-adjacent/docked strategy, shell geometry, auto-hide, Explorer restart, and floating fallback
IN SCOPE:
- Shell AppBar API interop (SHAppBarMessage, ABM_GETTASKBARPOS, ABM_GETSTATE)
- Taskbar edge detection (Bottom, Top, Left, Right)
- Auto-hide detection and safe floating fallback
- Explorer restart recovery via RegisterWindowMessage("TaskbarCreated")
- Windows 11 shell limitations analysis (DeskBand API removal in Win11)
- Companion window positioning calculations without shell injection
OUT OF SCOPE:
- Explorer DLL injection, undocumented shell memory patching, or invasive API hooks (strictly prohibited)
EXPECTED FILES:
- src/SpeedMeter.Windows/TaskbarCompanion.cs
- tests/SpeedMeter.Checks/TaskbarFixtures.cs
- docs/handoffs/P0-TASKBAR-001.md
- docs/reviews/P0-TASKBAR-001.md
ACCEPTANCE CONDITIONS:
- Zero shell injection or explorer tampering
- Correct geometric computation for taskbar-adjacent docking
- Safe floating fallback when auto-hide is active or placement is obstructed
- Explorer restart recovery mechanism documented and implemented
TEST REQUIREMENTS:
- Geometric calculation fixtures across Bottom/Top/Left/Right taskbar edges and auto-hide states
STATUS: In progress
