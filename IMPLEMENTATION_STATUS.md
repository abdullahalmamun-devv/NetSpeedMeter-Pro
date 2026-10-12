# Implementation status

**Current release:** 1.0.0-rc.2 is the latest packaged, test-signed field/lab candidate, built from clean `RC2ReleaseSourceSha` `b0e425f34ac3033ee36bf2da27ee9d339a9b3bed`. It is not a public production release. RC.1 evidence remains preserved. GA remains blocked.
**Current milestone:** Gate E1 passed. RC.2/RC.3 consumer experience closure is **not verified** after independent review; Gate E2 field validation is not authorized. The prior RC.2 package drill passed, while the required updater-capable RC.2 → RC.3 app/restart/data-preservation flow, public hosting, production signing, interactive accessibility, and packet-level privacy evidence remain open. See `docs/evidence/RELEASE-EVIDENCE-v1.0.0-rc.2.md`.
**Updated:** 12 October 2026 (Asia/Dhaka).

## Build / test status

### RC.3 closure work in progress (12 October 2026)

- The seven unpublished RC.2 commits were preserved on `backup/consumer-closure-pre-audit`; the local starting HEAD was `8cfc8e96cbb10dfcdb93f35d5b134e58aac89028` and `origin/main` remained `37b5f57a086fcb9d11ae248e136b1c20f6ff78af` after fetch. No remote push has yet been verified for this work.
- The former installer fixture could select real `%LocalAppData%\SpeedMeterPro` for purge. Current account data impact cannot be conclusively established from available evidence. Installer and fixture guards now require owned targets and sentinel GUID test roots; see `docs/handoffs/V1-CONS-SAFETY-002.md` and the independent `docs/reviews/V1-CONS-SECURITY-002.md`. The reviewer still requested changes for the deferred batch and purge validation-to-delete race.
- The isolated RC.2 lab app and Setup were built and test signed from the working tree for updater-seed testing only. They are not an authoritative RC.2 field artifact. RC.3 source version was advanced to `1.0.0-rc.3` / `1.0.0.3`; no clean-tree RC.3 field installer or release-source SHA has yet been recorded.
- With `NuGetAudit=false` because the vulnerability feed returned `NU1900`, a Release build passed with zero warnings/errors. The executable standard runner passed **273 / 1 intentional skip / 0 failed** before the RC.3 version change, and guarded WPF smoke passed with live monitoring. A later Windows run yielded **280 passed / 1 skip / 1 failed** because the host WLAN query returned access-denied code 5 for its active Wi-Fi SSID. RC.3 version-adjusted regression remains pending.
- Native accessibility remains **NOT VERIFIED**: the desktop helper timed out before opening Setup, and no keyboard/Narrator/DPI/contrast observations were claimed (`docs/reviews/V1-CONS-ACCESSIBILITY-002.md`). Process-level zero-network observation and the live one-click update/restart drill remain pending.
- The first live RC.2 lab `Update now` action retrieved the signed loopback manifest, downloaded the test-signed RC.3 Setup, verified it, and reported installer launch, but the installed version stayed RC.2. Reproduction with the updater's exact trailing-separator `-Dir` argument returned Setup exit code 1; a trimmed path returned 0. The installer and updater now normalize that path, with a trailing-separator installer fixture. The failed one-click attempt is not counted as a completed update. A manual isolated Setup run preserved all 124 pre-upgrade interval records and the selected settings, then reached RC.3; this does not substitute for the required one-click drill.

### RC.2 consumer integration observations (12 October 2026)

