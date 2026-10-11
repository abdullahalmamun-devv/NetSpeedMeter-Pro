# P0-WINDOWS-001 handoff

TASK: P0-WINDOWS-001 — Real Windows adapter counter provider and typed availability contract.

STATUS: Review requested. Only the Project Director may accept Windows provider contracts or declare integration.

IMPLEMENTED:
- Updated `SpeedMeter.Windows.AdapterProvider` to implement `IAdapterCounterProvider` from `SpeedMeter.Core`.
- Implemented `ReadSnapshot()` returning typed `AdapterObservation` with explicit `ProviderAvailability` outcomes:
  - `Available`: successful `GetIfTable2` native enumeration, returning non-loopback hardware and virtual network interfaces.
  - `Unsupported`: non-Windows OS or non-x64 process with incompatible `MIB_IF_ROW2` layout (1352 bytes).
  - `PermissionDenied`: Win32 access denied (error 5).
  - `Failed`: other Win32 errors or parsing exceptions.
- Maintained standard-user zero-privilege execution: no elevated helpers, no active probes, and no shell process injection.
- Preserved existing `Read()` method for backward compatibility.
- Added live Windows verification check in `tests/SpeedMeter.Checks/Program.cs`: verified `ReadSnapshot()` against native `GetIfTable2` on Windows build 19045, confirming 35 observed interfaces with `ProviderAvailability.Available`.

FILES CHANGED:
- `src/SpeedMeter.Windows/AdapterProvider.cs`
- `tests/SpeedMeter.Checks/Program.cs`
- `docs/handoffs/P0-WINDOWS-001.md`

DESIGN DECISIONS:
- Never throw unhandled exceptions out of `ReadSnapshot()`; unexpected failures are safely mapped to `ProviderAvailability.Failed` without leaking internal file paths.
- Software loopback interface (Type 24) is filtered out as an ineligible scope.
- Interfaces are stably sorted: connected first (`IsUp`), then hardware, then alias name.

TESTS EXECUTED:
- `scripts/check.ps1 -Windows`:
  - `PASS Windows native layout and counter parity with managed OS API` (35 adapters compared)
  - `PASS Windows IAdapterCounterProvider typed snapshot contract` (35 adapters observed via GetIfTable2; availability: Available)

ACTUAL RESULTS:
- Native struct alignment verified on x64 (1352 bytes).
- Counters match managed `System.Net.NetworkInformation.IPv4InterfaceStatistics` within same sample window.
- Zero probe or network traffic generated.

KNOWN LIMITATIONS:
- Tested on Windows 10 Pro x64 (build 19045). Target Windows 11 matrix validation remains required when matching hardware/VM environment is available.
- Default route inference and split/full tunnel VPN detection belong to `P0-SCOPE-001`.

RISKS:
- Network interface index or GUID reuse after hibernate/network adapter cycle requires epoch boundary rebaseline.

RECOMMENDED NEXT STEP:
- Architect and QA review `P0-WINDOWS-001`. Director approves and integrates.
