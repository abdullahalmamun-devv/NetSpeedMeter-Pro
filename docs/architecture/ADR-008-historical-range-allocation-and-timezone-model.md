# ADR-008: Historical range allocation, integer splitting, and timezone model

## Status

Accepted — Formally approved by Project Director following V1 Wave 2 implementation and verification on 11 October 2026.

Implementation completed in `SpeedMeter.Core` and `SpeedMeter.Storage`:
- Package & Runtime: .NET 10 LTS native `Int128` arithmetic and `System.TimeZoneInfo`.
- Domain Range Contract: `SpeedMeter.Core.HistoricalRange` defines canonical half-open `[StartUtcMs, EndUtcMs)` query spans with positive duration guard, strict disjointness, and overlap calculation.
- Monotonic Integer Splitting: `SpeedMeter.Core.IntegerSplitter` provides cumulative monotonic allocation:
  $$\text{CumulativeBytes}(t) = \left\lfloor \frac{(\text{Int128})B \times (t - t_{\text{start}})}{D} \right\rfloor$$
  guaranteeing exact partition conservation ($\sum \text{allocated} \equiv \text{original}$) across arbitrary split boundaries with zero byte remainder loss.
- Timezone Interpretation: `SpeedMeter.Core.CalendarRangeResolver` resolves user-facing periods (Today, Yesterday, ThisWeek, ThisMonth, BillingCycle) in arbitrary local timezones without altering canonical UTC persistence in SQLite. Correctly handles DST transitions (23h spring forward, 25h fall back) and fixed-offset non-DST zones.
- Billing Cycle Clamping: Day-of-month billing cycles clamp to month length (e.g. day 31 clamps to Feb 28 or 29) dynamically for each cycle without permanently mutating the user's requested preference.
- Database Application Boundary: `SqliteStorageEngine.GetUsageSummaryAsync` computes range intersections and byte allocations entirely in domain code using `IntegerSplitter`, avoiding SQLite `SUM()` overflow and rounding risks.
- Verified by automated fixtures: `W2-SPLIT: IntegerSplitter partition conservation property across arbitrary boundaries`, `W2-SPLIT: IntegerSplitter exact remainder allocation and boundary matching`, `W2-SPLIT: IntegerSplitter Int128 arithmetic eliminates 64-bit multiplication overflow`, `W2-CALENDAR: CalendarRangeResolver Today Yesterday Week Month boundaries`, `W2-CALENDAR: Billing cycle day 31 clamps in February without mutating requested cycle day`, `W2-CALENDAR: Billing cycle February leap year (29) vs common year (28) clamping`, `W2-CALENDAR: DST spring forward (23h) and fall back (25h) local day boundary resolution`, `W2-CALENDAR: Fixed-offset non-DST timezone (Asia/Dhaka) sanity resolution`, `W2-HIST: No double counting across adjacent half-open ranges (A + B == C)`.

## Context

PRD Sections 14, 21.4, and 22 require accurate historical usage reporting across arbitrary reporting periods (Today, Yesterday, Weekly, Monthly, Billing Cycles, Custom). Canonical accounting intervals in SQLite are bounded by fixed time slices (e.g. 1-second to 30-second windows) that frequently span midnight or arbitrary query boundaries.

Naive integer division ($B \times \Delta t / D$) truncates remainder bytes on each split, leading to drift where the sum of slices does not equal the interval total. Furthermore, 64-bit integer multiplication of large byte values ($> 9 \times 10^{18}$) by millisecond offsets overflows standard integer primitives. Finally, changing reporting timezones or crossing DST boundaries can introduce 1-hour gaps or overlaps if local timestamps are used directly in persistence.

## Decision

1. **Canonical Half-Open Ranges**: All historical queries and intervals operate on canonical half-open `[StartUtcMs, EndUtcMs)` millisecond intervals. Adjacent ranges $[T_0, T_1)$ and $[T_1, T_2)$ are strictly disjoint and tile $[T_0, T_2)$ with zero overlap and zero gap.
2. **Cumulative Monotonic Allocation**: Any interval $[t_0, t_1)$ partially overlapping a query range $[A, B)$ has its allocated bytes determined by difference of cumulative evaluation:
   $$\text{Allocated}(R) = \text{CumulativeBytes}(t_{\text{end}}) - \text{CumulativeBytes}(t_{\text{start}})$$
   where $t_{\text{start}} = \max(t_0, A)$ and $t_{\text{end}} = \min(t_1, B)$. This guarantees exact partition conservation: for any set of adjacent non-overlapping query windows partitioning the interval, the sum of allocated bytes identically equals the total interval bytes.
3. **Native Int128 Math**: All split calculations utilize .NET 10 `Int128` primitives, avoiding 64-bit multiplication overflow and eliminating dynamic heap allocations.
4. **UTC Persistence, Local Presentation**: SQLite stores only UTC milliseconds (`epoch_utc_ms`). Timezone conversion occurs exclusively at the query boundary via `CalendarRangeResolver`.
5. **Billing Cycle Clamping Invariant**: A requested cycle day (1..31) is never permanently mutated. In shorter months (February, April, etc.), the cycle end date clamps to the actual last day of the month; subsequent longer months automatically restore the full cycle length up to day 31.

## Alternatives Considered

- Storing local timestamps in SQLite: Rejected because reporting timezone changes and DST transitions would invalidate persisted indices and lead to ambiguous or duplicate hour records.
- SQL `SUM()` aggregation with proportional splitting in SQL: Rejected due to floating-point truncation, lack of exact integer partition conservation, and SQL integer overflow vulnerabilities.
- Permanently updating billing cycle day to 28 when February is encountered: Rejected because users who bill on the 31st expect March billing to run through March 31st.

## Consequences

- Historical queries require loading intervals intersecting the query range $[StartUtcMs, EndUtcMs)$ and executing `IntegerSplitter` in memory.
- For multi-year historical queries, daily rollups pre-calculated via `ComputeDailyRollupsAsync` serve as pre-aggregated units, reconciled against raw intervals.
- The reporting layer guarantees zero lost bytes: $\text{Usage}(Today) + \text{Usage}(Yesterday)$ exactly reconciles with a 48-hour custom range query spanning both days.
