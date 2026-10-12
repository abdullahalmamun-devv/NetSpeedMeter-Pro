# V1-CONS-FIELD-PREGATE-003 — Native consumer observations

- **Owner:** Project Director
- **Reviewer:** Independent Accessibility and Security/Privacy specialists
- **PRD references:** §§6, 24, 25.1–25.2, 26.13; AC-S03, AC-S07, AC-S08
- **Release:** V1.0 RC.4 candidate, pre-Gate-E2 only
- **Dependencies:** RC.4 safety integration; clean candidate package; verified commit and push before native destructive tests; explicit user authorization for the current Windows profile.
- **Objective:** Record real Installed Apps, keep/reinstall, purge, packaged crash, accessibility, process privacy and update-consent observations.
- **In scope:** Test-only isolated crash harness, native matrix, evidence and gate decision.
- **Out of scope:** Deletion outside SpeedMeter-owned install/data roots, Gate E2 soak, GA claim, V1.5.
- **Expected files:** `src/SpeedMeter.App/App.xaml.cs`, new reviews and `docs/evidence/CONSUMER-CLOSURE-RC4.md`, board/status.
- **Acceptance conditions:** Native observations are explicit; missing evidence remains blocked; final gate decision is Director-only.
- **Test requirements:** Packaged app and Settings UI in the user-authorized current profile with exact target checks, keyboard/Narrator/DPI/contrast, process-attributed outbound observation, update opt-in/out. Preserve unrelated files and restore temporary display settings.
- **Output:** RC.4 evidence and reviews.
- **Status:** In progress; current-profile native validation authorized by the user, gated on verified source push.
