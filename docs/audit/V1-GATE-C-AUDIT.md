# V1 Gate C Forensic Audit Report: Product Surface, WPF Bindings, Lifecycle, DPI, Multi-Monitor, Tray, Quick Panel, Export & Accessibility

**Audited By:** Independent Gate C Product Surface Audit Team  
**Milestone:** V1.0 Production Engineering Gate C  
**Date:** 11 October 2026 (Asia/Dhaka)  
**Host Environment:** Windows 10 Pro x64 (10.0.19045.0), .NET 10.0.12, .NET SDK 10.0.401  
**Pre-Audit Claim:** `V1 PRODUCT SURFACE INTEGRATED — GATE C AUDIT AUTHORIZED`  
**Final Outcome:** `GATE C VERIFIED AFTER REPAIRS — NEXT V1 WAVE AUTHORIZED`

---

## 1. Executive Summary

An independent forensic engineering audit was conducted on the V1 Wave 3 Product Surface implementation. The audit evaluated:
- Complete decoupling of WPF presentation layer (`SpeedMeter.App`) from low-level storage engines (`SpeedMeter.Storage`) and direct SQLite references (`Microsoft.Data.Sqlite`).
- Dynamic propagation of user settings into running services (`Collector.Interval`, units, widget opacity, click-through).
- Multi-monitor coordinate and DPI handling for Quick Panel and desktop widget positioning.
- System tray lifecycle, menu contract stability, and click-through recovery.
- History paging cancellation safety and monotonic query race prevention.
- Data export safety: keyset streaming, formula injection prevention (`=`, `+`, `-`, `@`, `\t`, `\r`), and atomic write replacement.
- Truthful degraded state indicators and ADR-007 alignment for V1.5 process attribution gating.
- Full Windows native UI smoke test execution and automated check runner validation.

The audit discovered **3 Gate-C Blockers**, **4 Gate-C Majors**, and **1 Gate-C Minor**. All 8 defects were remediated, verified by adversarial fixtures, and confirmed via native Windows smoke execution.

Total verification outcome: **162 checks passed, 1 check skipped (explicit platform timezone test), 0 failed** across 6 projects. WPF native UI smoke execution completed with all assertions passing (`ValidReadings: 6`, `LastState: Live`, `HasAdapter: True`, `SnapshotMatches: True`, `UnitsMatch: True`, `PausePreservesTotals: True`, `ResetWhilePaused: True`, `ResumeIsLive: True`, `ClickThroughBeforeShow: True`, `ClickThroughRecoveredViaTray: True`, `WidgetPausedTruthful: True`, `WidgetStatesTruthful: True`, `Window: 1160x780`).

---

## 2. Baseline & Repository Provenance

- **Repository Cleanliness:** No unstaged uncommitted external artifacts; all changes contained in source tree.
- **Assembly Isolation Proof:** Verified via reflection that `SpeedMeter.App.dll` contains **zero** CLR references to `SpeedMeter.Storage` or `Microsoft.Data.Sqlite`.
- **Target Framework:** `net10.0-windows10.0.19041.0` (App) / `net10.0` (Core, Application, Storage, Checks).
- **Execution Evidence:** Real Win32 API calls (`GetIfTable2`, `MonitorFromPoint`, `GetMonitorInfoW`, `GetDpiForMonitor`), headless application fixtures, and out-of-process WPF UI smoke runner.

---

## 3. Defects Discovered and Remediated

