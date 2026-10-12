# V1-CONS-ACCESS-NATIVE-002 — Consumer accessibility observation

- **Owner:** Accessibility reviewer
- **Reviewer:** Product / Project Director
- **PRD references:** §6.2–6.3, §25, §26.13; consumer closure handoff §§35–37
- **Release:** RC.2/RC.3 consumer closure
- **Dependencies:** Isolated process/profile and destructive test safety before installer/uninstaller flows.
- **Objective:** Observe the actual consumer surfaces with keyboard, Narrator/UI Automation, display scaling, enlarged text, and High Contrast.
- **In scope:** Updater, graphical install/uninstall, crash recovery; names, focus, announcements, clipping and action reachability.
- **Out of scope:** Claiming native pass from XAML inspection; Gate E2 long field matrix.
- **Expected files:** `docs/reviews/V1-CONS-ACCESSIBILITY-002.md`; source fixes only after Director coordinates ownership.
- **Acceptance conditions:** Each observed host/mode/control has reproducible evidence; unobserved combinations remain pending; one explicit accessibility decision.
- **Test requirements:** Native interaction on the available Windows host, with exact limitations.
- **Output:** Independent review and evidence references.
- **Status:** In progress.
