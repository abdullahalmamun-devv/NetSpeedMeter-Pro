# P0-APPATTR-001

TASK ID: P0-APPATTR-001
OWNER: Windows Platform Engineer + Network Measurement Research Engineer
REVIEWER: Security Engineer + QA Engineer + Software Architect
PRD REFERENCES: F20, F21, F22, F23; AC-P01–P08; §§13–14
RELEASE: P0 feasibility
DEPENDENCIES: P0-ARCH-001
OBJECTIVE: Feasibility spike on application traffic attribution, kernel ETW, PID lifecycle, security boundaries, and event completeness
IN SCOPE:
- Process identity model: PID + process creation timestamp (resilient to PID reuse)
- Separation of ProcessIdentity from ApplicationIdentity
- Evaluation of ETW network providers (Microsoft-Windows-Kernel-Network, Microsoft-Windows-TCPIP) vs socket tables
- Evaluation of transport protocols: TCP IPv4/IPv6, UDP IPv4/IPv6, QUIC
- Security & privilege model: standard-user constraints vs elevated helper necessity
- Incompleteness and event loss accounting (never subtract adapter totals to create fake unknown buckets)
OUT OF SCOPE:
- Production Apps dashboard UI (strictly V1.5)
- Automatic summation of app bytes with adapter octets (violates measurement invariants)
EXPECTED FILES:
- src/SpeedMeter.Windows/AppAttributionSpike.cs
- tests/SpeedMeter.Checks/AppAttributionFixtures.cs
- docs/handoffs/P0-APPATTR-001.md
- docs/reviews/P0-APPATTR-001.md
ACCEPTANCE CONDITIONS:
- PID reuse immunity demonstrated via ProcessIdentity (PID + StartTime)
- Distinct ProcessIdentity vs ApplicationIdentity models
- Security review confirms privilege requirements and cross-user data isolation
- No forced reconciliation between adapter counters and application transport totals
TEST REQUIREMENTS:
- Unit fixtures verifying ProcessIdentity equality, PID recycling detection, and completeness states
STATUS: In progress
