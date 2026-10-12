# V1-CONS-CRASH-001 — Local crash recovery handoff

- **Task:** V1-CONS-CRASH-001
- **Owner:** Reliability / Crash UX engineer
- **Reviewers:** Security / Privacy, Accessibility, QA, Project Director
- **PRD:** F57, F63, §§24 and 25.3; consumer closure request §§48–58, 68
- **Dependencies:** Gate E1, ADR-015, `OperationalLogger`, `RedactionHelper`, `DiagnosticBundleService`
- **Status:** Implementation ready for independent review and integrated verification. No feature or gate declaration.

## Implemented

- Added local fatal report capture under `%LocalAppData%\SpeedMeterPro\crashes`. Each report is at most 24 KiB; at most 12 reports are retained. Writes use a temporary file and atomic replacement. Orphan report temporary files are pruned on startup.
- Reports contain UTC time, product version, source SHA, database schema, OS and runtime versions, exception type, bounded sanitized message and stack, and truthful storage/collector availability. Existing redaction is augmented for IPv6, URL, email, generic absolute paths and named secret/SSID fields. No memory dump or traffic payload is captured.
- Durable run marker distinguishes clean quit, OS shutdown, known fatal exception and abrupt unknown unclean termination. Only a known fatal report prompts recovery on next launch. Unknown unclean termination is not labeled a confirmed app crash.
- Wired WPF Dispatcher and AppDomain fatal handlers without setting `Handled` or swallowing process failure. An unobserved TaskScheduler exception is logged by type only and does not falsely mark a fatal run.
- Added modeless recovery window with View, Save, Report issue and Dismiss. View and Save open a redacted, selectable preview. Save requires the user's chosen destination. Report issue opens a fixed HTTPS GitHub New Issue URL containing bounded metadata only; no diagnostic content is uploaded or placed in the URL.

## Files changed

- `src/SpeedMeter.Application/CrashReportService.cs`
- `src/SpeedMeter.App/App.xaml.cs`
- `src/SpeedMeter.App/CrashRecoveryWindow.cs`
- `tests/SpeedMeter.Checks/ConsumerCrashFixtures.cs`
- `docs/handoffs/V1-CONS-CRASH-001.md`

## Integration and tests

- Fixture registration required in `tests/SpeedMeter.Checks/Program.cs`: `SpeedMeter.Checks.ConsumerCrashFixtures.Run(Check);` (Director owns this shared entry point).
- Four fixtures cover local report creation and redaction, next-launch recovery, clean/OS/unknown outcomes, 12-report retention, size bound and safe issue URI. The fixture source is complete; execution results await the Director's integrated build and executable fixture run.
- A full WPF controlled UI/background fatal drill, screen-reader/keyboard review, and user-driven save/browser checks require independent QA/Accessibility evidence. This handoff does not claim them as observed.
- An attempted App build was stopped before project compilation at the Director's request to avoid shared `obj` contention while concurrent agents edit. No build result is claimed here.

## Limits and decisions

- Storage and collector state are recorded as `unavailable` by the global exception handlers because those private `MainWindow` services cannot be safely queried in a fatal path. This is explicit rather than guessed.
- The run marker tracks process exit classification; it does not certify collector or SQLite flush durability. Existing shutdown result reporting remains authoritative for that question.
- If the crash directory cannot be written, capture fails without recursive crashing. The next run cannot claim a report was created.
- Local files remain accessible to the current user and administrators under normal Windows permissions. The report is never sent automatically; opening GitHub is user initiated.

## Recommended next step

Director integrates fixture registration and any update-startup wiring, then obtains Security, Accessibility and QA reviews plus executable Windows evidence before declaring the task complete.
