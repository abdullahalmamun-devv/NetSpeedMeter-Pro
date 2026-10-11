# ADR-003: Measurement scope and identity

## Status

Accepted — Formally approved by Project Director following V1 Foundation Wave 1 integration and verification on 11 October 2026.

Canonical `ScopePolicy`, `RouteProvider` (dual-stack IPv4/IPv6 egress route and candidate detection), `ScopeStabilizer` (route flapping suppression with 3.0s debounce), and dynamic `AutoPrimary` evaluation have been integrated directly into the production `Collector` and `MeasurementEngine.RebaselineForScopeChange` pipeline. Verified by automated tests `V1-SCOPE: AutoPrimary follows route changes with clean rebaseline and preserved session bytes`, `V1-SCOPE: Route flapping debounce suppresses spurious revisions and commits after stabilization`, and `V1-SCOPE: Dual-stack disjoint egress flags ambiguity and fails closed`. Dual-stack disjoint egress correctly fails closed without fabricated merges. Scope revision increments establish clean baselines with 0 throughput spikes and preserve cumulative session bytes.

Superseded claim history preserved: originally proposed in P0, audited on 11 October 2026 as needing production loop integration, now integrated into `SpeedMeter.Application.Collector` and accepted with executable Windows verification evidence.

## Context

PRD 8 defines primary, selected, validated aggregate and raw diagnostic scopes. The production engine accepts a single explicit interface and resets transient session totals on scope selection. Standalone ScopePolicy/RouteProvider code is a feasibility experiment; it is not wired to production Collector/App. One selected boundary avoids automatically summing physical and tunnel bytes but does not prove route selection, topology recognition or aggregate deduplication.

## Decision

Keep explicit selected-interface scope as the first vertical slice. Future canonical scope contains immutable scope ID/revision, source method, boundary members and membership times. Collection epoch, provider counter epoch, user session and scope revision remain separate concepts. Preserve historical intervals across visible selection changes. Quotas bind to their saved logical scope. Auto and aggregate modes remain gated on topology evidence; unresolved topology asks for a boundary or reports a scoped partial view.

## Alternatives

Largest-traffic interface guessing is not route evidence. Summing all active interfaces risks physical/tunnel double counting. Treating adapter display name or SSID as permanent identity corrupts reporting after rename or network changes.

## Consequences

P0 user selects a scope; no fake automatic behavior is shown. Source epochs may be unknown when lifecycle is unobservable. Persistent history later needs immutable identity/membership and canonical interval keys, not the transient session reset behavior as a database deletion model.

## PRD Impact

Preserves 7, 8.2–8.5 and 22.2 invariants without waiving V1 auto/validated aggregate requirements. No VPN security guarantee follows from detecting a tunnel interface.

## Validation Required

Fixtures for identity change, stale in-flight read, epoch reset, scope change and missing adapter. Real Wi-Fi/Ethernet switch, IPv4/IPv6 primary routes, simultaneous egress, full/split VPN and virtual switch. Exact historical raw interval reconciliation and network membership tests before reports/quotas.
