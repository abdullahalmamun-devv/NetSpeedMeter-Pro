# P0-WIDGET-001

TASK ID: P0-WIDGET-001
OWNER: Desktop UI / UX Engineer + Accessibility Specialist
REVIEWER: QA Engineer + Product Analyst
PRD REFERENCES: F04, F05, F42; AC-W03, AC-W04, AC-W08; §§9–10
RELEASE: P0 feasibility
DEPENDENCIES: P0-COLLECTOR-001, P0-MEASURE-001
OBJECTIVE: Productionize the Minimal floating widget with complete state model, accessibility compliance, position persistence abstraction, lock, opacity, click-through, and tray recovery
IN SCOPE:
- Full state handling: Measuring, Live, Idle/zero, Paused, Suspended, Degraded, Unavailable, Failed
- Invariant: Never display zero when data is unavailable or paused
- Widget interactions: Drag, Lock, Topmost, Opacity levels (100%, 80%, 60%)
- Click-through architecture with mandatory tray recovery route
- Multi-monitor safe positioning and non-drag keyboard nudging (accessible positioning)
- Accessibility: Narrator automation properties, high-contrast support, tabular figures, no disruptive per-tick live announcements
OUT OF SCOPE:
- Full multi-series graph docking or full dashboard redesign (belong to subsequent releases)
EXPECTED FILES:
- src/SpeedMeter.App/WidgetWindow.cs
- src/SpeedMeter.App/MainWindow.xaml.cs
- tests/SpeedMeter.Checks/WidgetFixtures.cs
- docs/handoffs/P0-WIDGET-001.md
- docs/reviews/P0-WIDGET-001.md
ACCEPTANCE CONDITIONS:
- All 8 collector/reading states rendered faithfully
- Click-through recoverable at all times from tray menu
- Keyboard navigation and Narrator labels functional
- Arrow keys move widget without requiring mouse drag
TEST REQUIREMENTS:
- Unit/fixture verification of state mappings and accessibility properties
STATUS: In progress
