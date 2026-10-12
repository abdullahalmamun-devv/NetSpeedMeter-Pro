# V1-CONS-SAFETY-002 — Destructive test and purge safety

- **Owner:** Windows Installer / Test Safety engineer
- **Reviewer:** Independent Security/Privacy + Project Director
- **PRD references:** §§24, 25.1, 26.13; AC-S03; consumer closure handoff §§7–12
- **Release:** V1.0 RC.3 if shipping code changes are required
- **Dependencies:** RC.2 local source, read-only incident reconstruction; destructive fixtures frozen until guard review
- **Objective:** Prevent any automated purge or uninstall test from reaching real user data and constrain production purge to the canonical current-user product root.
- **In scope:** Installer purge validation, test-only directory injection, unique sandbox/sentinel/delete guard, incident evidence.
- **Out of scope:** Deleting real account data, arbitrary-path consumer purge switch, Gate E2 long tests.
- **Expected files:** `src/SpeedMeter.Installer/InstallerEngine.cs`, `tests/SpeedMeter.Checks/V1Wave5ReleaseFixtures.cs`, `ConsumerInstallerFixtures.cs`, dedicated safety helper/fixtures, handoff.
- **Acceptance conditions:** Every destructive test path is absolute, unique, contained, and non-reparse; unsafe targets abort before deletion. Production purge cannot target an arbitrary path. Prior incident impact is stated without invented certainty.
- **Test requirements:** Static deletion-path audit, negative guard fixtures, safe isolated installer fixtures, independent review before any destructive integration drill.
- **Output:** Code, executable evidence, and `docs/handoffs/V1-CONS-SAFETY-002.md`.
- **Status:** In progress.
