# SpeedMeter Pro

A high-performance, truthful Windows desktop network telemetry, accounting, and visualization application, engineered strictly from `Premium-Speed-Meter-PRD.md`.

**Status: V1.0 Wave 4 complete; Gate D formally passed.** Gate A, B, C, and D work remain integrated. The Wave 4 product-authority decision accepts repeated UTC labels across recorded clock segments under the byte and uncertainty safeguards in ADR-011. Real sleep, live clock transitions, Windows 11, and whole-app shutdown evidence remain designated as public release-gate work.

The executable Windows fixture run passed 235 checks, with 1 intentional skip and 0 failures (228 passed in standard `check.ps1`). The post-change native WPF smoke passed all 18 assertions on Windows 10 x64. These results do not validate real hardware sleep or Windows 11.

---

## Quick Start

Windows x64 and .NET SDK **10.0.401** are required. This repository includes a local SDK configuration under `.tools/dotnet`; scripts use it first, falling back to an installed system SDK. The application runs as a standard Windows user without requiring administrative privileges.

```powershell
.\scripts\build.ps1
.\scripts\check.ps1 -Windows
.\scripts\smoke.ps1
.\scripts\run.ps1
```

---

## Core Architecture & Implemented Capabilities

### 1. Telemetry & Measurement Engine
- **Native Counter Acquisition:** Real-time 64-bit octet counters queried via Win32 IP Helper `GetIfTable2`. Loopback interfaces excluded.
- **Monotonic Timing Domain:** Actual elapsed time from Windows QPC drives rate calculations; UTC places observations in history. A separate awake-time counter helps detect missed suspend notifications.
- **Dynamic Route-Aware Scope:** Automatic primary egress detection (`RouteProvider`) with a 3.0-second debounce stabilizer (`ScopeStabilizer`) to eliminate route flapping. Preserves cumulative session octets while seamlessly rebaselining rates upon route handoff.
- **Scope UI Protection:** Programmatic binding guards prevent UI adapter selection from overriding canonical `AutoPrimary` routing. When a selected adapter disconnects, the selection identity is preserved without silent fallback to another adapter.

### 2. Accounting Stream & Persistence
- **Canonical Half-Open Intervals:** Persistent SQLite accounting stream structured into canonical `[StartUtcMs, EndUtcMs)` half-open intervals. Zero SQL `SUM()` queries over unpartitioned rows.
- **Deterministic Integer Splitting:** `IntegerSplitter` uses `Int128` precision to distribute usage across calendar/billing boundaries while guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) with zero truncation or rounding drift.
- **Lifecycle Gap Truth:** Pause and sleep gaps retain monotonic elapsed duration separately from observed UTC placement. A wall-clock jump creates a zero-width event marker and a new clock segment; affected calendar coverage is shown as uncertain. A normal quit confirms storage through flush barriers or reports failure.
- **High-Performance Storage:** Managed SQLite WAL mode engine (`SpeedMeter.Storage`) with single-writer bounded channel, `PRAGMA synchronous = FULL`, foreign key validation, and BLOB counter checkpoints.

### 3. Usage, Calendar, Billing & Quota
- **Timezone & Calendar Awareness:** Local calendar boundaries (Today, Yesterday, This Week, This Month, Billing Cycle) calculated by `CalendarRangeResolver` with dynamic month clamping (day 31 in February clamps to 28/29 without mutating the rule) and full DST transition handling (including non-1-hour transitions).
- **Quota Engine:** Multi-rule quota evaluation with integer basis points precision (e.g. 8000 = 80.00%, 10000 = 100.00%). Monotonic threshold crossings are deduplicated in SQLite (`quota_threshold_events`) and guaranteed to fire at most once per cycle across restarts.
- **Truthful Error States:** If the storage engine is unavailable or queries fail, historical cards and quota displays render truthful unavailable indicators (`—`) rather than displaying stale numbers. Monotonic query generations prevent out-of-order slow query overwrites.

### 4. User Interface & Windows Platform
- **WPF Navigation Shell:** Modern dark-theme desktop shell with full MVVM decoupling across 5 primary views (Overview, Live Traffic, Apps, Usage & History, Network Tools) and Settings.
- **PerMonitorV2 DPI Placement:** Native Win32 positioning pipeline (`PlacementHelper`) using `GetDpiForWindow`, `MonitorFromPoint`, and `SetWindowPos`. Robust across heterogeneous multi-monitor DPI scaling, negative virtual desktop origins, and taskbar docks.
- **Quick Panel & Floating Widget:** Accessible, movable desktop companion with tabular numerals, keyboard nudging, opacity control, and click-through recovery via the system tray menu (`DisableClickThroughMenuItem`).
- **Data Export:** Keyset-cursor paged CSV streaming and typed JSON export with formula injection neutralization (`=`, `+`, `-`, `@`, `\t`, `\r`) and atomic file replacement (`.tmp` -> target).

---

## Architectural Boundaries & Gating

- **Adapter Octets vs Application Attribution:** SpeedMeter Pro's canonical accounting tracks interface octets via IP Helper. Per-process network attribution is gated to **V1.5**; standard-user processes cannot capture kernel ETW network events without elevation or least-privilege ETW adjustments, which remain active research paths.
- **No In-Process DeskBand / Explorer Injection:** SpeedMeter Pro explicitly rejects Microsoft's deprecated DeskBand COM interfaces and Explorer memory injection. The taskbar companion is implemented as an out-of-process, borderless, top-level window anchored adjacent to the taskbar work-area boundary with an automatic floating fallback.
- **No Summation of VPN and Physical Interfaces:** Physical network adapters and virtual VPN tunnel adapters occupy separate measurement domains. SpeedMeter Pro never automatically sums VPN and physical counters.
- **Local-First & Standard User:** Operates completely offline without background cloud services, mandatory user accounts, or telemetry phone-home.

---

## Repository Structure

- `src/SpeedMeter.Core`: Pure domain contracts, measurement math, calendar resolver, quota evaluator, integer splitting, and immutable models. Zero external dependencies.
- `src/SpeedMeter.Windows`: Native Win32 interop (IP Helper `GetIfTable2`, Win32 routing table, PerMonitorV2 placement helpers, taskbar query helpers).
- `src/SpeedMeter.Storage`: SQLite database engine, WAL persistence, migrations, and keyset pagination.
- `src/SpeedMeter.Application`: High-level application services (`Collector`, `UsageHistoryService`, `QuotaService`, `SettingsService`, `ExportService`). Decouples UI from storage mechanics.
- `src/SpeedMeter.App`: WPF presentation layer, XAML views, ViewModels, design tokens, Quick Panel, and floating widget companion. Completely decoupled from direct storage and SQLite references.
- `tests/SpeedMeter.Checks`: Standalone, dependency-free verification fixture runner covering core math, storage durability, lifecycle invariants, DPI placement, and live Windows hardware checks.
- `scripts`: PowerShell automation for clean builds, test execution (`check.ps1`), native UI smoke testing (`smoke.ps1`), and application launch (`run.ps1`).
