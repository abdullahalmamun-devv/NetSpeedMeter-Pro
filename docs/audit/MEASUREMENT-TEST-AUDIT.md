# Independent measurement and test audit

Auditor: B/G (Product agent reassigned by Director). Date: 11 October 2026, Asia/Dhaka. Phase: read-only forensic inspection; only this report authored. Prior product documents and gate claims were not treated as evidence. No production/test repairs were made.

## Baseline and actual execution

Inspected the entire measurement engine, all seven fixture/runner files, collector and provider contracts, and build/check/common/smoke scripts. Line references below are the inspected baseline, before repairs.

| Baseline file | SHA-256 |
|---|---|
| `src/SpeedMeter.Core/Measurement.cs` | `C68B1A60ED6AD696441F21DC46F6C1FA16FEBDBEFB2A5EECEB7967D85EC91226` |
| `tests/SpeedMeter.Checks/Program.cs` | `B90F81C3B1FD0121DB5CADAAA4F08CDF997CC83D0FA8FFA51EA9293F339BA46F` |
| `src/SpeedMeter.Application/Collector.cs` | `4D826AD299A3D7AA1FC24416335BBBECA7039F890436B4568A3992CB94813382` |

Command actually run: `& .\scripts\check.ps1 -Windows` from the repository root. It restored successfully and exited **0 with 69 PASS records**. During concurrent build activity it emitted MSB3026 retry warnings for locked Core/Application DLLs; this run is not evidence of a clean zero-warning build. No second build was started by this auditor. Tool execution session: 73628; final output chunk: b5fa5a. Director is separately retaining baseline logs under `artifacts/audit-20261011/`.

Observed output: 35 adapters compared; typed snapshot contained 35 adapters; route provider returned four candidates, one with a default route; taskbar query was unavailable and only fallback was exercised. Provider mean 1.401 ms/50 calls; collector mean 1.345 ms/50 calls; process working set 27 MB; reported heap delta 1790 KB/100 immediate calls. These figures describe this executable invocation, not the desktop product's CPU/private-memory/soak budgets.

Independent decimal arithmetic, outside product code: `125000000 / 10 = 12500000 B/s`; `/ 1000000 = 12.5 MB/s`; `* 8 / 1000000 = 100 Mbps`. A first PowerShell calculation command failed parsing because `DecimalMBps` and `DecimalMbps` are case-insensitively duplicate keys; rerun with distinct names exited 0 and produced these values. The product checks independently assert `12.50 MB/s` and `100.00 Mbps` (`MeasurementFixtures.cs:24–32`).

## What the count means

| File / registration | Registered checks | Actual type / limits |
|---|---:|---|
| Program original checks, lines 17–104 | 14 | Deterministic domain assertions; overlaps expanded fixtures |
| MeasurementFixtures | 21 | Deterministic arithmetic/identity/format fixtures, with multiple parameter loops inside some checks |
| QaFixtures | 7 | Synchronous collector fixtures using fake clock/provider; no `Start()` worker exercised |
| ScopeFixtures | 11 | Synthetic scope policy inputs; one explicit engine scope switch |
| TaskbarFixtures | 6 | Pure coordinate examples; no window, DPI or Explorer interaction |
| AppAttributionFixtures | 3 | Two record/comparison examples and one current-process token check; no network event collection |
| Program `--windows` block, lines 114–173 | 4 | Native counter bracket, snapshot availability, route inventory, taskbar query/fallback |
| PerfFixtures, behind `--windows` | 3 | Short synchronous microbenchmarks, no product resource/soak gate |
| Total | 69 | 62 without `--windows`; not 69 release acceptance cases |

The 62 default checks are not all platform-independent unit tests: attribution capability inspects the host token. Counted assertion call sites: Program 25, Measurement 43, QA 31, Scope 32, Taskbar 11, Attribution 7, Perf 5 = 154 textual sites. This is not an assertion execution count: loops repeat sites and branches omit others. No explicit skip reporting exists.

