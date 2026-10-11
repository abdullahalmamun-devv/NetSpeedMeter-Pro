# P0-COLLECTOR-001

TASK ID: P0-COLLECTOR-001
OWNER: Director / Application Engineer
REVIEWER: Architect + QA
PRD REFERENCES: §21.2–21.4; AC-M05/M07; F01/F43
RELEASE: P0 feasibility
DEPENDENCIES: Stable measurement and provider contracts
OBJECTIVE: Separate collector from WPF and bound display delivery
IN SCOPE: Application collector/clock seam; lifecycle state; latest-only display; UI integration
OUT OF SCOPE: Persistence, IPC/service, user scope change
EXPECTED FILES: Core/ProviderContracts.cs; src/SpeedMeter.Application/**; App/MonitorSession.cs and MainWindow; slnx
ACCEPTANCE CONDITIONS: No WPF/provider dependency in collector; preserve accounting while coalescing display; typed failures
TEST REQUIREMENTS: Injectable fixtures; lifecycle tests; existing real UI smoke regression
OUTPUT: Source/documents as assigned plus structured handoff; Director integrates after peer review.
STATUS: Ready; PROJECT_BOARD.md is the current-state authority.

