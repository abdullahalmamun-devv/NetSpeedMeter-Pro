# V1-CONS-RELEASE-003 — RC.4 package safety and provenance

- **Owner:** Release/Build specialist
- **Reviewer:** Security/Privacy specialist and Project Director
- **PRD references:** §§24, 25.1–25.2; AC-S03
- **Release:** V1.0 RC.4
- **Dependencies:** V1-CONS-SAFETY-003 and QA acceptance; clean committed release source.
- **Objective:** Audit release-output deletion, account for Setup size, and prepare the clean-source RC.4 package path.
- **In scope:** Guarded package cleanup, version references, package content and size analysis, release handoff.
- **Out of scope:** Replacing installer framework, production key creation, Gate E2.
- **Expected files:** `scripts/package.ps1`, `scripts/release.ps1`, `scripts/test-sandbox.ps1` if needed, `docs/handoffs/V1-CONS-RELEASE-003.md`.
- **Acceptance conditions:** Output cleanup is confined and reparse-checked; canonical package is built only after clean RC.4 source commit; size contribution is evidenced.
- **Test requirements:** Script review, build and full release validation when source is frozen; record exact bytes/hash/signing/source SHA.
- **Output:** Script changes and handoff.
- **Status:** In progress.
