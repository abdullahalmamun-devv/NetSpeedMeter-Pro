# V1-CONS-RELEASE-001 — RC.2 integration and evidence

- **Owner:** Project Director / Release engineer
- **Reviewer:** Independent QA, Security, Accessibility
- **PRD references:** F54/F57/F63; §§24, 25, 29
- **Release:** V1.0 RC.2
- **Dependencies:** V1-CONS-UPDATE-001, V1-CONS-INSTALL-001, V1-CONS-CRASH-001
- **Objective:** Integrate owned changes, maintain accurate product docs, execute full regression and release a source-identifiable RC.2 candidate.
- **In scope:** Version/release scripts, startup wiring, fixture registration, documentation, source commit, clean-tree build, package hashing, evidence and Director decision.
- **Out of scope:** Gate E2 field validation, GA, V1.5.
- **Expected files:** `Directory.Build.props`, `ProductIdentity.cs`, `scripts/**`, fixture runner, README/board/status/help/privacy/known issues, new evidence/handoff/review records.
- **Acceptance conditions:** Source SHA and artifact SHA match; release build uses Production trust only; all required suites and consumer drills have observed outcomes; unrun environment dependencies are explicit.
- **Test requirements:** `build.ps1`, `check.ps1`, `check.ps1 -Windows`, `smoke.ps1`, consumer integration and clean-tree release drill.
- **Output:** RC.2 release evidence and consolidated closure report.
- **Status:** In progress.
