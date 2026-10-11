# Initial slice verification

Verified locally on **11 October 2026 (Asia/Dhaka)**.

## Environment

- Host reports Microsoft Windows NT **10.0.19045.0** (Windows 10 build 19045).
- x64 application; 4 logical processors reported.
- Local SDK **10.0.401**; runtime **.NET 10.0.12**.
- WPF screenshot captured at the host's current DPI setting.

This is evidence for this machine only. It does **not** establish the PRD's Windows 11 support matrix, approve a Windows 10 product port, or satisfy the full P0/V1 release gates.

## Results

| Check | Result |
|---|---|
| Release solution build | Passed; zero warnings, zero errors |
| Deterministic core fixtures | 14 passed |
| Windows counter/layout comparison | Passed on 35 adapters against the managed Windows network API |
| Real WPF app launch | Passed; 8 valid readings across the smoke workflow |
| Actual traffic | Nonzero Wi-Fi download/upload observed; no test traffic generated |
| Dashboard and measurement snapshot | Values matched |
| Byte/bit unit switch | Display matched conversion; raw session total preserved |
| Pause | Missing rates shown; session totals preserved |
| Reset while paused | Totals cleared; collection stayed paused |
| Resume | Rebaseline followed by valid live readings |
| Dashboard / widget render | Inspected both PNGs; corrected dropdown contrast before final verification |

The native-counter test brackets a managed API reading with native readings and checks that counters fall within the bracket. It validates layout and counter semantics; it is **not** the PRD's 60-second independent accuracy experiment and cannot prove provider completeness.

Fixture coverage includes exact counter deltas, elapsed-time conversion, delayed ticks, counter resets, pauses, selected-adapter disappearance, reconnect, scope changes, wall-clock reversal, invalid monotonic intervals, session reset, overflow atomicity and unknown link speed. Acceptance IDs in the test names are traceability references, not claims that every release-level scenario for those IDs is complete.

## Artifacts

Local generated files (ignored by Git):

- `artifacts/smoke-20261011-034942/smoke.json`
- `artifacts/smoke-20261011-034942/dashboard.png`
- `artifacts/smoke-20261011-034942/widget.png`

Repeat with `scripts/check.ps1 -Windows` and `scripts/smoke.ps1`. The smoke script exits nonzero when its live-reading or interaction assertions fail.

## Not yet verified

Windows 11 integration; default routes and VPN topologies; suspend/hibernate on real hardware; Explorer restart; mixed-DPI monitor removal; keyboard/Narrator and high contrast; independent 60-second accuracy; resource budgets; soak reliability; signed deployment and update recovery. Persistent history and ETW attribution are not implemented yet.

## Tooling note

The managed filesystem sandbox denied NuGet's standard user configuration lookup even with an explicit local config. The successful build/restore ran through an approved command outside that sandbox. Package sources remain empty, all project dependencies are SDK/framework references, and SDK/package/CLI working directories are local to the workspace. No administrator manifest or system-wide SDK installation is required by the app.
