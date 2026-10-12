# V1-CONS-UPDATE-DRILL-002 — Updater-capable build rehearsal

- **Owner:** Update/Application engineer
- **Reviewer:** Security/Privacy, QA, Project Director
- **PRD references:** F54; §§24, 25.2, 26.13; AC-S03; consumer closure handoff §§16–24
- **Release:** RC.2 updater seed to RC.3 target
- **Dependencies:** V1-CONS-SAFETY-002 before any installer execution; existing ADR-014 trust and canonical installer.
- **Objective:** Prove the real updater mechanics with a signed DevelopmentTest lab manifest while keeping every consumer build on Production trust.
- **In scope:** Test-only injection/harness, manifest and package retrieval, size/hash/WinVerifyTrust/publisher/failure cases, installed upgrade and restart evidence.
- **Out of scope:** Production key creation, consumer test-trust switch, GA or Gate E2 long tests.
- **Expected files:** `src/SpeedMeter.Application/UpdateService.cs`, `UpdateVerificationService.cs`, `tests/SpeedMeter.Checks/ConsumerUpdateFixtures.cs`, lab harness and handoff.
- **Acceptance conditions:** Source build contains updater; target version is newer; full app→Update Now→installer→restart path observed in isolated profile; bad inputs preserve old install; DevelopmentTest cannot be selected by Settings, manifest, download, or consumer CLI.
- **Test requirements:** Focused non-destructive fixtures, then isolated end-to-end Windows drill after safety gate.
- **Output:** Source, tests, `docs/handoffs/V1-CONS-UPDATE-DRILL-002.md`, and drill evidence.
- **Status:** In progress.