- Clean-tree `scripts/release.ps1 -SignMode Test` completed without bypass flags from `b0e425f34ac3033ee36bf2da27ee9d339a9b3bed`. The canonical test-signed Setup.exe is 217,575,696 bytes, SHA-256 `5ac09394b27c2d6c9f7be2cf8b5b3961e059652d6874d0dc85c2d1a459c1eedf`. Its silent custom-directory install and uninstall returned exit code 0; installed ProductVersion embedded the source SHA. The signing root is untrusted and the artifact is not for public distribution.
- `scripts/build.ps1`: initial restore failed `NU1900` because the NuGet vulnerability feed was unreachable. With `NuGetAudit=false` for this offline local run, the full solution built with zero warnings/errors. This does not constitute a fresh package-vulnerability audit.
- `scripts/check.ps1`: **268 pass, 1 intentional skip, 0 fail** with normal user access after installer fixtures were isolated from real account data and shell entries.
- `scripts/check.ps1 -Windows`: **276 pass, 1 intentional skip, 0 fail** on Windows build 19045. This remains developer-host evidence only.
- `scripts/smoke.ps1`: native WPF smoke completed with all expected state/navigation assertions True and live monitoring. Artifact: `artifacts/smoke-20261012-065004`.
- Independent Security and Accessibility source reviews requested further packaged-flow and native accessibility evidence. RC.1→RC.2 update/restart, public Pages manifest, production signing, and Windows 11 field behavior remain unverified.
- Independent reviews were refreshed after the package drill: source findings are addressed, while both decisions remain CHANGES REQUESTED because consumer end-to-end and native accessibility evidence are incomplete. Gate E2 work was not started.

### Prior RC.1 gate evidence

- **Build:** Succeeded with 0 warnings, 0 errors across 7 projects (`Core`, `Windows`, `Storage`, `Application`, `App`, `Installer`, `Checks`) using .NET 10 SDK 10.0.401.
- **Fixtures:** 250 passed, 1 intentional skip, 0 failed via `scripts/check.ps1`; 258 passed, 1 skip, 0 failed via `scripts/check.ps1 -Windows`. Total evaluated: 259. Groups include:
  - 14 baseline arithmetic and boundary checks
  - 21 epoch, large counter, and delta discontinuity fixtures (including mandatory 125 MB / 10s = 12.5 MB/s and 100 Mbps)
  - 7 application collector lifecycle and resilience fixtures
  - 11 scope engine, transition, metric priority, and fail-closed aggregate fixtures
  - 6 taskbar companion geometry and fallback fixtures
  - 3 application attribution ProcessIdentity and NotMeasured capability fixtures
  - 12 independent audit regression fixtures in `AuditFixtures.cs`
  - 10 Windows audit regression fixtures in `WindowsAuditFixtures.cs`
  - 10 V1 Foundation Wave 1 fixtures in `V1FoundationFixtures.cs`
  - 10 V1 Foundation Gate A forensic audit fixtures in `V1FoundationFixtures.cs`
  - 28 V1 Wave 2 & Gate B audit fixtures in `V1Wave2Fixtures.cs`
  - 21 V1 Wave 3 & Gate C closure fixtures in `V1Wave3Fixtures.cs`
  - 14 V1 Core Truth closure fixtures in `V1CoreTruthFixtures.cs`
  - 14 adversarial clock and shutdown fixtures in `V1ClockShutdownFixtures.cs`
  - 11 connectivity and reachability fixtures in `ConnectivityFixtures.cs`
  - 11 alert engine fixtures in `AlertFixtures.cs`
  - 21 network tools, privacy, and V1-Must closure fixtures in `NetworkToolsFixtures.cs`
  - 7 configuration export/import fixtures in `ConfigExportImportFixtures.cs`
  - 16 Wave 5 release engineering, update trust, and installer truth fixtures in `V1Wave5ReleaseFixtures.cs` (W5-REL: canonical version identity, manifest parsing/validation, tampered manifest RSA signature rejection, untrusted key rejection against pinned trust anchor, single-byte tampered payload SHA-256 rejection, staging path traversal prevention, live-writer SQLite WAL snapshot creation and rollback recovery drill with 100% data fidelity, incompatible schema downgrade prevention, bounded operational log rotation with IPv4/MAC/user path redaction, diagnostic bundle sanitized export with live redaction preview, zero-external-network update privacy invariant, Authenticode publisher enforcement, whole-application 3.0s shutdown bound, installer graceful shutdown handshake without destructive force-kill, installer transactional staging and upgrade rollback, installer fresh install and uninstall purge)
  - 3 performance and stability benchmarks (preliminary microbenchmarks)
  - 5 live Windows IP Helper, RouteProvider, TaskbarCompanion, and native WLAN BSSID correlation checks
