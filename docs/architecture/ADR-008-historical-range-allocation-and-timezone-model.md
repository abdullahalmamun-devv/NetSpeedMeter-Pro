# ADR-008: Historical range allocation, integer splitting, and timezone model

## Status

Accepted — Formally audited, repaired, and approved by Independent Gate B Audit Team on 11 October 2026.

Implementation verified in `SpeedMeter.Core`, `SpeedMeter.Storage`, and `SpeedMeter.Application`:
- Package & Runtime: .NET 10 LTS native `Int128` arithmetic and `System.TimeZoneInfo`.
- Domain Range Contract: `SpeedMeter.Core.HistoricalRange` defines canonical half-open `[StartUtcMs, EndUtcMs)` query spans with positive duration guard, strict disjointness, and overlap calculation.
- Monotonic Integer Splitting: `SpeedMeter.Core.IntegerSplitter` provides cumulative monotonic allocation:
  $$\text{CumulativeBytes}(t) = \left\lfloor \frac{(\text{Int128})B \times (t - t_{\text{start}})}{D} \right\rfloor$$
  guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) across arbitrary split boundaries with zero byte remainder loss. Verified across 10,000 seeded randomized cases.
- Timezone Interpretation: `SpeedMeter.Core.CalendarRangeResolver` resolves user-facing periods (Today, Yesterday, ThisWeek, ThisMonth, BillingCycle) in arbitrary local timezones without altering canonical UTC persistence in SQLite. Correctly derives dynamic daylight transition rules from `TimeZoneInfo.GetAdjustmentRules()` (verified on Lord Howe Island 30-minute DST shift, eliminating hardcoded 1-hour assumptions) and handles ambiguous fall-back intervals and fixed-offset non-DST zones.
- Single Source of Truth for Days: `CalendarRangeResolver.GetDayRange(date, tz)` provides single-point-of-truth day boundary calculation used by both historical queries and daily rollup generation.
- Strict Inequality Boundaries: `SqliteStorageEngine.GetIntervalsAsync` enforces strict half-open boundary inequalities (`end_utc_ms > @start AND start_utc_ms < @end`), excluding boundary-touching rows with 0 overlap.
- Truthful Range Totals: `SqliteStorageEngine.GetTotalBytesAsync` allocates proportional bytes for partial ranges using `IntegerSplitter` or routes directly through `GetUsageSummaryAsync`.
- Application Facade: `SpeedMeter.Application.IUsageHistoryService` exposes high-level period query contracts, shielding presentation components from low-level SQLite mechanics.

## Context

PRD Sections 14, 21.4, and 22 require accurate historical usage reporting across arbitrary reporting periods (Today, Yesterday, Weekly, Monthly, Billing Cycles, Custom). Canonical accounting intervals in SQLite are bounded by fixed time slices that frequently span midnight or arbitrary query boundaries.

Naive integer division truncates remainder bytes on each split, leading to drift. Standard 64-bit integer multiplication of large byte values overflows standard integer primitives. Furthermore, daylight-saving transitions vary by geography (e.g. 30 minutes, 1 hour), and hardcoding a 1-hour jump skews day boundaries.

## Decision

1. **Canonical Half-Open Ranges**: All historical queries and intervals operate on canonical half-open `[StartUtcMs, EndUtcMs)` millisecond intervals. Adjacent ranges $[T_0, T_1)$ and $[T_1, T_2)$ are strictly disjoint and tile $[T_0, T_2)$ with zero overlap and zero gap.
2. **Cumulative Monotonic Allocation**: Any interval $[t_0, t_1)$ partially overlapping a query range $[A, B)$ has its allocated bytes determined by difference of cumulative evaluation:
   $$\text{Allocated}(R) = \text{CumulativeBytes}(t_{\text{end}}) - \text{CumulativeBytes}(t_{\text{start}})$$
   guaranteeing exact partition conservation across all split boundaries.
3. **Dynamic Timezone Adjustment**: Dynamic DST adjustment deltas are derived directly from active `TimeZoneInfo.AdjustmentRule` entries, correctly handling non-1-hour transitions.
4. **Strict Boundary Querying**: Storage queries use strict inequalities (`end_utc_ms > @start AND start_utc_ms < @end`).
5. **Billing Cycle Clamping Invariant**: A requested cycle day (1..31) is never permanently mutated. In shorter months, cycle end clamps dynamically to month length; subsequent months restore requested length up to day 31.
