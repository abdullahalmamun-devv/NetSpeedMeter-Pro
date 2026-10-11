# ADR-002: Traffic counter provider

## Status

Accepted with documented validation limits — Project Director & Independent Audit review, 11 October 2026. The real IP Helper/GetIfTable2 Win32 provider is accepted as the canonical adapter traffic source for SpeedMeter Pro. Documented validation limits: the existing implementation is verified on 35 adapters with typed snapshots and microbenchmarks (<1 ms read latency), but the independent 60-second multi-adapter synchronized accuracy verification and long-run soak trials remain scheduled for V1 production testing.

Superseded claim preserved: previously “Accepted — P0 Gate review. Verified with typed AdapterObservation and 0.69 ms sampling latency across 35 adapters.” Historical latency is a preliminary microbenchmark and does not establish complete long-run soak performance. See [audit](../audit/REQUIREMENTS-ADR-AUDIT.md).

## Context

PRD 7 requires exact raw counter deltas and explicit measurement domains. The current provider reads `InOctets`/`OutOctets`, uses interface GUID instead of permanent index identity, excludes loopback, checks its x64 native layout and frees table memory. The existing test brackets a managed counter reading; it is not the 60-second independent accuracy experiment.

## Decision

Retain IP Helper interface counters for V1 adapter traffic. Put typed availability, method/version, safe diagnostic details and observed source-epoch evidence behind a provider-neutral contract. Use GUID plus observed lifecycle generation; retain LUID/index as metadata where needed for route joins, never as an eternal historical identity. Treat counter decreases, down/up, disappearance/reappearance and known suspension as boundaries requiring rebaseline. Do not promise to detect recreation events the provider never observed.

## Alternatives

Managed network APIs offer useful parity checks but no evidence yet to replace the current native path. Performance counters or shell polling add a different measurement/operational contract and are not needed. ETW is an independent app domain, not a replacement for canonical adapter usage.

## Consequences

Standard-user local collection remains the target. Adapter bytes can include LAN, multicast and tunnel traffic; UI must say adapter traffic. Native ABI assumptions and failure mapping need explicit tests. Unknown epoch continuity or partial inventory cannot become a successful complete reading without evidence.

## PRD Impact

Supports 7.1–7.5, 8.1, 21.3 and AC-M/AC-A. Does not establish auto routes, VPN deduplication, Windows support or exact independent measurement accuracy.

## Validation Required

Deterministic typed failure and epoch tests; native size/field/counter conformance; record actual token elevation status; real removal/reconnect and reset behavior; synchronized 60-second same-layer comparison; Windows 11 matrix; overhead measurement. No permission-denial experiment is claimed unless actually run.
