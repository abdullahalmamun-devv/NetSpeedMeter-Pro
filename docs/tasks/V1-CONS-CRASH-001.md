# V1-CONS-CRASH-001 — Local crash recovery

- **Owner:** Reliability/Crash UX engineer
- **Reviewer:** Security/Privacy, Accessibility, QA, Project Director
- **PRD references:** F57/F63; §§24, 25.3
- **Release:** V1.0 RC.2
- **Dependencies:** Gate E1; ADR-015; operational logger and diagnostic bundle
- **Objective:** Bounded local crash records, truthful next-launch recovery, user-controlled report export and browser issue path.
- **In scope:** Fatal exception capture, run-state tracking, sanitization, retention, non-blocking recovery UX.
- **Out of scope:** Automatic cloud upload, telemetry backend, Gate E2.
- **Expected files:** New `Crash*` application/UI files, `App.xaml.cs`, dedicated fixtures, handoff.
- **Acceptance conditions:** Fatal paths create a redacted bounded report; clean exit and unknown abrupt exit are distinguished from known crash; next launch offers view/save/report without automatic upload; collector durability and shutdown bound remain intact.
- **Test requirements:** Controlled UI/background failure, redaction, retention, recovery UX, export and safe issue URL.
- **Output:** Code, fixtures, and `docs/handoffs/V1-CONS-CRASH-001.md`.
- **Status:** In progress.