- **UI Smoke:** Real WPF shell, production navigation across all 5 primary views individually, Quick Panel, and floating widget smoke passed via `scripts/smoke.ps1` on Windows build 19045 (`.NET 10.0.12`) with all 18 native assertions True.
- **Release Verification Drill:** Standalone installer `SpeedMeterPro-1.0.0-rc.1-Setup.exe` tested via silent installation and uninstallation drills into sandbox directory with verified Authenticode signature integrity.

## Implemented verified paths

- **SpeedMeter.Core:**
  - Strict monotonic timing domain, 64-bit checked integer deltas, atomic RX/TX overflow handling, `CounterEpoch` and `MeasurementEpoch` boundaries, decimal rate and byte formatters (`MetricFormat`).
  - `HistoricalRange`: Readonly struct with canonical half-open `[StartUtcMs, EndUtcMs)` semantics, non-negative duration enforcement, overlap detection, and duration calculation.
  - `UsageIntervalRecord`: Pure immutable domain record for interval usage accounting, isolating domain and presentation from storage mechanics.
  - `CalendarRangeResolver`: Timezone-aware local boundary resolution for Today, Yesterday, This Week, This Month, and Billing Cycle.
  - `IntegerSplitter`: Cumulative monotonic mapping guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) with 0 remainder loss.
  - `QuotaDomain`: `QuotaRule`, `QuotaDirectionMode`, `QuotaPeriodType`, `QuotaEvaluation`, and `QuotaEvaluator.Evaluate`. Integer basis points (`int ThresholdBps`). PRD §11.2 defaults (`[8000, 9000, 10000]`).
  - `LiveGraphBuffer`: Pre-allocated circular ring buffer with bounded memory and peak-preserving downsampling.
  - `SettingsDomain`: `SpeedMeterSettings` schema version 1, value sanitization, monitor identity persistence, atomic write, corrupt file recovery, and strict future schema rejection.
  - `ConnectivityDomain`: Independent 3-axis status model (`CollectorState`, `ConnectivityState`, `QualityState`), `EndpointRegistry` with typed approved HTTPS targets, probe result contracts, hysteresis rules, captive portal detection, and metered network policy.
  - `AlertDomain`: `AlertRule`, `AlertIncident`, `AlertSeverity`, cooldown enforcement, quiet hours popup suppression, and persistent deduplication ledger contracts.
  - `NetworkDetailsContracts`: `NetworkInterfaceDetails`, `GatewayDetails`, `DnsServerDetails`, and truthful link speed labeling contracts.
- **SpeedMeter.Storage:**
  - Dedicated storage layer consuming `Microsoft.Data.Sqlite 9.0.2` and security-patched `SQLitePCLRaw.bundle_e_sqlite3 2.1.12`.
  - Single background writer over bounded `Channel`, WAL mode, `PRAGMA synchronous = FULL; foreign_keys = ON; busy_timeout = 5000;`.
  - Migration 2: Table `quota_threshold_events` with integer compound uniqueness `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`.
  - Migration 3: Tables `connectivity_incidents` and `alert_incidents` with compound unique constraints for restart-durable incident deduplication and timeline history.
  - Strict inequality half-open range queries, range-aware `GetTotalBytesAsync`, and daily rollup computation.