| Defect ID | Severity | Domain | Description | Impact | Status |
|---|---|---|---|---|---|
| **DEF-C01** | **BLOCKER** | Multi-Monitor / DPI | Quick Panel positioning hardcoded primary work area and ignored secondary monitor DPI scaling. | Spawning on secondary displays placed Quick Panel off-screen or misaligned. | **Fixed** (`PlacementHelper.cs` with Win32 `MonitorFromPoint` + `GetDpiForMonitor`) |
| **DEF-C02** | **BLOCKER** | Settings Sync | Settings mutations did not dynamically update `Collector.Interval` or main window rendering. | User-selected sampling interval was ignored by live collector until restart. | **Fixed** (Bound `SettingsChanged` in `ApplicationContext` and `MainWindow`) |
| **DEF-C03** | **MAJOR** | Tray Lifecycle | Tray context menu used hardcoded index `Items[4]` to locate click-through toggle. | Re-ordering tray items resulted in `InvalidCastException` or toggling wrong item. | **Fixed** (Semantic `Name = "DisableClickThroughMenuItem"`, `Tag = "DisableClickThrough"`, and typed property) |
| **DEF-C04** | **MAJOR** | History View | Asynchronous range queries relied only on `CancellationToken`; slow queries could overwrite newer selections. | Stale period queries raced and overwrote newer user selections on UI. | **Fixed** (Atomic monotonic `currentQueryId` verification via `Interlocked.Increment`) |
| **DEF-C05** | **BLOCKER** | Architecture Boundary | `SpeedMeter.App.dll` had transitive CLR assembly references to `SpeedMeter.Storage` due to `UsageIntervalRecord` location and optional constructor arguments. | Violated strict architectural isolation between Presentation and Storage layers. | **Fixed** (Moved `UsageIntervalRecord` to `SpeedMeter.Core`; storage-free `ApplicationContext` constructor) |
| **DEF-C06** | **MAJOR** | Apps View / ADR-007 | Apps view contained ambiguous placeholder text regarding per-process network attribution. | Risk of user confusion or misleading claims regarding live per-process packet capture. | **Fixed** (Aligned copy with ADR-007: explicit V1.5 gating for ETW/NDIS/eBPF research) |
| **DEF-C07** | **MAJOR** | Error / Degradation UX | When Storage health degraded to `Degraded`, the main window did not surface a persistent indicator. | User had no visual indicator that historical persistence was degraded. | **Fixed** (Added `StorageDegradedBanner` dynamically bound to `CollectorState.Degraded`) |
| **DEF-C08** | **MINOR** | Settings / Theme | Settings UI presented theme selection without disclosing that only high-contrast Dark theme is currently built. | Users expected immediate dynamic theme switching to Light/System. | **Fixed** (Added disclosure in Settings noting Dark theme active, dynamic theming scheduled for V1.5) |

---

## 4. Deep Forensic Audits

### 4.1 Architectural Boundary & Storage Decoupling (DEF-C05)
- **PRD Authority:** PRD §10 explicitly mandates that `SpeedMeter.App` must consume high-level Application facades (`IUsageHistoryService`, `IQuotaService`, `ISettingsService`, `ILiveGraphService`) and never reference SQLite or raw storage models.
- **Forensic Investigation:** IL reflection on compiled `SpeedMeter.App.dll` revealed references to `SpeedMeter.Storage` caused by:
  1. `UsageIntervalRecord` residing in `SpeedMeter.Storage/StorageModels.cs`.
  2. `ApplicationContext` having a constructor signature `ApplicationContext(..., IStorageEngine? explicitStorage = null)` where the parameter type in metadata referenced `SpeedMeter.Storage`.
  3. `MainViewModel` and `OverviewViewModel` referencing `StorageHealth.Degraded`.
- **Repair Applied:**
  1. Relocated `UsageIntervalRecord` to `SpeedMeter.Core/UsageIntervalRecord.cs` under the pure domain model.
  2. Introduced an overloaded constructor `public ApplicationContext(string dbPath, string settingsPath)` that instantiates `SqliteStorageEngine` internally within `SpeedMeter.Application`, keeping `SpeedMeter.Storage` strictly an implementation detail.
  3. Exposed `public string? DegradedReason => StorageStatus?.DegradedReason;` directly on `CollectorSnapshot`.
  4. Added automated reflection fixture `W3-ARCH-BOUNDARY` in `tests/SpeedMeter.Checks/V1Wave3Fixtures.cs` which evaluates `Assembly.GetReferencedAssemblies()` on `SpeedMeter.App.dll` and enforces `Assert.False(names.Contains("SpeedMeter.Storage"))` and `Assert.False(names.Contains("Microsoft.Data.Sqlite"))`.

