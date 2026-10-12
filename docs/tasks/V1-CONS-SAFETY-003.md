# V1-CONS-SAFETY-003 — Managed uninstall and purge closure

- **Owner:** Windows Installer specialist
- **Reviewer:** Independent Security/Privacy specialist and Project Director
- **PRD references:** §§24, 25.1–25.2; AC-S03, AC-S07
- **Release:** V1.0 RC.4
- **Dependencies:** Gate E1; RC.3 source and consumer closure evidence; coordinated helper contract with QA
- **Objective:** Replace deferred CMD deletion with a product-owned managed cleanup flow and close purge containment gaps.
- **In scope:** Trusted out-of-tree helper, parent-exit wait, immediate target revalidation, no CMD path constraint, quarantine and checked deletion, truthful incomplete-purge outcome.
- **Out of scope:** Real-account purge tests, Gate E2 duration work, V1.5, other platforms.
- **Expected files:** `src/SpeedMeter.Installer/InstallerEngine.cs`, `Program.cs`, `InstallerWindow.cs`, installer-specific new source, handoff.
- **Acceptance conditions:** No CMD/batch production uninstall; owned path and marker checked immediately before mutation; helper rejects arbitrary requests; reparse points and escapes abort; partial purge is reported.
- **Test requirements:** Focused disposable-root fixtures, normal and Windows executable check runners, isolated install/uninstall drill, independent security review.
- **Output:** Code, evidence, `docs/handoffs/V1-CONS-SAFETY-003.md`.
- **Status:** In progress.