- **SpeedMeter.Application:**
  - `IUsageHistoryService` / `UsageHistoryService`: Period summaries, custom ranges, paged intervals, availability, `PruneHistoryAsync`, and `ClearAllHistoryAsync`.
  - `IQuotaService` / `QuotaService`: Cycle quota evaluation, threshold event recording, and persistent alert deduplication.
  - `ISettingsService` / `SettingsService`: Centralized settings state, atomic disk persistence, and `SettingsChanged` event notifications.
  - `IConnectivityService` / `ConnectivityService`: Passive Windows link monitoring, optional active HTTPS reachability checks with hysteresis, metered suppression, and captive portal detection.
  - `INetworkToolsService` / `NetworkToolsService`: Passive network details, ping benchmark runner, DNS resolution, and privacy-guarded manual public IP lookup.
  - `IAlertService` / `AlertService`: Rule evaluation, incident deduplication, cooldown tracking, and quiet hours suppression.
  - `ConfigExportImportService`: Versioned JSON configuration export/import with atomic writing, schema compatibility validation, and privacy invariant preservation.
  - `ApplicationContext`: Composition root wiring Collector, Storage, History, Quota, Settings, LiveGraph, Connectivity, Tools, Alerts, and Export services.
- **SpeedMeter.Windows & App:**
  - Design tokens (`Tokens.xaml`, `ThemeDark.xaml`, `ThemeLight.xaml`) with directional colors. Tabular numerals across all metrics.
  - Full theme engine (`ThemeManager`) supporting Dark, Light, and System (Windows registry `AppsUseLightTheme` with fallback) via pack URIs.
  - Complete MVVM presentation architecture: `MainViewModel`, `OverviewViewModel`, `LiveTrafficViewModel`, `HistoryViewModel`, `QuotaViewModel`, `SettingsViewModel`, `QuickPanelViewModel`, `NetworkToolsViewModel`, `AppsViewModel`.
  - Floating widget with Minimal, Compact Graph (live sparkline), and Detailed layouts, idle dimming, fullscreen auto-hide, and tray click-through recovery.
  - Windows Startup helper (`StartupHelper`) via HKCU Run registry key.
  - Global hotkeys (`HotkeyHelper`) for Dashboard, Widget toggle, and Click-through recovery.
  - Transparent first-run privacy onboarding (`FirstRunWindow`).
  - Modern navigation shell with 5 primary destinations + Settings, dynamic storage and connectivity banners, and live settings synchronization.

## Validated on real Windows

- **Host Environment:** Microsoft Windows 10 Pro x64 (build 10.0.19045.0), 4 logical processors, .NET 10.0.12 runtime. Developer evidence only; not a release port authorization.
- **Observed:**
  - 233 passed checks, 1 intentional skip, 0 failed checks via `check.ps1`; 241 passed checks, 1 skip, 0 failed via `check.ps1 -Windows`. Exit code: 0.
  - Post-change UI smoke test passed with all 18 native assertions verified (`True`). Exit code: 0. Window geometry: 1160x780. Artifact: `artifacts/smoke-20261012-023350`.

## Clock and shutdown truth limits

- Production `MeasurementClock` uses .NET `Stopwatch`/Windows QPC for elapsed time, UTC for history placement, and Windows unbiased interrupt time to identify a missed suspend notification.
- Stable lifecycle gaps persist observed UTC endpoints and monotonic duration. A wall-clock jump produces a zero-width gap event plus a new clock segment. Affected calendar summaries report `ClockUncertain` and the UI suppresses the coverage percentage. Distinct valid readings can have repeated UTC labels across segments; exact bytes are preserved without falsely claiming globally disjoint UTC labels.
- Normal quit keeps a structured `CollectorShutdownResult`, traces failures, and does not assert clean durability without successful flush barriers and healthy storage.
- Controlled hardware sleep/hibernate, timezone-only reporting changes, system clock changes on a live Windows host, Windows 11, and full AC-M05/M08/S05 acceptance remain designated as `RELEASE-GATE PENDING` for Wave 5 (Release Engineering). Gate D is passed.

