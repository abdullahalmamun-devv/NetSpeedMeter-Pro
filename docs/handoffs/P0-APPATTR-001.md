# P0-APPATTR-001 handoff

TASK: P0-APPATTR-001 — Application traffic attribution feasibility spike, ETW analysis, PID lifecycle, and security model.

STATUS: Review requested. Only the Project Director may accept attribution feasibility findings.

IMPLEMENTED:
- Researched provider pathways for per-process traffic attribution on Windows:
  1. Kernel ETW Providers (`Microsoft-Windows-Kernel-Network`, `Microsoft-Windows-TCPIP`):
     - Capable of packet-level transport byte attribution across TCP IPv4/IPv6, UDP IPv4/IPv6, and QUIC.
     - Critical Security Limitation: Creating real-time trace sessions requires Administrative elevation (`SeCreateGlobalPrivilege` / Administrator role). Standard Windows user accounts cannot initiate kernel ETW sessions.
  2. IP Helper Extended MIB Tables (`GetExtendedTcpTable`, `GetExtendedUdpTable`):
     - Accessible to standard users without elevation.
     - Critical Measurement Limitation: Only reports connection states, endpoints, and owning PIDs; does NOT report byte counts or throughput metrics. Socket polling cannot measure UDP traffic or short-lived transfers.
  3. Packet Filter Drivers (NDIS filter, WinPcap, npcap):
     - Requires third-party signed kernel driver installation; violates local-first zero-driver standard-user architectural constraint.
- Implemented process identity domain model in `src/SpeedMeter.Windows/AppAttributionSpike.cs`:
  - `ProcessIdentity`: Couples `ProcessId` with `StartTimeUtc` to defeat PID recycling/reuse vulnerabilities.
  - `ApplicationIdentity`: Logical application grouping (AppId, DisplayName, PackageFamilyName) separate from transient process instances.
  - Invariant enforced: Application transport measurements remain in a separate measurement domain from interface physical octets. App totals are NEVER subtracted from adapter totals to create a fictitious "Unknown" bucket.
- Authored test fixtures in `tests/SpeedMeter.Checks/AppAttributionFixtures.cs`:
  - Verified PID reuse immunity: identical PID with distinct start timestamp is identified as recycled.
  - Verified separation between `ProcessIdentity` and `ApplicationIdentity`.
  - Verified standard-user capability mapping (`AttributionCompleteness.StandardUserRestricted`).

FILES CHANGED:
- `src/SpeedMeter.Windows/AppAttributionSpike.cs`
- `tests/SpeedMeter.Checks/AppAttributionFixtures.cs`
- `tests/SpeedMeter.Checks/Program.cs`
- `docs/tasks/P0-APPATTR-001.md`
- `docs/handoffs/P0-APPATTR-001.md`

DESIGN DECISIONS:
- Standard user execution must report `AttributionCompleteness.StandardUserRestricted` rather than pretending to provide complete packet capture.
- Per-application monitoring must be gated to V1.5 and requires either an explicit optional elevated helper service or user UAC consent.
- Adapter octets and application transport bytes are separate accounting domains.

TESTS EXECUTED:
- `scripts/check.ps1 -Windows`: 66/66 checks passed with exit code 0.

ACTUAL RESULTS:
- Feasibility spike confirms mathematical and identity invariants.
- Elevation constraints clearly identified and documented.

KNOWN LIMITATIONS:
- Full ETW session parsing is not implemented in production pipeline (by design for P0 feasibility).

RISKS:
- Introducing a privileged background helper in V1.5 expands attack surface and requires strict local IPC authentication and privacy threat modeling.

RECOMMENDED NEXT STEP:
- Security Engineer and Architect review `P0-APPATTR-001`. Director approves and updates ADR-007.