`Program.cs:7–10` catches each thrown exception, writes FAIL and sets `Environment.ExitCode=1`; later successful checks do not reset it. `scripts/check.ps1:6–7` propagates nonzero through a terminating throw. Restore/build wrappers also check exit status. Source inspection supports this mechanism, but controlled forced-failure execution remains pending authorization; successful runs alone do not prove the failure path. Unknown arguments are ignored, so misspelling `--windows` silently omits seven checks. `dotnet test` is not configured with a test adapter; it cannot replace this executable runner.

## Verified strengths and precise gaps

| Invariant | Inspected assertion coverage | Remaining boundary |
|---|---|---|
| Baseline before rate | Original baseline check; exact sample starts from nonzero counters | First baseline upload null is not individually asserted in original check, but engine makes both null |
| Exact bytes / rate conversion | Original 125 MB example and expanded simultaneous RX/TX example | No independent 60-second Windows experiment |
| Actual duration | Delayed 9 s tick, irregular .25/1.75 s intervals, 3 s coarse boundary | Real provider latency and timer suspension not tested |
| Idle vs missing | Expanded idle asserts live zero + coverage; unavailable/pause checks null rates | Real UI accessibility/error semantics separate |
| Reset arithmetic | Receive reset original; transmit reset expanded rejects both directions | Provider cannot identify every lifetime transition |
| Integer precision | Near-`ulong.MaxValue` subtract-before-convert fixture | Unit presentation is intentionally floating-point/rounded |
| Atomic overflow | RX overflow original and TX overflow expanded; duration unchanged in expanded fixture | Overflow recovery sequence after rejected interval not comprehensively asserted |
| Finite input/output | NaN/infinite monotonic inputs; invalid expected intervals; subtraction/rate/coverage overflow; formatter invalids | Public `Interrupt` accepts arbitrary ReadingState; see defensive contract note below |
| Epochs | Source lifetime changes, unknown-to-known lifetime, reset/pause baseline generation | Real AdapterProvider supplies default SourceEpoch, so injected-epoch tests do not prove OS incarnation detection |
| Scope identity | Alternate source rejected; explicit scope reset; mutable name retained | Scope policy revisions are not passed to engine; same-adapter membership revision is not tested |
| Gaps | Explicit Interrupt excludes sleep/pause fixture bytes and measured duration | No durable gap interval, reason enum or history ledger; fake interruption does not prove real suspend delivery |
| Wall clock | Backward and forward UTC shifts leave rates/bytes unchanged | No clock-change event/reporting mapping implemented |
| Immutable readings | Earlier reading values retained; unique snapshot/session IDs | AdapterObservation also defensively copies array and exposes read-only wrapper; mutation rejection not fixture-tested |
| Collector pause/reset | Correct counters and rebaseline in seven synchronous fixtures | Suspend+pause ordering, repeated Start/Dispose, race and failure isolation absent |

Core `Sample` guards finite elapsed/rates/coverage before mutation; computes both checked integer totals before assigning either; and updates baseline on reset/overflow. No arithmetic corruption was found for the existing represented fixtures. This is a bounded finding, not acceptance of all measurement or lifecycle requirements.

## Findings requiring changes

### MT-01 — P1: subscriber failure silently terminates collection

`Collector.cs:157` invokes `Updated` synchronously; `RunAsync:173–178` handles only expected cancellation/disposal. Any subscriber exception faults the worker. `Latest` may remain Running with a stale healthy reading; no terminal failure snapshot is published. The next `Dispose` rethrows the worker failure at line 196. This is a source-proven path, independently corroborating the Director's finding; not yet dynamically reproduced by this auditor. All current QA fixtures call SampleOnce without subscribers, so 69 PASS cannot detect it.

