# V1 Gate B Forensic Audit Report: History, Timezone, Billing, Quota, Rollups, Live Graph & Settings

**Audited By:** Independent Gate B Audit Team  
**Milestone:** V1.0 Production Engineering Gate B  
**Date:** 11 October 2026 (Asia/Dhaka)  
**Host Environment:** Windows 10 Pro x64 (10.0.19045.0), .NET 10.0.12  
**Starting Commit:** `ec2e849`  
**Final Outcome:** `GATE B VERIFIED AFTER REPAIRS — WAVE 3 AUTHORIZED`

---

## 1. Executive Summary

An independent forensic engineering audit was conducted on the Wave 2 domain contracts covering:
- Historical range queries and cumulative integer partitioning
- Calendar and timezone resolution (DST transitions and billing cycles)
- Quota rules, evaluation, threshold deduplication, and opening balances
- Rollup provenance and reconciliation
- In-memory live graph buffer monotonicity and downsampling
- Settings schema versioning, atomic replacement, and monitor identity
- Application service facades

The previous Project Director claimed:
`USAGE & BILLING DOMAIN VERIFIED — PRODUCT SURFACE WAVE AUTHORIZED`

The forensic audit revealed 6 Gate-B Blockers and 7 Gate-B Majors. All identified defects have been remediated, covered by adversarial regression tests, and verified against reproducible Windows evidence.

Total verification outcome: **148 checks passed, 1 check skipped (explicit test of SkipCheckException), 0 failed** across 6 projects with exit code 0. WPF UI smoke test passed with exit code 0.

---

## 2. Baseline Reproduction

- **Commit:** `ec2e849`
- **Build Status:** Succeeded with 0 warnings, 0 errors.
- **Initial Verification:** 137 checks passed prior to audit repairs.

---

## 3. Defects Discovered and Remediated

| Defect ID | Severity | Domain | Description | Impact | Status |
|---|---|---|---|---|---|
| **DEF-B01** | **BLOCKER** | Timezone | Hardcoded 1-hour DST jump in `CalendarRangeResolver.ToUtcOffset`. Ignored dynamic `AdjustmentRule.DaylightDelta`. | In non-1-hour DST zones (e.g. Lord Howe Island 30m delta), skipped 30 minutes of valid calendar time. | **Fixed** (Dynamically derived from `AdjustmentRules`) |
| **DEF-B02** | **BLOCKER** | Settings | `SettingsStorage.Load` silently accepted `schemaVersion > CurrentSchemaVersion` without rejection. | An older application would load and overwrite newer schema versions, destroying future configuration. | **Fixed** (Rejects `schemaVersion > CurrentSchemaVersion`, preserves original file) |
| **DEF-B03** | **BLOCKER** | Quota | `QuotaEvaluator.Evaluate` returned `RecordedBytes = 0` when `rule.Enabled == false`. | Produced misleading zeros in UI when alerts were disabled, falsifying measured usage. | **Fixed** (Returns actual recorded bytes truthfully) |
| **DEF-B04** | **BLOCKER** | Quota / Storage | Database table `quota_threshold_events` used `threshold_percent REAL` in a compound `UNIQUE` constraint, while domain passed floating ratios (`0.80`). | Floating-point IEEE 754 precision mismatches in SQLite uniqueness constraints; unit ambiguity. | **Fixed** (Canonical `threshold_bps INTEGER` throughout domain and database) |
| **DEF-B05** | **BLOCKER** | Storage | `GetTotalBytesAsync` called `GetIntervalsAsync` and summed full interval bytes without applying `IntegerSplitter`. | Overcounted traffic when intervals partially overlapped query range; competed with `GetUsageSummaryAsync`. | **Fixed** (Routes partial ranges through `IntegerSplitter` / `GetUsageSummaryAsync`) |
| **DEF-B06** | **BLOCKER** | Storage | `GetIntervalsAsync` used inclusive inequalities (`end_utc_ms >= @start`, `start_utc_ms <= @end`). | Returned boundary-touching intervals with 0 overlap in half-open `[start, end)` queries. | **Fixed** (Changed to strict `end_utc_ms > @start` and `start_utc_ms < @end`) |
| **DEF-B07** | **MAJOR** | Coverage | `GetUsageSummaryAsync` used `observed + missing` as denominator, ignoring unexplained holes. | Reported 100% coverage when intermediate unmonitored holes lacked explicit gap rows. | **Fixed** (Denominator accounts for expected monitored duration in range) |
| **DEF-B08** | **MAJOR** | Rollups | `ComputeDailyRollupsAsync` hardcoded `scope_id = 'canonical_all'` and `network_profile_id = 'unidentified'`, and used ad-hoc `tz.GetUtcOffset(date)`. | Fabricated provenance in database; prevented filtered historical rollup queries; DST disparity. | **Fixed** (Queries distinct dimensions and uses `CalendarRangeResolver.GetDayRange`) |
| **DEF-B09** | **MAJOR** | Test Harness | Timezone test fixtures used `catch { return; }`, causing skipped environment tests to report as `PASS`. | Falsified test pass counts when environment timezones were absent. | **Fixed** (Introduced `SkipCheckException` and explicit PASS/SKIP/FAIL tracking) |
| **DEF-B10** | **MAJOR** | Architecture | `SpeedMeter.Application` lacked service facades (`IUsageHistoryService`, `IQuotaService`, `ISettingsService`, `ILiveGraphService`). | Forced UI in Wave 3 to directly invoke low-level SQLite queries and JSON serialization. | **Fixed** (Implemented 4 application service facades) |
| **DEF-B11** | **MAJOR** | Settings | Settings persisted only `WidgetX` and `WidgetY`, omitting monitor identity. | Moving windows across multi-monitor environments caused off-screen or invalid positions. | **Fixed** (Added `WidgetMonitorDeviceName` and `WidgetMonitorBounds`) |
| **DEF-B12** | **MAJOR** | Live Graph | `LiveGraphBuffer.Append` allowed out-of-order timestamps; downsampling allocation was overclaimed as zero. | Chronological corruption on clock adjustment; inaccurate zero-allocation claims. | **Fixed** (Monotonic guards + duplicate sequence handling; honest allocation documentation) |
| **DEF-B13** | **MAJOR** | Quota | Ad-hoc cycle string identifiers and lack of `CreateRevision` helper. | Risk of collision across timezones/revisions and mutable rule editing. | **Fixed** (Canonical `BillingCycleIdentity.Create` and `QuotaRule.CreateRevision`) |