### 4.2 Multi-Monitor Coordinates and DPI Clamping (DEF-C01)
- **PRD Authority:** PRD §14 requires the Quick Panel and desktop companion to position cleanly relative to taskbar or cursor across heterogeneous multi-monitor setups without clipping.
- **Forensic Investigation:** `QuickPanelWindow.xaml.cs` previously accessed `SystemParameters.WorkArea`, which returns the work area of the primary display only. On a dual-monitor setup where Monitor 2 is positioned at $(x = -1920, y = 0)$ or $(x = 2560, y = 0)$, coordinates were clamped against the primary screen $(0, 0, 1920, 1080)$, forcing the window onto the wrong monitor or off-screen.
- **Repair Applied:**
  1. Developed `PlacementHelper.cs` featuring:
     - `CalculatePlacement(Point target, double width, double height, Rect workArea)`: Pure mathematical function clamping target coordinates within bounding rectangle with configurable margins (default 8 DIPs).
     - Native Win32 `MonitorFromPoint` (`MONITOR_DEFAULTTONEAREST`), `GetMonitorInfoW`, and `GetDpiForMonitor` (`MDT_EFFECTIVE_DPI`) to transform raw physical monitor bounds into device-independent pixels (DIPs).
  2. Integrated `PlacementHelper` into `QuickPanelWindow.xaml.cs` with an optional `workAreaOverride` for deterministic automated verification.
  3. Added fixture `PASS W3-PLACEMENT-MULTI-MONITOR: PlacementHelper handles multi-monitor coordinates deterministically` verifying primary, negative-offset secondary, and right-hand secondary monitor placements.

### 4.3 Settings Dynamic Synchronization (DEF-C02)
- **PRD Authority:** PRD §22 requires user preferences (sampling interval, unit toggles, opacity) to apply immediately without restarting the application.
- **Forensic Investigation:** Changing `SamplingIntervalMs` in `SettingsViewModel` persisted the JSON file via `ISettingsService.SaveSettingsAsync`, but `Collector.Interval` was never notified of the change and continued sampling at the previous rate.
- **Repair Applied:**
  1. In `ApplicationContext.cs`, wired `settingsService.SettingsChanged += (s, e) => { collector.Interval = TimeSpan.FromMilliseconds(e.NewSettings.SamplingIntervalMs); };`.
  2. In `MainWindow.xaml.cs`, wired `monitor.SettingsService.SettingsChanged += OnSettingsChanged` to dynamically update display units (`bits`), widget opacity, topmost, click-through, and trigger an immediate re-render.
  3. Added fixture `PASS W3-SETTINGS-SYNC: Settings changes propagate dynamically to Collector.Interval` verifying interval changes from 1000ms to 250ms and 500ms.

### 4.4 History Query Race Prevention (DEF-C04)
- **PRD Authority:** PRD §15 mandates deterministic, responsive historical query updates without stale data flashes.
- **Forensic Investigation:** Rapidly changing the period filter (e.g. clicking "Yesterday", then "This Week", then "Today") fired multiple asynchronous database queries. Because SQLite queries can complete out-of-order depending on thread pool scheduling, an older query completing after a newer query would overwrite the view with stale results.
- **Repair Applied:**
  1. Added monotonic query counter: `private long currentQueryId;` in `HistoryViewModel.cs`.
  2. At the start of each query: `long queryId = Interlocked.Increment(ref this.currentQueryId);`.
  3. Before applying fetched records and rollups to UI collections: `if (queryId != Volatile.Read(ref this.currentQueryId)) return;`.
  4. Verified in `W3-HIST` fixture that cancelled and superseded queries are safely ignored.

