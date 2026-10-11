# ADR-009: Quota rules, threshold deduplication, and opening balances

## Status

Accepted — Formally approved by Project Director following V1 Wave 2 implementation and verification on 11 October 2026.

Implementation completed in `SpeedMeter.Core` and `SpeedMeter.Storage`:
- Domain Quota Model: `SpeedMeter.Core.QuotaDomain` implements `QuotaRule`, `QuotaDirectionMode` (Total, DownloadOnly, UploadOnly), `QuotaPeriodType` (Daily, Weekly, Monthly, BillingCycle), and `QuotaEvaluation`.
- Safe Opening Balances: User-specified initial cycle balance (`OpeningBalanceBytes`) is incorporated into `EffectiveUsedBytes = RecordedBytes + OpeningBalanceBytes` for quota evaluation, but is strictly isolated from `UsageSummary.ObservedDurationMs` and canonical interval bytes so it never falsifies observation history or coverage ratios.
- Threshold Evaluation & Jumping: `QuotaEvaluator.Evaluate` checks monotonic warning thresholds (e.g. 50%, 75%, 90%, 100%). When sudden large traffic spikes jump over multiple thresholds in a single sampling cycle, all crossed thresholds are emitted in ascending order.
- Persistent Deduplication: Migration 2 introduces table `quota_threshold_events` with compound uniqueness constraint:
  `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_percent)`
  ensuring that a given quota warning threshold fires at most once per billing cycle, even across application crashes, reboots, or engine restarts.
- Uncapped Overage Tracking: When usage exceeds 100%, `RemainingBytes` clamps to 0 while `OverageBytes = EffectiveUsedBytes - LimitBytes` and `PercentUsed` accurately report overage without integer wraparound or negative values.
- Verified by automated fixtures: `W2-QUOTA: QuotaEvaluator calculates remaining bytes and overage beyond 100% accurately`, `W2-QUOTA: Opening balance improves effective quota calculation without inflating coverage`, `W2-QUOTA: Warning threshold crossing emits in deterministic order and handles threshold jumping`, `W2-QUOTA: Quota threshold deduplication persists in SQLite and survives storage restart`.

## Context

PRD Sections 14.3 and 17 specify data quota tracking, threshold alerts (e.g. 50%, 75%, 90%, 100%), and cycle resets.
In client applications, alert deduplication is notoriously brittle: if stored purely in memory, restarting the app or rebooting Windows causes annoying duplicate alert popups. Conversely, if opening balances are added directly into database intervals, data usage history becomes permanently falsified with synthetic bytes. Furthermore, network bursts can bypass intermediate thresholds if evaluations only check equality or single threshold steps.

## Decision

1. **Persistent Deduplication Ledger**: Table `quota_threshold_events` in SQLite stores triggered events with `UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_percent)`. The storage engine checks recorded events via `GetRecordedThresholdsAsync` and writes new events atomically via `RecordQuotaThresholdEventAsync`.
2. **Cycle Identifier Isolation**: Each cycle is uniquely identified by `[CycleStartUtcMs]_[CycleEndUtcMs]`. At cycle reset, the identifier advances, resetting deduplication state cleanly without requiring table purges or manual counter resets.
3. **Threshold Jumping Monotonicity**: If usage jumps from 40% to 95%, `QuotaEvaluator` identifies all unrecorded thresholds $\le 95\%$ and emits them in deterministic ascending order (`[50, 75, 90]`).
4. **Opening Balance Isolation**: The opening balance is stored in configuration and evaluated in domain logic; it is never written to `usage_intervals` or `daily_rollups`.
5. **Exact Integer Math**: All percentage and threshold math uses `Int128` multiplication to avoid 64-bit integer overflow when calculating $\text{LimitBytes} \times \text{ThresholdPercent}$.

## Alternatives Considered

- Memory-only threshold tracking: Rejected because restarting SpeedMeter Pro would re-fire all previously triggered notifications.
- Synthetic "opening balance" interval injected into SQLite: Rejected because it corrupts real traffic audit trails, adapter counters, and coverage duration metrics.
- Modifying quota rules to only track single highest threshold: Rejected because user-facing PRD requirements demand distinct notifications at each configured threshold.

## Consequences

- The quota evaluation pipeline is fully idempotent and stateless with respect to volatile memory.
- Notification dispatchers can reliably invoke OS toast alerts knowing duplicate firings within the same cycle are structurally impossible.