Proposed adversarial reproduction: fake finite clock/provider, attach subscriber that throws once on a Running sample, start worker, observe at least three scheduled intervals. Verify the provider's read count continues or a truthful terminal failure is exposed according to a documented policy, and disposal does not leak the observer exception. Use bounded waits and an outer child-process deadline so test failure cannot hang the suite.

### MT-02 — P1: callback-triggered disposal can deadlock its own worker

`SampleOnce:157` runs observers on the worker thread. A callback calling `Dispose` captures that worker at line 192 and synchronously waits for it at line 196, while that worker is still executing the callback. The same-thread wait cannot finish. `Dispose` also sends a Stopped notification before waiting, so an adversarial test must avoid recursively triggering the same handler. Current tests only dispose synchronous, never-started collectors via `using`.

Proposed reproduction: launch a short-lived child test process; handler only on first Running snapshot calls Dispose. Fake clock/provider ensure a valid interval. Parent requires a completion signal/process exit within a bounded deadline, then terminates only the child if it hangs. This catches self-wait without deadlocking the main verification executable. Repair needs a documented callback/disposal contract; suppressing the fixture would not resolve it.

### MT-03 — P1: attribution test validates an unsupported completeness claim

`AppAttributionSpike.cs:69–77` reports Complete solely from elevation, despite no ETW session/provider being started. `AppAttributionFixtures.cs:41–54` repeats that condition and asserts the misleading result. Elevation is not collection availability, attribution coverage or event-loss evidence. Also `AppAttributionFixtures:28–38` labels a check as application grouping but only verifies two records differ and a literal display name equals itself; no process-to-application mapping occurs.

Required correction: explicit not-implemented/unsupported capability until a real source returns evidence; privilege and completeness must be separate. Keep identity examples labelled as such. Actual TCP/UDP/IPv4/IPv6/QUIC/PID churn/loss/user-isolation experiments remain required and must not be replaced by these assertions.

### MT-04 — P2: scope fixtures encode assumptions as verified disjointness

`ScopeFixtures.cs:118–125` names two hardware adapters "purely disjoint" although the inputs supply no route/path overlap proof or explicit boundary-validation evidence. The assertions then demand aggregation be unambiguous. This fixture can protect a flawed implementation. `ScopeFixtures:127–146` claims scope-revision rebaseline but calls only `engine.SelectScope` with a different adapter; no revision is fed to the engine. The VPN fixture (`99–106`) asserts the tunnel is canonical, requiring reconciliation with the PRD's physical-egress preference and honest fallback requirements.

Proposed adversarial cases: two hardware interfaces without known independent boundaries; a same-adapter scope revision change; IPv4 and IPv6 defaults on different interfaces; VPN /1 routes without a /0 default; route/interface metric disagreement. Separate policy fixtures from real topology validation. Director's platform audit owns the policy repair decision.

### MT-05 — P2: several PASS labels materially exceed their assertions

- `Program.cs:146–154` only asserts candidate count > 0. Printed topology/primary is not validated, so wrong default selection passes.
- `Program.cs:157–171` mixes native taskbar evidence with hard-coded 1920x1040 work area. A valid query only requires nonnegative coordinates; it does not check bounds, overlap, scale or actual window placement. In this run the query was unavailable, yet the combined check reports PASS. Negative coordinates can also be valid for secondary monitors; nonnegative is not the general safety condition.
- `QaFixtures.cs:163–172` labels PermissionDenied/TemporarilyUnavailable coverage but only executes PermissionDenied. Add individual cases or correct the name.
- `PerfFixtures.cs:75–102` calls SampleOnce 100 times synchronously, never Start. It records heap growth without asserting a bound and only checks the entire test process working set <300 MB. Calling this continuous-loop stability/memory boundedness is unsupported.
- `PerfFixtures.cs:27–40` includes a forced GC in the latency stopwatch and calls a managed heap delta an allocation baseline. GC.GetTotalMemory is retained heap estimation, not total allocated bytes. Neither benchmark reports p95, CPU, desktop/widget overhead, five-minute warm-up or 30-minute measurement.