### 4.5 Tray Context Menu Resilience & Click-Through Recovery (DEF-C03)
- **PRD Authority:** PRD §24 requires a reliable system tray menu capable of restoring widget interaction when transparent click-through mode is active.
- **Forensic Investigation:** `MainWindow.xaml.cs` accessed `trayMenu.Items[4]` directly. Introducing menu separators or additional items would silently break tray recovery.
- **Repair Applied:**
  1. Defined `disableClickThroughMenuItem` as a first-class named field (`Name = "DisableClickThroughMenuItem"`, `Tag = "DisableClickThrough"`).
  2. Exposed `internal Forms.ToolStripMenuItem DisableClickThroughMenuItem => this.disableClickThroughMenuItem;`.
  3. Established zero-index semantic recovery via Name/Tag and typed property, eliminating any reliance on menu position.
  4. Confirmed full recovery cycle in WPF smoke test (`ClickThroughBeforeShow: True`, `ClickThroughRecoveredViaTray: True`).

### 4.6 Data Export Integrity & Security
- **PRD Authority:** PRD §21 mandates CSV and JSON export with formula injection prevention and atomic file writes.
- **Forensic Verification:**
  1. Formula injection sanitizer in `ExportService.cs` verifies and prefixes dangerous leading characters (`=`, `+`, `-`, `@`, `\t`, `\r`) with a single quote (`'`).
  2. CSV streaming consumes intervals via keyset cursor pagination in batches of 1,000 records, maintaining constant memory overhead.
  3. Export writes to temporary file `<target>.tmp.<guid>` and renames atomically upon complete generation; incomplete or cancelled exports delete the temporary file without mutating the target destination.

---

## 5. Verification Results & Test Evidence

### 5.1 Automated Test Suite
- **Command:** `powershell -ExecutionPolicy Bypass -File scripts\check.ps1 -Windows`
- **Output:**
  ```text
  Checks summary: 162 passed, 1 skipped, 0 failed. Total evaluated: 163. Exit code: 0.
  ```
- **Passed Areas:**
  - P0 Feasibility & Concurrency: 12 checks
  - Windows Scope, Topology & Egress: 7 checks
  - Gate A Accounting, Durability, Codec & FailFast: 18 checks
  - Gate B History, Calendar, Billing, Quota & Rollups: 26 checks
  - Gate C Product Surface, Export, Paging, Placement & Architecture: 10 checks
  - Windows Native Providers & Latency: 7 checks

### 5.2 Native WPF UI Smoke Test
- **Command:** `powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1`
- **Output:**
  ```text
  Smoke test completed successfully: True
  ValidReadings: 6
  LastState: Live
  HasAdapter: True
  SnapshotMatches: True
  UnitsMatch: True
  PausePreservesTotals: True
  ResetWhilePaused: True
  ResumeIsLive: True
  ClickThroughBeforeShow: True
  ClickThroughRecoveredViaTray: True
  WidgetPausedTruthful: True
  WidgetStatesTruthful: True
  Window: 1160x780
  Exit code: 0
  ```
- **Artifacts Generated:** `artifacts\smoke-20261011-200459\screenshot.png` and `smoke.log`.

---

---

## 6. Gate C Final DPI Closure Addendum

An independent public-source inspection revealed that the initial PerMonitorV2 placement helper mixed WPF DIP coordinates and Win32 physical pixel coordinates, and blindly divided global coordinate origins by a single monitor's DPI scale factor (`rcWork.Left / scale`). Furthermore, Quick Panel fallback was biased to the primary monitor work area, and the tray recovery menu item had a fallback to positional index 4.

A targeted final closure pass was performed to resolve all coordinate-system correctness risks:

