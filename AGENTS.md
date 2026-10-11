# SpeedMeter Pro engineering rules

The product authority is `Premium-Speed-Meter-PRD.md`. The user has requested a multi-agent engineering office. The lead acts as Project Director; activate relevant specialists, with separate file ownership, instead of spawning every role for every task.

- Work in order: P0 feasibility, V1.0, V1.5, V2.0, independently gated extensions. Do not advance through an unpassed gate.
- Read `PROJECT_BOARD.md` and `IMPLEMENTATION_STATUS.md` before assigning work. Read relevant PRD sections and existing code before changes.
- Every meaningful task has owner, reviewer, PRD references, dependencies, scope, files, acceptance, tests and handoff. See `docs/engineering-office.md`.
- Only the Director declares integration, accepted architecture, feature Done or a passed gate, after inspecting evidence. Critical code requires independent review.
- Coordinate shared contracts before dependent edits. Do not overwrite another agent's work. There are currently four concurrent agent slots including the Director; queue roles when full.
- Preserve the real provider → collector → measurement → immutable snapshot → UI pipeline. No fabricated production data, misleading zeros, silently missing requirements or scope changes.
- Preserve integer counter deltas, actual monotonic durations, baseline/epoch boundaries and missing coverage. Never automatically sum VPN and physical traffic. Interface, application and test measurements remain separate domains.
- Local-first, standard-user UI, no mandatory account. Optional external requests require their documented enablement. Do not add privileged helpers or hosted services without the corresponding gate.
- Build/test meaningful integrations with the local scripts and appropriate Windows evidence. `dotnet test` without executed tests is not evidence; the existing executable fixture runner must actually run until/unless a test adapter is integrated.
- Record observed results and limitations. Windows build 19045 evidence does not validate Windows 11 support or authorize a Windows 10 product port.
- Keep board, implementation status, ADRs, evidence and provider limitations accurate. Do not mark a feature Done merely because it builds.

Default stack: C#, supported .NET 10 LTS, WPF, Windows APIs and planned SQLite. Keep domain independent of WPF/Windows/storage. No Electron, cloud backend or microservices without a justified requirement.
