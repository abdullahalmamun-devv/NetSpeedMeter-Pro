# ADR-009: Quota rules, basis points deduplication, and opening balances

## Status

Accepted — Formally audited, repaired, and approved by Independent Gate B Audit Team on 11 October 2026.

Implementation verified in `SpeedMeter.Core`, `SpeedMeter.Storage`, and `SpeedMeter.Application`:
- Domain Quota Model: `SpeedMeter.Core.QuotaDomain` implements `QuotaRule`, `QuotaDirectionMode`, `QuotaPeriodType`, and `QuotaEvaluation`.
- Basis Points Representation: Thresholds standardized to integer basis points (`int ThresholdBps`, where 8000 = 80.00%, 10000 = 100.00%), eliminating floating-point hashing and precision ambiguity in memory and database constraints.
- PRD Defaults: `QuotaRule.DefaultThresholdsBps` enforces PRD §11.2 defaults: 80%, 90%, 100% (8000, 9000, 10000 bps).
- Truthful Disabled Accounting: Disabling quota alerts suppresses event notifications but truthfully preserves actual recorded usage (`RecordedBytes`), preventing misleading zeros in presentation layers.
- Safe Opening Balances: User-specified initial cycle balance (`OpeningBalanceBytes`) is incorporated into `EffectiveUsedBytes = RecordedBytes + OpeningBalanceBytes` for quota evaluation, but is strictly isolated from `UsageSummary.ObservedDurationMs` and canonical interval bytes so it never falsifies observation history or coverage ratios.
- Persistent Deduplication: Migration 2 introduces table `quota_threshold_events` with integer compound uniqueness:
  `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)`
  ensuring that a given quota warning threshold fires at most once per billing cycle, even across application crashes, reboots, or engine restarts.
- Canonical Cycle Identity: `BillingCycleIdentity.Create` generates deterministic strings based on rule ID, revision, start UTC, end UTC, and timezone ID.
- Application Facade: `SpeedMeter.Application.IQuotaService` orchestrates cycle evaluation, threshold deduplication, and persistence.

## Context

PRD Sections 14.3 and 17 specify data quota tracking, threshold alerts (80%, 90%, 100%), and cycle resets.
Floating-point threshold keys in SQLite uniqueness constraints are prone to platform-dependent IEEE 754 precision mismatches. Disabling quotas previously resulted in misleading zeros for recorded usage. Furthermore, client applications frequently suffer duplicate alert popups upon restart if deduplication state is volatile.

## Decision

1. **Exact Basis Points Contract**: Thresholds are represented as integers in basis points ($1\% = 100\text{ bps}$). The SQLite column is `threshold_bps INTEGER NOT NULL`.
2. **Persistent Deduplication Ledger**: Table `quota_threshold_events` in SQLite stores triggered events with compound uniqueness `(rule_id, rule_revision, cycle_identifier, threshold_bps)`.
3. **Truthful Disabled Semantics**: Disabling quota evaluation never clears or zeroes out recorded bytes.
4. **Isolated Opening Balances**: Opening balance is incorporated into effective usage for quota calculation, but never written to `usage_intervals` or `daily_rollups`.
5. **Exact Integer Math**: All percentage and threshold math uses `Int128` multiplication to avoid 64-bit integer overflow.
