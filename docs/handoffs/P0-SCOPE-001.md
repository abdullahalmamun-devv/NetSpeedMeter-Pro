# P0-SCOPE-001 handoff

TASK: P0-SCOPE-001 — Canonical network scope engine, routing discovery, and VPN/aggregation topology rules.

STATUS: Review requested. Only the Project Director may accept scope contracts or declare integration.

IMPLEMENTED:
- Created domain scope models in `src/SpeedMeter.Core/ScopeDomain.cs`:
  - `ScopeMode`: `AutoPrimary`, `SelectedAdapter`, `ValidatedAggregate`, `RawDiagnostic`.
  - `TopologyState`: `SinglePrimary`, `DualStackDisjoint`, `AmbiguousMultipleDefaults`, `VpnTunnelActive`, `NoActiveEgress`, `SelectedAdapterMissing`.
  - `InterfaceClassification`: `PhysicalEthernet`, `PhysicalWireless`, `VpnTunnel`, `VirtualBridge`, `Other`.
  - `AdapterScopeCandidate`: immutable candidate description with link speed, route metric, and classification.
  - `CanonicalScopeDecision`: immutable scope evaluation snapshot including monotonic scope revision, active adapter list, primary adapter ID, and ambiguity flag.
  - `ScopePolicy`: deterministic evaluation engine implementing all four PRD scope modes.
- Implemented Windows routing and interface classifier in `src/SpeedMeter.Windows/RouteProvider.cs`:
  - Uses native IP Helper `GetBestInterface` (destination `8.8.8.8`) to look up the kernel routing table's best egress interface.
  - Enumerates managed `NetworkInterface` instances and gateways.
  - Heuristically classifies VPN tunnels (WireGuard, Wintun, TAP, Cisco, OpenVPN, PPP) and virtual adapters (Hyper-V, VirtualBox, VMware, vEthernet).
- Implemented deterministic test suite in `tests/SpeedMeter.Checks/ScopeFixtures.cs`:
  - Verified 11 core scope scenarios: Ethernet only, Wi-Fi only, Ethernet ↔ Wi-Fi transitions with scope revision increments, missing selected adapter invariance (never switches silently), ambiguous equal-metric default routes, VPN default route isolation (physical transport excluded from summation), ValidatedAggregate safety rejection when VPN or virtual bridge coexists, and rebaseline without throughput spikes.
- Added live Windows verification check in `Program.cs`:
  - Observed 4 candidate interfaces on host system.
  - Correctly resolved `SinglePrimary` on the active Wi-Fi adapter (`cccadc86-3317-4857-a960-6e1752366e20`) matching Windows default route.

FILES CHANGED:
- `src/SpeedMeter.Core/ScopeDomain.cs`
- `src/SpeedMeter.Windows/RouteProvider.cs`
- `tests/SpeedMeter.Checks/ScopeFixtures.cs`
- `tests/SpeedMeter.Checks/Program.cs`
- `docs/tasks/P0-SCOPE-001.md`
- `docs/handoffs/P0-SCOPE-001.md`

DESIGN DECISIONS:
- Critical Invariant: Missing or down selected adapter in `SelectedAdapter` mode NEVER silently switches to an alternate adapter.
- Critical Invariant: VPN tunnel traffic is NEVER automatically summed with physical transport traffic. When VPN has default route, only the VPN adapter is counted in primary view.
- Critical Invariant: Ambiguous topologies (multiple default routes with identical metric) return `IsAmbiguous = true` rather than guessing.
- Scope revision increments force measurement engine rebaseline, guaranteeing zero spikes on adapter switches.

TESTS EXECUTED:
- `scripts/check.ps1 -Windows`: 56/56 checks passed with exit code 0.

ACTUAL RESULTS:
- All 11 scope scenarios pass deterministically.
- Windows host candidate evaluation matched live network topology without packet injection.

KNOWN LIMITATIONS:
- Full-tunnel vs split-tunnel routing distinction relies on default route (`0.0.0.0/0`) ownership. Split tunnels with specific subnets require per-route destination evaluation.

RISKS:
- Frequent route flapping on unstable networks will increment scope revision and force repeated rebaselines.

RECOMMENDED NEXT STEP:
- Architect and QA review `P0-SCOPE-001`. Director approves and integrates.
