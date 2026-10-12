# V1-CONS-PROVENANCE-002 — Source and field-candidate provenance

- **Owner:** Project Director / Release engineer
- **Reviewer:** Independent Security/Privacy + QA
- **PRD references:** F54; §§24, 25, 29; consumer closure handoff provenance and package sections
- **Release:** RC.3 if shipping source changes
- **Dependencies:** Seven-commit RC.2 audit, safety and updater integration reviews.
- **Objective:** Establish clean source/artifact identity, publish verified commits by normal fast-forward, and record accurate consumer gate evidence.
- **In scope:** Backup ref, commit audit, package-size/dependency audit, safe release scripts, complete regression, versioned artifact/hashes, GitHub push and remote verification, board/status/evidence.
- **Out of scope:** Force push, GA claim, Gate E2 24-hour/7-day/Windows 11 work.
- **Expected files:** `scripts/package.ps1`, `scripts/release.ps1`, `Directory.Build.props`, `ProductIdentity.cs`, release docs/evidence/board/status and artifact directory.
- **Acceptance conditions:** Clean-tree candidate embeds source SHA; exact bytes/hash recorded; local HEAD, fetched origin/main, ls-remote and public GitHub agree after evidence commit; no silent test skip or unresolved safety risk.
- **Test requirements:** `build.ps1`, `check.ps1`, `check.ps1 -Windows`, `smoke.ps1`, isolated consumer drills and independent review.
- **Output:** Field-candidate artifact, safety/provenance evidence, final Director decision.
- **Status:** In progress.
