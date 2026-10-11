# ADR-007: Application traffic attribution feasibility

## Status

Proposed — unresolved provider/least-privilege research. Project Director & Independent Audit review, 11 October 2026. Audit judgment: INSUFFICIENT EVIDENCE for full per-app attribution feasibility. Identity comparison records and a token-role query exist; no ETW/app-attribution provider or privileged helper exists. Standard-user execution on the tested prototype does not have permission to capture kernel network event traces; least-privilege alternatives (such as Performance Log Users group, explicit session security descriptors, user-mode trace providers, or an isolated authenticated helper service) remain unresolved research paths that must be evaluated and proved before V1.5.

## Context

PRD 13 and 29 require P0 feasibility before the V1.5 Apps feature. Application payload/transport observations are a separate domain from adapter octets. Attributing 95% of observed bytes alone cannot establish completeness if events were missed. The currently tested standard-user prototype does not provide usable byte attribution. Whether standard users can achieve byte attribution through least-privilege configurations or require an elevated background helper remains an open engineering question.

## Decision

Run a bounded ETW or supported-alternative prototype outside the production feature path prior to V1.5 implementation. Investigate least-privilege options (including Performance Log Users, session ACL adjustments, SystemTraceControl/user-mode tracing, or a narrow elevated background helper) before committing to a permission model. Report availability, elevation requirements, loss/completeness and unassigned traffic explicitly. Use PID plus process creation identity with authorized user/session attribution, not PID alone. Collect only required metadata; no payload capture. Independently measure collection completeness and attribution success in a declared comparable domain. Introduce an elevated helper only if evidence establishes its necessity and Security review approves a narrow authenticated protocol.

## Alternatives

Inferring app bytes from adapter totals or connection listings cannot prove attribution. Invented proportional allocations are invalid. Privileged service by default expands risk before proving benefit. Shipping the Apps dashboard before provider evidence encourages misleading completeness claims.

## Consequences

TCP/UDP/IPv4/IPv6/QUIC, loopback, VPN and shared-process cases may have different capability states. Partial remains partial; unassigned traffic stays visible. App measurements never feed the interface quota ledger as additional bytes. P0 research does not authorize V1.5 feature expansion.

## PRD Impact

7.1, 13, 21.2–21.3 and 29.1/29.3. Completeness assessment supplements the provider-observed attribution threshold; any promised limitations require a Director/product scope decision.

## Validation Required

Controlled independent workloads and denominator definition; TCP/UDP/IPv4/IPv6/QUIC; PID reuse and short-lived processes; dropped-event stress; VPN behavior; standard-user and denied elevation; user/session isolation and helper threat model if needed; CPU/memory overhead; privacy metadata review. Provider choice remains unresolved until measured.
