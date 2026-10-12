# V1-CONS-QA-003 — Destructive safety regression

- **Owner:** QA specialist
- **Reviewer:** Security/Privacy specialist and Project Director
- **PRD references:** §§24, 25.1–25.2; AC-S03, AC-S07
- **Release:** V1.0 RC.4
- **Dependencies:** V1-CONS-SAFETY-003 helper contract; sentinelled `TestSandboxGuard`.
- **Objective:** Prove rejection of unowned, forged, escaped and reparse targets without touching real profile data.
- **In scope:** Negative uninstall, purge and helper fixtures; valid NTFS metacharacter paths.
- **Out of scope:** Destructive tests on the real user profile, production release signing.
- **Expected files:** `tests/SpeedMeter.Checks/ConsumerInstallerFixtures.cs`, optional dedicated safety fixtures, handoff.
- **Acceptance conditions:** Every destructive test root is under `%TEMP%\SpeedMeterPro.Tests\<GUID-N>` with a valid sentinel; required negative cases run and reject; positive valid-path cases pass.
- **Test requirements:** Executable fixture runner, Windows runner where available, actual result counts.
- **Output:** Fixtures and `docs/handoffs/V1-CONS-QA-003.md`.
- **Status:** In progress.
