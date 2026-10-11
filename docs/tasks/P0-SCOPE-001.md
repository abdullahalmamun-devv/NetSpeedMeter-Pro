# P0-SCOPE-001

TASK ID: P0-SCOPE-001
OWNER: Windows Platform Engineer + Network Measurement Engineer
REVIEWER: Software Architect + QA Engineer
PRD REFERENCES: F11, F12, F13, F14; AC-A01–A06; §§7–8
RELEASE: P0 feasibility
DEPENDENCIES: P0-WINDOWS-001, P0-MEASURE-001, P0-COLLECTOR-001
OBJECTIVE: Implement canonical scope engine, route detection, adapter transition handling, and VPN/aggregate topology rules
IN SCOPE:
- Scope domain models: ScopeMode, TopologyState, InterfaceClassification, ScopeCandidate, CanonicalScopeDecision
- Canonical scope policy implementing Auto/Primary, Selected Adapter, Validated Aggregate, and Raw Diagnostic
- Windows route/egress provider with VPN and virtual adapter classification
- Discontinuity and rebaseline enforcement on scope revision changes
- Deterministic fixture matrix covering 15+ routing, switching, VPN, and ambiguity scenarios
OUT OF SCOPE:
- Full automated packet inspection, kernel NDIS filter, or active internet probes
EXPECTED FILES:
- src/SpeedMeter.Core/ScopeDomain.cs
- src/SpeedMeter.Windows/RouteProvider.cs
- tests/SpeedMeter.Checks/ScopeFixtures.cs
- docs/handoffs/P0-SCOPE-001.md
- docs/reviews/P0-SCOPE-001.md
ACCEPTANCE CONDITIONS:
- Zero double-counting across physical and VPN boundaries
- Ambiguous topologies return explicit ambiguous state; never guess
- Selected adapter mode never switches silently when adapter disappears
- Scope revision changes force rebaseline without throughput spikes
TEST REQUIREMENTS:
- Ethernet only, Wi-Fi only, Ethernet ↔ Wi-Fi switch, adapter disconnect/reconnect, missing selected adapter, dual-stack, conflicting routes, VPN tunnel presence, aggregate rejection
STATUS: In progress
