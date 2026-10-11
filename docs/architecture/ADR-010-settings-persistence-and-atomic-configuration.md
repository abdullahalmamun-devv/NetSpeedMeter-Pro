# ADR-010: Settings persistence, atomic replacement, and live graph ring buffer

## Status

Accepted — Formally audited, repaired, and approved by Independent Gate B Audit Team on 11 October 2026.

Implementation verified in `SpeedMeter.Core`, `SpeedMeter.Storage`, and `SpeedMeter.Application`:
- Separation of Concerns: Configuration settings are decoupled from the SQLite accounting database. Configuration is stored in an independent JSON file (`settings.json`).
- Schema Versioning & Future Version Rejection: `SpeedMeterSettings` provides explicit schema versioning (`SchemaVersion = 1`). `SettingsStorage.Load` rejects any file where `schemaVersion > CurrentSchemaVersion`, preserving the future configuration file untouched on disk, loading safe in-memory defaults, and emitting an explicit diagnostic warning.
- Monitor Identity Persistence: Settings persist `WidgetMonitorDeviceName` and `WidgetMonitorBounds` alongside `WidgetX` and `WidgetY`, preserving window placement anchors across multi-monitor setups.
- Atomic File Replacement: `SettingsStorage` writes new configuration to a temporary sibling file (`settings.json.tmp.*`) with `FileOptions.WriteThrough` flush followed by atomic replacement (`File.Move(..., overwrite: true)`), preventing truncation upon sudden termination.
- Corruption Recovery: If invalid JSON is encountered, the damaged file is preserved as `settings.json.corrupt.<timestamp>.json` and clean default settings are loaded.
- Monotonic Live Graph Ring Buffer: `LiveGraphBuffer` provides a fixed-capacity circular ring buffer with strict monotonic timestamp guards. Out-of-order timestamps are rejected; duplicate timestamps with higher sequence numbers update in place.
- Truthful Allocation Profile: Zero steady-state allocations during continuous sampling append; bounded allocations during window downsampling.
- Application Facades: `SpeedMeter.Application.ISettingsService` and `SpeedMeter.Application.ILiveGraphService` encapsulate settings state and live graph snapshots.

## Context

PRD Sections 14, 18, and 23 define application settings and live graph visualization requirements.
Historically, applications that fail to validate schema versioning destroy future settings files if users downgrade or run mixed versions. Storing window coordinates without monitor identity causes off-screen placement on multi-monitor systems. Non-monotonic ring buffer appends cause graph rendering glitches during clock adjustments.

## Decision

1. **Independent Settings Storage**: User preferences reside in JSON files separate from the SQLite usage ledger.
2. **Strict Future Schema Rejection**: Newer schema versions are never loaded or overwritten by older binaries.
3. **Multi-Monitor Work Area Anchors**: Settings persist monitor device name and bounding rectangle.
4. **Atomic Replacement Guarantee**: WriteThrough temporary file flush followed by atomic move.
5. **Monotonic Live Graph Buffer**: Ring buffer enforces non-decreasing timestamp order and updates duplicate timestamps in-place based on sample sequence.
