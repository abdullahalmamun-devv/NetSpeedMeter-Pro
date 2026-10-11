# ADR-001: Runtime and UI stack

## Status

Accepted — Project Director & Independent Audit review, 11 October 2026. Audit judgment: ACCEPTED. C#/.NET 10 LTS, WPF, and native Windows APIs are accepted as the SpeedMeter Pro runtime, UI, and domain stack. Separation between Core, Application, Windows, and App is verified in both headless runner and UI smoke test. Target Windows 11 release lane validation and long-run soak testing proceed into V1.

## Context

PRD 21.1 recommends C#, .NET 10 LTS, WPF, native Windows APIs and SQLite. `global.json` pins SDK 10.0.401 with latest-patch roll-forward; App targets `net10.0-windows`, x64, WPF plus WinForms tray. Existing evidence reports .NET 10.0.12 on Windows 10 build 19045 only. That does not verify the current supported patch or Windows 11 release matrix.

## Decision

Retain C#/.NET 10/WPF for P0 and use native Windows integrations behind providers. Keep x64 as the current tested architecture. Use a supported patched SDK/runtime selected and verified at packaging time; record exact build/runtime/dependency inventory. Retain the Application project that removes the collector's WPF dependency. SQLite remains a separate proposed persistence decision.

## Alternatives

- WinUI 3: possible only after evidence of widget/taskbar behavior, deployment and resource effects; no present migration justification.
- Browser/Electron UI or cloud service: not required for this native local-first product.
- One giant App assembly: cheaper immediately but prevents headless collector testing; retain the existing Core/Windows separation.

## Consequences

Reuses functional native code and existing real data UI. Windows-specific deployment and accessibility remain engineering work. No ARM64 or Windows 10 product support promise is made by a developer-machine run. Pinning an SDK is reproducibility evidence, not proof of ongoing patch support.

## PRD Impact

Implements the candidate in 21.1 and dependency intent in 21.2. Does not change supported OS/product scope or fulfill the runtime/performance portion of 29.1 merely by selecting a stack.

## Validation Required

Director acceptance; verify official runtime/OS support and patch state at release; repeat build, real provider and UI checks on chosen Windows 11 builds; record startup/CPU/RAM including all product processes; keyboard/Narrator/high contrast/mixed-DPI review. Existing local report is `docs/VALIDATION.md`; no new benchmark was executed by this documentation task.
