# ADR-010: Settings persistence, atomic replacement, and live graph ring buffer

## Status

Accepted — Formally approved by Project Director following V1 Wave 2 implementation and verification on 11 October 2026.

Implementation completed in `SpeedMeter.Core`:
- Separation of Concerns: Configuration settings are decoupled from the SQLite accounting database. Configuration is stored in an independent JSON file (`settings.json`).
- Schema Versioning & Sanitization: `SpeedMeterSettings` provides explicit schema versioning (`SchemaVersion = 1`). The `Sanitize()` method validates and clamps all values (billing cycle days 1..31, graph retention 60..3600s, refresh interval 100..5000ms, thresholds 1..100%).
- Atomic File Replacement: `SettingsStorage` writes new configuration to a temporary sibling file (`settings.json.tmp`) followed by atomic replace (`File.Move(..., overwrite: true)`), preventing half-written corrupted files upon power outage or sudden termination.
- Corruption Recovery: If the settings file is corrupted or contains invalid JSON, `SettingsStorage` preserves the damaged file as `settings.json.corrupt.<timestamp>.json` for user forensics, and initializes a clean, sanitized default configuration. The application never crashes on startup due to damaged settings.
- Memory-Bounded Live Graph Buffer: `LiveGraphBuffer` provides a fixed-capacity circular ring buffer with zero steady-state heap allocations. Peak-preserving downsampling aggregates high-resolution points into display bins while preserving maximum spikes in download/upload rates.
- Verified by automated fixtures: `W2-GRAPH: LiveGraphBuffer memory is strictly bounded and downsampling preserves peak spikes`, `W2-SETTINGS: SettingsStorage persists atomically with versioned schema and sanitization`, `W2-SETTINGS: SettingsStorage corrupt file recovery creates backup and loads safe defaults`.

## Context

PRD Sections 14, 18, and 23 define application settings and live graph visualization requirements.
Historically, client applications that co-locate user preferences in an accounting database risk locking conflicts between high-frequency telemetry writers and UI configuration updates. Furthermore, non-atomic configuration writes frequently corrupt settings files if the computer is turned off or battery dies during an update.
For the live graph, keeping an unbounded list of data points leads to memory leaks, while naive average downsampling flattens transient network spikes, giving the false illusion of steady low bandwidth.

## Decision

1. **Independent Settings Storage**: User preferences reside in JSON files separate from the SQLite usage ledger.
2. **Atomic Replacement Guarantee**: Settings are serialized to memory, flushed to a temporary file, and atomically swapped into place via OS filesystem operations.
3. **Resilient Failure Mode**: Corrupted JSON triggers an automatic backup and fallback to safe defaults, logging a diagnostic event instead of throwing an unhandled startup exception.
4. **Ring Buffer with Peak Preservation**: The live graph uses a pre-allocated circular ring buffer. Downsampling for UI rendering evaluates the maximum value within each bucket ($\max(p_{\text{down}})$ and $\max(p_{\text{up}})$) alongside average values, ensuring transient latency and throughput spikes remain visually evident.

## Alternatives Considered

- Storing UI configuration in SQLite alongside telemetry: Rejected to prevent database schema entanglement and reader/writer lock contention between UI preference updates and the 1-second accounting queue.
- Directly overwriting `settings.json` in-place: Rejected due to the risk of 0-byte or corrupted files during power failure or unexpected process termination.
- Naive box-filter average downsampling for graphs: Rejected because average downsampling masks sub-second spikes critical for troubleshooting network contention.

## Consequences

- Settings modifications do not compete with SQLite WAL writers.
- Memory footprint for graph history is strictly deterministic ($O(N)$ bounded pre-allocated array).
- Visual live graphs truthfully expose transient network saturation spikes.
