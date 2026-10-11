# Acceptance traceability — 98 original cases

Audit date: 11 October 2026 (Asia/Dhaka). Source: PRD revision 2.0 section 27. Owner: Product / PRD Analyst; reviewer: Project Director. Each original ID appears once. Release splits below are analysis, not PRD changes. Execute separate sub-scenarios under the original ID where a case crosses releases. P0 feasibility evidence supports eventual acceptance but never substitutes for a release gate.

All cases remain open for release acceptance. Evidence notes distinguish prior fixture/smoke evidence, partial implementation and no recorded experiment. T and V refer to the test runner and validation report described in [FEATURE-MAP.md](FEATURE-MAP.md). No tests were rerun for this documentation-only task.

Every eventual execution record needs setup, expected/actual results, build/commit, OS/configuration, evidence path, reviewer and outstanding failures. Carry forward each earlier-release case as regression coverage. Read-only source review cannot prove a Windows or privacy test passed.

| ID | Features | Release / qualification | Original condition | Original pass criterion | Current evidence / remaining work |
|---|---|---|---|---|---|
| AC-M01 | F01, F07 | V1.0 | Replay known byte counters and elapsed intervals | Integer totals exact; rates match Section 7 conversion | Partial evidence: baseline/exact integer/conversion fixtures in T; full release verification open |
| AC-M02 | F01, F06, F07 | V1.0 | Delay timer callbacks | Rate uses actual elapsed time; coarse timing flagged without losing known bytes | Partial evidence: delayed-tick fixture in T; end-to-end coarse history/peak handling open |
| AC-M03 | F01, F11, F47 | V1.0 | Reset/recreate interface counter | No negative or giant rate; new baseline and gap recorded | Partial evidence: reset fixture in T; persistent epoch/gap and interface recreation open |
| AC-M04 | F01, F02, F07 | V1.0 | Switch bits/bytes, units, precision and smoothing | Underlying byte totals unchanged; correct displayed conversion | Partial evidence: side-effect fixture and prior smoke in V; fixed units/precision/smoothing absent |
| AC-M05 | F01, F07, F15, F47 | V1.0 | Pause, sleep and resume across traffic | No invented usage/rates for gap; no sleep-derived outage | Partial evidence: interruption fixture and prior pause smoke; real sleep/hibernate and durable gaps untested |
| AC-M06 | F01, F11 | V1.0 | Compare same-boundary Windows reference counters for ≥60 s | ≤1% proposed total discrepancy; boundaries/method recorded | Not executed: prior native/managed counter bracket is not independent 60-second accuracy evidence |
| AC-M07 | F01, F03, F04, F06 | V1.0 | Open all surfaces during transfer | Same snapshot gives consistent values within one sample interval | Partial evidence: prior dashboard value match; quick panel/docked surface absent, full skew experiment pending |
| AC-M08 | F01, F08, F09, F47 | V1.0 | Change system clock and timezone | No duplicate bytes/negative duration; explicit clock/reporting event | Partial evidence: wall-clock reversal fixture; reporting event/timezone mapping absent |
| AC-A01 | F11, F12 | V1.0 | Wi-Fi ↔ Ethernet with active traffic | Auto view changes correctly; interval accounting preserved | Open — no scenario-level acceptance evidence recorded |
| AC-A02 | F11 | V1.0 | Selected manual adapter disappears | Unavailable shown; no silent scope replacement | Partial evidence: wrong/missing selected source fixture; real adapter removal UI test pending |
| AC-A03 | F13, F14 | V1.0 | VPN tunnel plus physical egress carry same flow | Canonical usage counts once; tunnel shown separately | Open — no scenario-level acceptance evidence recorded |
| AC-A04 | F13 | V1.0 | Two independent interfaces plus virtual bridge | Validated aggregate correct; ambiguous topology labelled | Open — no scenario-level acceptance evidence recorded |
| AC-A05 | F10, F11 | V1.0 | Repeated SSID, adapter rename, index reuse | Distinct profiles/epochs preserved until explicit user grouping | Open — no scenario-level acceptance evidence recorded |
| AC-A06 | F12, F13 | V1.0 | IPv4/IPv6 use different routes | Primary-view limitation or verified aggregate is explicit | Open — no scenario-level acceptance evidence recorded |
| AC-W01 | F03, F42 | V1.0 | Meter at tested taskbar positions/scales | Readable; no blocked Windows controls | Open — no scenario-level acceptance evidence recorded |
| AC-W02 | F03, F46 | V1.0 | Auto-hide taskbar and Explorer restart | Safe recovery within target; no duplicate tray icons | Open — no scenario-level acceptance evidence recorded |
| AC-W03 | F05, F46 | V1.0 | Enable lock/click-through, then recover | Tray/hotkey remains usable; no trapped overlay | Open — no scenario-level acceptance evidence recorded |
| AC-W04 | F04, F05, F42 | V1.0 | Move between mixed-DPI monitors; unplug one | Widget remains visible, sharp and inside work area | Open — no scenario-level acceptance evidence recorded |
| AC-W05 | F41, F64 | V1.0 | Idle, fullscreen, presentation and hide modes | Expected surface behaviour; collection continues unless paused | Open — no scenario-level acceptance evidence recorded |
| AC-W06 | F44 | V1.0 | Repeated startup/second launch | One owner per user; existing UI focused rather than duplicate collectors | Partial implementation: mutex rejects duplicate process; existing UI is not focused |
| AC-W07 | F44, F46 | V1.0 | Close dashboard, pause, quit and relaunch | Each command has distinct documented collection behaviour | Partial evidence: prior pause/resume smoke; close/quit/relaunch lifecycle scenario not fully recorded |
| AC-W08 | F02, F37, F38, F39, F58, F64 | V1.0 | Keyboard, Narrator, high contrast, 200% text/scale | Essential values/actions understandable and reachable | Open — no scenario-level acceptance evidence recorded. V2 regression for Bengali/styles |
| AC-W09 | F45 | V1.0 if portable ships | Portable launch on writable/read-only locations | Supported functions work; permission failure clear; no silent service install | Open — no scenario-level acceptance evidence recorded |
| AC-G01 | F06 | V1.0 | Review every timeframe with known spikes/gaps | Spikes represented appropriately; gaps never joined as healthy traffic | Partial implementation: 60 s gap-aware graph; timeframe/extrema fixture suite absent |
| AC-G02 | F06, F43 | V1.0 | Resize/close graph repeatedly | Accounting unchanged; hidden rendering stops | Partial implementation: IsVisible gates invalidation; resize/hidden-rendering experiment absent |
| AC-G03 | F07, F08, F09 | V1.0 | Reset session after daily usage exists | Session changes only; quota/history unchanged | Partial evidence: session reset fixture/smoke; persistent daily/quota data absent |
| AC-G04 | F02, F07 | V1.0 | Change sampling interval and inspect peak/mean | Correct weighted mean; peak retains interval context | Open — no scenario-level acceptance evidence recorded |
| AC-U01 | F08, F10, F23, F47 | V1.0 | Sum minute/hour/day records in one scope | Exact unrounded totals reconcile without duplicate source domains | Open — no scenario-level acceptance evidence recorded. V1.5 app domain |
| AC-U02 | F08, F09, F47 | V1.0 | Sample spans midnight/cycle boundary | Deterministic byte allocation preserves interval total and timing flag | Open — no scenario-level acceptance evidence recorded |
| AC-U03 | F08, F09 | V1.0 | Billing days 28–31, leap year and DST zone | Correct half-open boundaries and saved timezone | Open — no scenario-level acceptance evidence recorded |
| AC-U04 | F09, F31, F34 | V1.0 | Restart around 80/90/100% crossing | One alert per threshold/cycle/revision | Open — no scenario-level acceptance evidence recorded |
| AC-U05 | F09, F13 | V1.0 | Change quota rule/scope or enter opening balance | Preview correct; measured and manual amounts distinguishable | Open — no scenario-level acceptance evidence recorded |
| AC-U06 | F08, F09, F47 | V1.0 | Delete history/compact retention | All affected views consistent; coverage loss and quota impact shown | Open — no scenario-level acceptance evidence recorded |
| AC-U07 | F36, F56 | V1.0 | Export CSV/JSON with Bengali/formula-like names | Correct encoding/escaping; integers and selected scope match UI | Open — no scenario-level acceptance evidence recorded. Bengali input required before translated UI |
| AC-U08 | F36 | V1.0 | Export fails/cancels or disk fills | No misleading complete file; original destination intact | Open — no scenario-level acceptance evidence recorded |
| AC-U09 | F48, F49 | V1.0 settings / V2.0 backup | Import settings; V2 backup/restore during writes | Validated consistent result; failure preserves active data | Open — no scenario-level acceptance evidence recorded. Split execution records |
| AC-C01 | F15, F16 | V1.0 | One ping target blocks ICMP; HTTPS reachable | No global outage inferred | Open — no scenario-level acceptance evidence recorded |
| AC-C02 | F15, F31 | V1.0 | All configured reachability checks fail then recover | Debounce/confirmation correct; evidence and uncertainty stored | Open — no scenario-level acceptance evidence recorded |
| AC-C03 | F15 | V1.0 | Local Wi-Fi stays connected behind captive portal | Limited/sign-in-suspected wording; no certain root-cause claim | Open — no scenario-level acceptance evidence recorded |
| AC-C04 | F15 | V1.0 | Sleep occurs during a failing-check incident | Observed segment ends with uncertainty; sleep is a gap | Open — no scenario-level acceptance evidence recorded |
| AC-C05 | F15 | V1.0 | Probes disabled; OS says link connected | Local status only; no fabricated remote uptime | Open — no scenario-level acceptance evidence recorded |
| AC-C06 | F15, F16, F27 | V1.0 | Proxy/DNS failure or endpoint timeout | Distinct test outcome; meter continues | Open — no scenario-level acceptance evidence recorded |
| AC-C07 | F15 | V1.0 | Compute availability with missing intervals | Unknown duration excluded; measured coverage displayed | Open — no scenario-level acceptance evidence recorded |
| AC-P01 | F20 | V1.5 | Isolated TCP/UDP/IPv4/IPv6 test apps | ≥95% provider-observed bytes attributed in supported fixtures; no cross-app assignment | Open — no scenario-level acceptance evidence recorded. P0 provider feasibility first |
| AC-P02 | F20, F22 | V1.5 | Rapid exits/restarts and PID reuse | Historical ownership correct; no reused-PID contamination | Open — no scenario-level acceptance evidence recorded. P0 lifecycle feasibility first |
| AC-P03 | F20, F22, F62 | V1.5 grouping / V2.0 tags | Browser children and shared system host | Supported grouping correct; unsupported service split not invented | Open — no scenario-level acceptance evidence recorded |
| AC-P04 | F14, F20, F23 | V1.5 | VPN/proxy/QUIC/loopback workloads | Declared attribution limits and domains correctly shown | Open — no scenario-level acceptance evidence recorded. P0 topology feasibility first |
| AC-P05 | F20 | V1.5 | Force ETW/provider loss or revoke access | Affected interval marked partial/unavailable; core meter survives | Open — no scenario-level acceptance evidence recorded. P0 loss/privilege feasibility first |
| AC-P06 | F20, F21, F23 | V1.5 | Provider totals differ from interface totals | No forced equality or fabricated subtraction bucket | Open — no scenario-level acceptance evidence recorded |
| AC-P07 | F20, F45 | V1.5 | Deny helper install; run portable mode | Honest capability state; no elevated UI fallback | Open — no scenario-level acceptance evidence recorded |
| AC-P08 | F20, F22 | V1.5 | Two Windows users and unauthorized IPC client | No disclosure of another user's process/history data | Open — no scenario-level acceptance evidence recorded |
| AC-Q01 | F16, F17 | V1.0 outcomes / V1.5 loss | Replay successful, timeout, pending and local-error probes | Correct denominator; no NaN; outcomes distinct | Open — no scenario-level acceptance evidence recorded |
| AC-Q02 | F17 | V1.5 | Replay known RTT series with missing probes | Jitter formula/pair count correct; no bridging gaps | Open — no scenario-level acceptance evidence recorded |
| AC-Q03 | F17, F18, F19 | V1.5 quality / V2.0 score | Too few samples or target changes | Warming/insufficient state; no stale score | Open — no scenario-level acceptance evidence recorded |
| AC-Q04 | F17, F47 | V1.5 | Aggregate loss/jitter across uneven buckets | Weighted counts/sums correct; percentiles not averaged | Open — no scenario-level acceptance evidence recorded |
| AC-Q05 | F18, F31, F32 | V1.0 mean-ping / V1.5 quality | Crossing/recrossing thresholds | Persistence and recovery hysteresis work | Open — no scenario-level acceptance evidence recorded |
| AC-Q06 | F19 | V2.0 if score ships | V2 numeric score boundary fixtures | Formula/clamping/version/evidence reproducible; insufficient data suppressed | Open — no scenario-level acceptance evidence recorded |
| AC-Q07 | F16, F18 | V1.0 endpoint context / V1.5 quality | High-latency remote target and otherwise healthy connection | Endpoint context clear; no universal game/ISP conclusion | Open — no scenario-level acceptance evidence recorded |
| AC-N01 | F31, F33 | V1.0 invariant / V1.5 watch | PC idle at zero throughput | No “slow internet” alert | Open — no scenario-level acceptance evidence recorded |
| AC-N02 | F33 | V1.5 if watch ships | Explicit transfer watch ends or transfer finishes | Watch expires; wording describes observed activity only | Open — no scenario-level acceptance evidence recorded |
| AC-N03 | F31, F32, F34, F64 | V1.0 / V1.5 additional rules | Multiple triggers/restarts/quiet hours | Deduplicated incidents, respected cooldown and popup suppression | Open — no scenario-level acceptance evidence recorded |
| AC-N04 | F31, F32, F33, F34 | V1.0 / V1.5 additional rules | Missing/stale observations | No false threshold trigger | Open — no scenario-level acceptance evidence recorded |
| AC-N05 | F35 | V1.5 | Insufficient baseline or method/network changes | No unsupported comparison insight | Open — no scenario-level acceptance evidence recorded |
| AC-N06 | F35, F29 | V1.5 baseline / V2.0 active-test exclusion | Valid comparative baseline and speed-test interval | Reproducible comparison; test load excluded from baseline, bytes still counted | Open — no scenario-level acceptance evidence recorded |
| AC-N07 | F31, F34, F35 | V1.0 alerts / V1.5 insights | Dismiss insight or disable alert | Preference persists; no immediate replay | Open — no scenario-level acceptance evidence recorded |
| AC-D01 | F24, F25 | V1.0 | Missing Wi-Fi capability/location consent | Unavailable fields clear; core monitoring unaffected | Open — no scenario-level acceptance evidence recorded |
| AC-D02 | F26, F14 | V1.0 lookup / V1.5 automatic history | Public IP lookup fails or split-route VPN is active | Cached timestamp/egress context visible; no protection claim | Open — no scenario-level acceptance evidence recorded |
| AC-D03 | F27, F28 | V1.0 / V2.0 expanded tools | Invalid/hostile diagnostic target input | No command injection; validated bounded operation | Open — no scenario-level acceptance evidence recorded |
| AC-D04 | F28 | V2.0 | Cancel timeout-heavy traceroute | Stops within documented job deadline; partial hops retained | Open — no scenario-level acceptance evidence recorded |
| AC-D05 | F28 | V2.0 | DNS flush with/without necessary access | Runs only after user intent; correct success/failure result | Open — no scenario-level acceptance evidence recorded |
| AC-D06 | F36, F55, F63 | V1.0 basic / V2.0 expanded report | Export support report containing sensitive fields | Redaction preview matches generated output | Open — no scenario-level acceptance evidence recorded |
| AC-D07 | F27, F28 | V1.0 gateway evidence / V2.0 hops | Gateway or intermediate hop rejects ICMP | No fabricated root-cause conclusion | Open — no scenario-level acceptance evidence recorded |
| AC-T01 | F29 | V2.0 | Provider configuration/approval missing | Feature unavailable honestly; V2 completion gate fails | Open — no scenario-level acceptance evidence recorded |
| AC-T02 | F29, F30 | V2.0 | Run normal test with server metadata | Correct phases, units, duration, provenance and history | Open — no scenario-level acceptance evidence recorded |
| AC-T03 | F29 | V2.0 | Hit byte/time cap or cancel | Transfers stop within target; partial/capped result explicit | Open — no scenario-level acceptance evidence recorded |
| AC-T04 | F29 | V2.0 | Start duplicate test or route changes mid-test | No concurrent duplicate job; result invalidation explained | Open — no scenario-level acceptance evidence recorded |
| AC-T05 | F29, F43 | V2.0 | Metered network and schedule constraints | No unexpected background test or quota bypass | Open — no scenario-level acceptance evidence recorded. Scheduling Should if offered |
| AC-T06 | F29, F30, F36 | V2.0 | Compare/export results and inspect usage ledger | Comparable results labelled; test bytes remain in quota | Open — no scenario-level acceptance evidence recorded |
| AC-X01 | F50 | V2.0 | Supported borderless game and anti-cheat configuration | Normal overlay functions within declared support; no injection | Open — no scenario-level acceptance evidence recorded |
| AC-X02 | F51 | V2.0 | OBS capture while UI resizes/errors/changes network | Only approved metrics shown; no private detail flash | Open — no scenario-level acceptance evidence recorded |
| AC-X03 | F40 | V2.0 if styles ship | Circular style without capacity; profile switching | No invented utilization; monitoring/history unchanged | Open — no scenario-level acceptance evidence recorded |
| AC-X04 | F56 | V2.0 | Bengali localization and long text | No clipped critical values; settings remain understandable | Open — no scenario-level acceptance evidence recorded |
| AC-X05 | F53 | Conditional extension | Optional cloud replay, conflict, deletion, outage | Idempotent sync; tombstones respected; local app usable | Open — no scenario-level acceptance evidence recorded |
| AC-X06 | F52, F59 | Conditional extensions | Optional browser feed/privileged control | Narrow authorized interface; no remote command/file exposure; control gate passed | Open — no scenario-level acceptance evidence recorded. Separate browser/control threat reviews |
| AC-S01 | F15, F16, F26, F27, F29, F54, F57 | V1.0 and every later release | All external features/telemetry disabled | No product-origin network requests during observed core session | Open — no scenario-level acceptance evidence recorded |
| AC-S02 | F11, F20, F24, F45 | V1.0 / V1.5 helper | Standard user denies optional permissions | Core functions usable; clear feature-specific limitation | Open — no scenario-level acceptance evidence recorded |
| AC-S03 | F54 | V1.0 | Tampered update/signature or manifest | Rejected before execution; previous app remains usable | Open — no scenario-level acceptance evidence recorded |
| AC-S04 | F47, F54 | V1.0 and every schema upgrade | Upgrade with history, then simulate migration failure | No destructive data loss; compatible recovery available | Open — no scenario-level acceptance evidence recorded |
| AC-S05 | F47 | V1.0 | Force terminate during commit/compaction | Committed history consistent; bounded uncommitted loss/gap | Open — no scenario-level acceptance evidence recorded |
| AC-S06 | F47, F45 | V1.0 | Disk-full/corrupt database/read-only destination | Clear degraded state; bounded memory; no silent success | Open — no scenario-level acceptance evidence recorded |
| AC-S07 | F44, F47, F54 | V1.0 | Uninstall with retain/delete choices | Registrations/collectors removed; selected data policy honoured | Open — no scenario-level acceptance evidence recorded |
| AC-S08 | F55, F57, F53 | V1.0 consent / commercial and cloud when offered | Opt-in/out, account/license outage or expiry | Consent respected; no history deletion or repeated sign-in traps | Open — no scenario-level acceptance evidence recorded |
| AC-S09 | F48, F49, F52, F59 | V1.0 settings / V2.0 backup / extensions | Malformed imports/backups/IPC payloads | Validated rejection; no traversal, escalation or active-data overwrite | Open — no scenario-level acceptance evidence recorded |
| AC-F01 | F01, F03, F04, F43 | V1.0 | Reference and low-end background benchmarks | Section 23 budgets passed or formally revised before claims | Open — no scenario-level acceptance evidence recorded. P0 initial benchmark before release budget claims |
| AC-F02 | F06, F08, F20, F47 | V1.0 dashboard/history / V1.5 app load | Populated history, live dashboard, app load | Responsive queries/UI; bounded queues and documented attribution quality | Open — no scenario-level acceptance evidence recorded |
| AC-F03 | F01, F11, F44, F47 | V1.0 and later regression | 24-hour mixed workload | No crash, duplication or unrecovered collection failure | Open — no scenario-level acceptance evidence recorded |
| AC-F04 | F01, F06, F11, F47 | V1.0 and later regression | 7-day soak with repeated open/close/adapter changes | No leak trend beyond target or database corruption | Open — no scenario-level acceptance evidence recorded |
| AC-F05 | F01, F03, F04, F12, F42, F44, F46 | V1.0 | Startup, resume, shell restart, monitor changes | Recovery timing and safe display targets met | Open — no scenario-level acceptance evidence recorded. P0 lifecycle findings |
