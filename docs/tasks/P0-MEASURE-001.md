# P0-MEASURE-001

TASK ID: P0-MEASURE-001
OWNER: Network Measurement Engineer
REVIEWER: Architect + QA + Director
PRD REFERENCES: F01/F02/F07; AC-M01–08; §§7–8
RELEASE: P0 feasibility
DEPENDENCIES: Existing engine; contract coordination
OBJECTIVE: Implement explicit epoch and immutable snapshot metadata; strengthen arithmetic
IN SCOPE: Core engine and independent deterministic fixtures
OUT OF SCOPE: Windows/App/storage changes; shared provider contract file
EXPECTED FILES: Core/Measurement.cs and related domain files; MeasurementFixtures.cs; own handoff
ACCEPTANCE CONDITIONS: Known epoch changes rebaseline; exact integer deltas; finite valid durations; mandatory 125M/10 conversion
TEST REQUIREMENTS: Normal/simultaneous/idle/reset/negative time/gaps/identity/overflow/nonfinite fixtures
OUTPUT: Source/documents as assigned plus structured handoff; Director integrates after peer review.
STATUS: In progress; PROJECT_BOARD.md is the current-state authority.