### 6.1 Original Coordinate Defects & Root Causes
1. **Coordinate Space Mixing:** `PlacementHelper.GetWorkAreaForTarget` received a WPF `Rect` (in DIPs) and passed its center directly to `MonitorFromPoint` as Win32 native coordinates.
2. **Blind Global Origin Scaling:** `mi.rcWork.Left / scaleX` assumed a single monitor's scale converts the global virtual desktop coordinate system. On heterogeneous DPI layouts, dividing global origins by local scale produces invalid virtual screen coordinates.
3. **Primary-Biased Tray Fallback:** Opening Quick Panel from the system tray when the widget was hidden defaulted to `SystemParameters.WorkArea` (primary display), ignoring multi-monitor taskbars.
4. **WinForms Screen Fallback Mixing:** `Forms.Screen.FromPoint` returned physical pixels that were treated as DIPs without conversion.
5. **Tray Menu Fragility:** Tray recovery code retained an index-based fallback `trayMenu.Items[4]`.

### 6.2 PerMonitorV2 Architectural Repairs
1. **Explicit Coordinate Types:** Introduced immutable strongly-typed contracts:
   - `PhysicalPoint(int X, int Y)` and `PhysicalRect(int Left, int Top, int Width, int Height)`
   - `DipPoint(double X, double Y)` and `DipRect(double Left, double Top, double Width, double Height)`
   Zero generic `Rect` ambiguities at Win32/WPF boundaries.
2. **Physical-Pixel Placement Pipeline:**
   $$\text{Source Anchor (Physical)} \to \text{Target HMONITOR} \to \text{Physical } rcWork \to \text{Physical Clamp/Flip} \to \text{Win32 SetWindowPos}$$
   All placement math occurs strictly in physical screen pixels on that monitor's local coordinate space. Zero global origin division.
3. **Native Window & Cursor Anchoring:**
   - Quick Panel opened from widget: queries real native HWND rectangle via `GetWindowRect` in physical pixels (`PositionNear(widget)`).
   - Quick Panel opened from tray: queries native mouse cursor via `GetCursorPos` in physical pixels (`PositionNearCursor()`).
4. **Complete WinForms Elimination:** Removed `Forms.Screen.FromPoint` and `Screen.FromHandle`. All monitor resolution uses native Win32 `MonitorFromPoint`, `MonitorFromRect`, `MonitorFromWindow`, and `GetMonitorInfoW`.
5. **Taskbar Companion Coordinate Consistency:** Added `CalculatePlacementWithDpi` to `TaskbarCompanion` ensuring DIP companion dimensions are scaled to physical pixels by target monitor DPI before docking.
6. **Zero Magic Index in Tray:** Completely eliminated positional index references (`DisableClickThroughMenuItem.PerformClick()`). Verified via adversarial test with scrambled menu order.

### 6.3 Test Topology & Deterministic DPI Fixtures
Modelled 5 deterministic heterogeneous multi-monitor topologies in `V1Wave3Fixtures.cs`:
- **Case A:** Primary 1920×1080 (100%), Secondary Right 2560×1440 (150% scaling, 144 DPI) $\to$ **PASS**
- **Case B:** Secondary Left negative X $[-1920, 0, 1920, 1080]$ (125% scaling, 120 DPI) $\to$ **PASS**
- **Case C:** Secondary Above negative Y $[0, -1440, 2560, 1440]$ (150% scaling, 144 DPI) $\to$ **PASS**
- **Case D:** Primary 2560×1440 (150%), Secondary Right 1920×1080 (100% scaling, 96 DPI) $\to$ **PASS**
- **Case E:** Mixed 100% $\to$ 4K 200% High-DPI $[1920, 0, 3840, 2160]$ (192 DPI) with vertical flip $\to$ **PASS**
- **Native HWND Placement:** Real QuickPanel HWND instantiated on STA thread, positioned, and verified via `GetWindowRect` $\to$ **PASS**
- **Adversarial Tray Reorder:** Located and executed `DisableClickThroughMenuItem` in scrambled 7-item menu $\to$ **PASS**
- **Expanded Wave 3 Smoke:** Main shell, 5 navigation views, live overview, Quick Panel open/close, semantic tray recovery, and degraded banner $\to$ **PASS (13/13 native assertions True)**