Rename checks to what they prove; add honest unavailable/skip outcomes for environment-dependent scenarios; create separately gated experiments for PRD budgets. A fallback unit test passing is not successful native taskbar compatibility.

## Defensive contract / missing coverage, not demonstrated production failures

- `MeasurementEngine.Interrupt(..., ReadingState.Live, ...)` is publicly accepted and produces IsValid=true with null rates (`Measurement.cs:57–60,25`). Current collector callers do not do this. Add a state guard or narrow API and a fixture before allowing more callers; classify as P2 API invariant weakness, not an observed current UI defect.
- Real provider SourceEpoch is default (`AdapterProvider.cs:37–39`). Same ID with a larger counter after an unobserved replacement cannot be distinguished from continuity by the engine; explicit limitation or provider lifecycle evidence is required.
- No checks for Settings persistence, storage, reports/quotas, external-request observation, Narrator/high-contrast, OS sleep/hibernate, Explorer restart, monitor removal, startup p95, 24-hour run or seven-day soak exist in this suite.
- Smoke wrapper checks seven result fields and process exit; its own results are produced inside the same application. It is useful interaction instrumentation, not independent visual/accessibility validation. No new smoke was run in this audit.

## Next authorized evidence steps

1. Director compares independent findings before repairs. Preserve baseline report/hash/logs.
2. Author bounded child-process tests for MT-01/02 and verify failure against baseline, then pass against reviewed repair.
3. Verify the runner itself by controlled Require(false) in an isolated temporary harness copy, confirming both direct executable and check wrapper return nonzero, and restore/remove only that isolated copy. Do not alter production fixtures to make the main suite appear green. Capture failure output and exact exit codes.
4. Strengthen misleading capability/policy/performance assertions; report counts by evidence category with explicit environment unavailability.
5. Rerun a serialized clean build and tests on the repaired tree. Keep Windows11, actual ETW, topology, taskbar and resource release gates open until separate evidence exists.

## Authorized regression work — pending execution/review

After the Director compared independent findings, this auditor was authorized to add only `tests/SpeedMeter.Checks/AuditFixtures.cs` and update this report. Production repairs and runner wiring remain Director-owned. The file adds 16 runtime checks: four child-process callback scenarios, five typed provider states, and seven lifecycle/numeric/immutability cases. Child processes have a 12-second outer deadline and are terminated if unresponsive; the main runner cannot be trapped by the old self-disposal deadlock.

- Throwing subscriber: actual Start worker must keep accounting and invoke an independent healthy subscriber.
- Slow subscriber: actual Start worker must perform two additional reads while delivery remains blocked.
- Coalescing: while one observer is blocked, 500 further samples must preserve all bytes and retain only the newest pending notification; release yields exactly two observed Running notifications.
- Callback disposal: Dispose must return, publish stopped state and prevent subsequent provider reads.
- Partial/Unsupported/PermissionDenied/TemporarilyUnavailable/Failed: preserve provider status, null rates, prior totals and a fresh recovery baseline.
- Empty inventory, overlapping pause/suspend, invalid monotonic sample, invalid interval configuration, invalid Live interruption, defensive inventory copying and retained canonical snapshot identity receive independent assertions.

No build was run by this auditor during repair to avoid concurrent output-directory locks. Director must execute these checks against an isolated baseline and the repaired tree. Test existence is not a passing result.

Reviewed Director baseline probe source and outputs in `artifacts/audit-20261011/probe/Program.cs`, `callback-fault-before.log` and `self-stop-before.log`: the throwing-callback probe observed one provider read after 1.6 seconds and disposal propagated InvalidOperationException; self-stop printed `Callback returned: False`. `callback-before.json` says the self-stop **process** completed; that does not mean its callback returned. These independently inspected outputs support MT-01/02 beyond static analysis.