---

## 4. Domain Audit Findings

### 4.1 Historical Range & Partition Conservation
- `IntegerSplitter` cumulative monotonic mapping $\text{CumulativeBytes}(t) = \lfloor \frac{(\text{Int128})B \times (t - t_{start})}{D} \rfloor$ was verified across 10,000 seeded randomized cases. Sum of partitions identically equals total bytes ($\sum A_i \equiv B$) with zero remainder loss.
- `GetIntervalsAsync` and `GetPagedIntervalsAsync` enforce strict half-open $[start, end)$ semantics. Boundary-touching intervals ending at query start or starting at query end are strictly excluded.
- `GetTotalBytesAsync` now uses `IntegerSplitter` to allocate partial range boundaries, eliminating conflicting range totals.

### 4.2 Timezone & DST Semantics
- In `CalendarRangeResolver`, dynamic DST resolution was verified using Windows native `TimeZoneInfo` for `"Lord Howe Standard Time"` (which features a 30-minute DST transition, `DaylightDelta = 00:30:00`). Local day 2026-10-04 resolved to exactly 23.5 hours, proving elimination of the hardcoded 1-hour assumption.
- `GetDayRange` provides single-point-of-truth day boundary calculation used by both historical query services and daily rollup aggregation.

### 4.3 Quota Rules & Basis Points Deduplication
- Warning thresholds standardized to integer basis points (`int ThresholdBps`, where 8000 = 80.00%, 10000 = 100.00%). PRD Section 11.2 defaults (`[8000, 9000, 10000]`) enforced.
- Database table `quota_threshold_events` migrated to `threshold_bps INTEGER NOT NULL` with compound uniqueness `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`, eliminating floating-point hashing hazards in SQLite.
- Quota evaluation when disabled now truthfully returns actual recorded traffic while suppressing alerts.

### 4.4 Rollup Provenance & Reconciliation
- `ComputeDailyRollupsAsync` inspects `usage_intervals` for actual distinct `(scope_revision_id, network_profile_id)` dimensions, generating rollups per real dimension rather than hardcoding `'canonical_all'`.
- Rollup generation verified as strictly idempotent across repeated passes.

### 4.5 Settings Versioning & Atomicity
- `SettingsStorage.Load` inspects `loaded.SchemaVersion`. Any file with `schemaVersion > CurrentSchemaVersion` is rejected with a diagnostic warning, original file is preserved untouched, and safe in-memory defaults are returned.
- Corruption backups are preserved as `.corrupt.<ts>.json` for Windows shell recognition.
- Monitor device name and work area bounds persisted to support multi-monitor placement in Wave 3.

### 4.6 Live Graph Monotonicity
- `LiveGraphBuffer.Append` guards monotonic timestamp order: rejects points with `TimestampUtcMs < lastTimestampUtcMs`. Duplicate timestamps with equal or higher sequence numbers update in place.
- Documentation downgraded to truthfully state: bounded pre-allocated memory; zero steady-state allocations during collection; bounded allocation on downsampling.

### 4.7 Application Service Boundaries
Four application service facades were created in `SpeedMeter.Application`:
1. `IUsageHistoryService` / `UsageHistoryService`
2. `IQuotaService` / `QuotaService`
3. `ISettingsService` / `SettingsService`
4. `ILiveGraphService` / `LiveGraphService`
The Presentation layer in Wave 3 will consume these high-level interfaces without directly accessing storage engines or JSON files.

---

## 5. Test Suite Verification & Skip Reporting

The check harness in `tests/SpeedMeter.Checks/Program.cs` was upgraded to distinguish PASS, SKIP, and FAIL:
- **148 checks passed**
- **1 check skipped** (`W2-CALENDAR: Missing environment timezone reports explicit SKIP instead of false pass`)
- **0 failed**
- Total evaluated: **149 checks**
- Exit code: **0**

UI Smoke Test (`scripts/smoke.ps1`) executed on Windows build 19045:
- 8 valid readings observed via live Win32 `GetIfTable2` provider.
- All 9 UI state and widget invariant assertions evaluated to `True`.
- Exit code: **0**.