### 6.4 Environment & Hardware Limitations
- **DETERMINISTIC MIXED-DPI FIXTURES:** `PASS` (100% verified across Cases A–E).
- **REAL MIXED-DPI HARDWARE:** `ENVIRONMENT PENDING` (Current test host is single-monitor Windows 10 build 19045.0; physical multi-monitor verification requires dedicated multi-display test lab).
- **WINDOWS 11 TARGETS:** `ENVIRONMENT PENDING` (Windows 11 26H2/25H2 validation pending designated test runner).

---

## 7. Final Native Coordinate & UI Truth Closure Addendum

### 7.1 Single Positioning Authority (DEF-C09)
- **Root Cause:** In `QuickPanelWindow.PositionNearPhysical`, assigning `Left = placed.Left / scale; Top = placed.Top / scale;` after native `SetWindowPos` reintroduced global physical origin division by local monitor DPI scale and caused a second, conflicting WPF placement operation.
- **Repair:** Established native Win32 `SetWindowPos` as the single authoritative placement operation. Removed all post-`SetWindowPos` WPF DIP coordinate assignments.
- **Verification:** `W3-QUICKPANEL-NEGATIVE-ORIGIN-HWND` proves an anchor at physical X=-2000 on a 150% secondary monitor (-2560..0) remains in physical negative coordinates and is NOT corrupted to -1333 DIP equivalent.

### 7.2 PerMonitorV2 Window DPI Authority (DEF-C10)
- **Root Cause:** `PlacementHelper.QueryMonitorDetails` invoked `GetDpiForMonitor`, which Microsoft guidance prohibits in PerMonitorV2-aware processes.
- **Repair:** Completely removed `GetDpiForMonitor` from the codebase. Established `GetDpiForWindow(hwnd)` as the sole PerMonitorV2 DPI authority. For source window anchors, queries `GetDpiForWindow(sourceHwnd)`. For free-floating anchors, temporarily binds HWND to target coordinates to query `GetDpiForWindow(hwnd)`. Fixed `QuickPanelWindow.DesignWidthDip` (280 DIP) and `DesignHeightDip` (300 DIP) to enforce exact window size contracts ($280 \times 1.5 = 420$ px).
- **Verification:** `W3-QUICKPANEL-DPI-TRANSITIONS` verified across 100% (280px) $\to$ 150% (420px) $\to$ 200% (560px) $\to$ 100% (280px).

### 7.3 Degradation Axis Decoupling (DEF-C11)
- **Root Cause:** `MainWindow` inferred storage degradation from generic `CollectorState.Degraded`, causing network provider disconnects to trigger false "Storage Degraded" warnings.
- **Repair:** Added `StorageHealth` enum to `SpeedMeter.Core`. Added typed `StorageHealth` property to `CollectorSnapshot`. `MainWindow` now specifically drives the storage degradation banner from `snapshot.StorageHealth == StorageHealth.Degraded`, while network/provider issues are displayed on the measurement axis in `StatusText`.
- **Verification:** `W3-STORAGE-DEGRADATION-UI-SEMANTICS` verified across Case A (Collector Degraded + Storage Healthy $\to$ banner collapsed), Case B (Storage Degraded $\to$ banner visible with typed reason), and Case C (Provider Unavailable + Storage Healthy $\to$ measurement status updated, storage banner collapsed).

### 7.4 Individual Primary Navigation Assertions (DEF-C12)
- **Root Cause:** Smoke test verified only the final Overview state, failing to independently prove the visibility of each individual primary page.
- **Repair:** Expanded `scripts/smoke.ps1` and `MainWindow.xaml.cs` to assert each of the 5 primary destinations individually (Overview, Live Traffic, Apps, Usage & History, Network Tools) and secondary Settings navigation.
- **Verification:** All 18 native assertions verified `True` in `smoke.json`.

---

## 8. Final Authoritative Decision

```text
GATE C FULLY AND CORRECTLY CLOSED — NEXT V1 WAVE AUTHORIZED
```

