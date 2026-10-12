# V1-CONS-INSTALL-001 — Consumer install and uninstall

- **Owner:** Windows Release/Installer engineer
- **Reviewer:** Accessibility, QA, Project Director
- **PRD references:** F54; §25.1; ADR-012/013/015
- **Release:** V1.0 RC.2
- **Dependencies:** Gate E1; canonical transactional installer
- **Objective:** Standard-user graphical install, update restart, and friendly uninstall from Windows Apps.
- **In scope:** Interactive installer and uninstaller, keep-data default, explicit purge confirmation, shortcuts/startup, silent mode preservation.
- **Out of scope:** Second installer architecture, privileged helper, Gate E2.
- **Expected files:** `src/SpeedMeter.Installer/**`, dedicated fixtures, handoff.
- **Acceptance conditions:** Double-click Setup.exe installs without console/UAC; Windows Apps uninstall opens graphical choice; cancellation leaves installation intact; preserve/reinstall restores data; purge requires explicit confirmation; running app exits gracefully or operation aborts.
- **Test requirements:** Standard-user interactive and silent drills, cancel/preserve/purge/reinstall and running-app checks.
- **Output:** Code, fixtures, and `docs/handoffs/V1-CONS-INSTALL-001.md`.
- **Status:** In progress.
