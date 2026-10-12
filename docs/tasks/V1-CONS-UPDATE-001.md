# V1-CONS-UPDATE-001 — Consumer update experience

- **Owner:** Update/Application + UI engineer
- **Reviewer:** Security/Privacy, Accessibility, QA, Project Director
- **PRD references:** F54; §§24, 25.2; AC for update privacy and release
- **Release:** V1.0 RC.2
- **Dependencies:** Gate E1; ADR-014; canonical installer contract
- **Objective:** GitHub-hosted signed-manifest discovery, user-consented daily checks, manual check, secure streamed download, truthful update states, and consumer-facing controls.
- **In scope:** Application update orchestration, Settings/first-run UX, deterministic security and cadence fixtures.
- **Out of scope:** Production private-key provisioning, Gate E2 field validation, V1.5.
- **Expected files:** `src/SpeedMeter.Core/SettingsDomain.cs`, `src/SpeedMeter.Application/UpdateService.cs`, update verification support, Settings and first-run views/viewmodels, dedicated fixtures, handoff.
- **Acceptance conditions:** Disabled automatic checks create zero requests; manual check is explicit; a signed newer same-channel version is offered; invalid manifest/package/signature/publisher never executes; cancellation and failures preserve the installed app; production path never accepts development trust.
- **Test requirements:** Deterministic update matrix plus integration with canonical Setup.exe and privacy network observation.
- **Output:** Code, fixtures, and `docs/handoffs/V1-CONS-UPDATE-001.md`.
- **Status:** In progress.
