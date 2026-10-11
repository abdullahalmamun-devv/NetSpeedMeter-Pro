# ADR-004: Taskbar-adjacent meter and floating fallback

## Status

Proposed — Project Director & Independent Audit review, 11 October 2026. Audit judgment: SpeedMeter Pro will not depend on DeskBand/taskbar-hosted shell extension technology. Microsoft does not recommend desk bands for modern development, and their support/behavior is not suitable as the reliability basis for this product. SpeedMeter Pro therefore chooses a separate taskbar-adjacent companion surface with a safe floating-widget fallback. The standalone geometry/query calculations exist; active desktop shell integration and dynamic layout validation remain open for V1.

## Context

PRD Section 9 and Section 29.1 prefer taskbar-adjacent/docked behavior with a safe floating fallback. Microsoft does not recommend in-process shell extensions or desk bands for modern Windows development, and their runtime behavior is fragile and unsuitable as an architectural foundation for SpeedMeter Pro. Separate these concepts clearly:
1. API / interface documentation status: Microsoft does not recommend DeskBand for modern shell development.
2. Product architecture decision: SpeedMeter Pro affirmatively rejects DeskBand/in-process shell extensions in favor of an independent top-level taskbar-adjacent companion window.
3. Windows 11 runtime shell behavior: In-taskbar band hosting is non-viable as a product foundation; centered taskbars, dynamic widget layouts, and Explorer restarts require external companion positioning.
4. Current implementation reality: `TaskbarCompanion.cs` supplies standalone shell query and coordinate calculation helpers with safe floating fallback. Production UI currently runs as an accessible floating widget; companion docking integration into the active application window is V1 work.

## Decision

SpeedMeter Pro will not depend on DeskBand or taskbar-hosted shell extensions. The product adopts an out-of-process taskbar-adjacent companion window anchored relative to the taskbar work-area boundary, backed by a first-class floating-widget fallback.

Retain the real-data floating widget while Windows engineering integrates taskbar-adjacent placement into the active application shell during V1. Do not inject into Explorer or rely on undocumented shell memory. Enable docking only for validated configurations; unknown, auto-hide, overlapping, or failed placement queries must immediately fall back to an accessible floating surface. Keep a non-drag reset-position command accessible from the tray.

## Alternatives

Explorer injection or in-process desk bands introduce unacceptable shell instability and maintenance hazards. Treating notification area tray text as an equivalent taskbar meter fails the intended companion surface. Claiming work-area corner placement is true docking without taskbar awareness is inaccurate. Floating-only release would require an explicit approved scope decision if docking cannot meet the PRD gate.

## Consequences

Taskbar companion placement is a recognized V1 engineering task. Saved positions must survive display and taskbar configuration changes safely. Current explicit fallback math is verified by fixtures and live queries, but full docked companion integration into the main application window is scheduled for V1.0.

## PRD Impact

Sections 9 and 29.1; AC-W series. No V1 Must feature is silently deferred by choosing the initial floating slice.

## Validation Required

Chosen Windows 11 builds; taskbar alignment and auto-hide; scaling/mixed DPI; monitor removal and negative monitor coordinates; Explorer restart; fullscreen behavior; no control overlap; keyboard and Narrator recovery paths. Record actual fallback outcome per configuration.
