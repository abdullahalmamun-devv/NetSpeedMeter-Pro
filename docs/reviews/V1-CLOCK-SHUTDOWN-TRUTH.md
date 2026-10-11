# V1 clock and shutdown truth review

**Reviewers:** Independent Architect / Windows reliability, independent QA, Project Director.
**Disposition:** APPROVED for code integration; CHANGES / EVIDENCE REQUIRED for the V1 product gate.
**PRD:** §7.2–7.4, §22.1–22.2, AC-M05/M08/S05.

## Source and fixture judgment

Independent review verified that `Collector` uses monotonic duration for lifecycle gaps, starts a new clock segment for a UTC jump, interrupts the measurement engine before capturing a post-jump baseline, and does not ingest the crossing counter delta. QA exercised independent UTC/monotonic/awake movement and checked persisted SQLite rows, query uncertainty, and exact bytes. Injected storage failures at gap admission, session end, both flush phases, and a nonreturning flush produce an observable unsuccessful `ShutdownResult` without an asserted clean durability result. Final tests: 197 PASS, 1 intentional SKIP, 0 FAIL; Release build clean; WPF smoke 18/18.

## Required before gate passage

1. Resolve the global UTC valid-valid overlap acceptance conflict explicitly with Product/PRD authority, preserving exact bytes and observed timestamps; segment-aware `ClockUncertain` is the implemented proposal.
2. Obtain controlled Windows sleep/hibernate, actual wall-clock/timezone transition, and Windows 11 evidence for AC-M05/M08.
3. Add a bounded production-facing shutdown failure path and validate whole application/storage disposal under writer failure for AC-S05. The existing structured result and Collector deadline are narrower.

**Director decision:** `V1 CLOCK & SHUTDOWN TRUTH NOT VERIFIED — WAVE 4 BLOCKED`.
