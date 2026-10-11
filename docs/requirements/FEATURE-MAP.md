# Feature traceability — PRD revision 2.0

Audit date: 11 October 2026 (Asia/Dhaka). Owner: Product / PRD Analyst. Review: Project Director. This is the initial P0 baseline snapshot; concurrent engineering changes require reconciliation after integration. No feature is declared Done and no scope is deferred here.

The PRD remains authoritative. The target and qualification columns reproduce its feature register. F26, F56 and F63 have release qualifications but no explicit Must/Should token; retain their requirements and obtain priority clarification rather than silently treating them as optional. See [open decisions](OPEN-DECISIONS.md).

**Status:** Partial = implemented sub-capability with unmet requirements or validation. Not started = no implementation of the named capability found. Implemented primitives are listed below; none establishes full feature completion. Future/conditional work stays outside core-release completion.

**Evidence keys:** M = `src/SpeedMeter.Core/Measurement.cs`; W = `src/SpeedMeter.Windows/AdapterProvider.cs`; U = `src/SpeedMeter.App/{App,MainWindow,WidgetWindow}` and `app.manifest`; G = `TrafficChart.cs`; T = `tests/SpeedMeter.Checks/Program.cs`; V = `docs/VALIDATION.md` (14 deterministic checks, one native comparison and UI smoke on Windows build 19045 only). Paths are repository-relative. Prior test outcomes were reviewed, not rerun by this documentation task.

Implemented sub-capabilities: native 64-bit interface reads, explicit single-adapter selection, baseline/delta arithmetic, interruption rebaseline, raw session byte totals, decimal unit conversion, real dashboard/widget values, 60-second graph, basic tray and pause/resume/reset. Missing features are not replaced with fabricated observations.

AC associations below identify relevant tests, not exhaustive coverage or passed results. The [acceptance register](ACCEPTANCE-MAP.md) retains every original scenario and criterion. Global accessibility/privacy/performance gates also apply to every feature even where omitted from a row.

| ID | Capability | PRD target | PRD priority / qualification | Relevant AC | Baseline status and implementation evidence |
|---|---|---|---|---|---|
| F01 | Real-time upload/download throughput | V1.0 | Must | AC-F01, AC-F03, AC-F04, AC-F05, AC-M01, AC-M02, AC-M03, AC-M04, AC-M05, AC-M06, AC-M07, AC-M08 | Partial — M/W: raw single-adapter rates, integer session bytes, monotonic timing; persistent epochs/gaps and supported-OS validation pending |
| F02 | Auto units, bits/bytes, precision, refresh controls | V1.0 | Must; 250 ms advanced mode only V2 after profiling | AC-G04, AC-M04, AC-W08 | Partial — M/U: decimal auto bytes/bits and 0.5/1/2 s controls; precision, fixed units, hysteresis and smoothing pending |
| F03 | Compact taskbar-adjacent meter | V1.0 | Must on approved configurations; compatibility fallback | AC-F01, AC-F05, AC-M07, AC-W01, AC-W02 | Not started — no named capability implementation/evidence found |
| F04 | Floating desktop widget | V1.0 | Must | AC-F01, AC-F05, AC-M07, AC-W04 | Partial — U: real floating widget with show/hide; persisted position, required interactions and display QA pending |
| F05 | Drag, snap, lock, opacity, topmost, click-through | V1.0 | Must | AC-W03, AC-W04 | Partial — U: drag and fixed topmost present; snap, lock, opacity, click-through and recovery controls pending |
| F06 | Live graph: 30 s, 1 min, 5 min, 15 min, 1 h | V1.0 | Must; one-hour data coarsens as needed | AC-F02, AC-F04, AC-G01, AC-G02, AC-M02, AC-M07 | Partial — G: 60 s two-series graph with gap breaks; remaining ranges, extrema downsampling and inspection pending |
| F07 | Session total, mean and peak | V1.0 | Must; app monitoring-session baseline | AC-G03, AC-G04, AC-M01, AC-M02, AC-M04, AC-M05 | Partial — M/U: session bytes/reset and measured duration; mean, peak interval context and durable-history separation pending |
| F08 | Daily, weekly, monthly and custom usage | V1.0 | Must | AC-F02, AC-G03, AC-M08, AC-U01, AC-U02, AC-U03, AC-U06 | Not started — no named capability implementation/evidence found |
| F09 | Billing cycle, daily/monthly quota, alerts | V1.0 | Must | AC-G03, AC-M08, AC-U02, AC-U03, AC-U04, AC-U05, AC-U06 | Not started — no named capability implementation/evidence found |
| F10 | Per-network history and friendly names | V1.0 | Must where identity reliable; unresolved bucket otherwise | AC-A05, AC-U01 | Not started — no named capability implementation/evidence found |
| F11 | Adapter detection and manual selection | V1.0 | Must | AC-A01, AC-A02, AC-A05, AC-F03, AC-F04, AC-M03, AC-M06, AC-S02 | Partial — W/U: native inventory and explicit selection; stable incarnation epochs and broader hardware QA pending |
| F12 | Automatic network switching | V1.0 | Must within declared scope | AC-A01, AC-A06, AC-F05 | Not started — no named capability implementation/evidence found |
| F13 | Multiple-interface totals | V1.0 | Must for validated disjoint scopes; ambiguity shown | AC-A03, AC-A04, AC-A06, AC-U05 | Not started — no named capability implementation/evidence found |
| F14 | VPN candidate indicator and scope handling | V1.0 | Must as best-effort observation | AC-A03, AC-D02, AC-P04 | Not started — no named capability implementation/evidence found |
| F15 | Connection status and basic downtime timeline | V1.0 | Must; optional probes, explicit evidence source | AC-C01, AC-C02, AC-C03, AC-C04, AC-C05, AC-C06, AC-C07, AC-M05, AC-S01 | Not started — no named capability implementation/evidence found |
| F16 | Basic current/min/max/mean ping | V1.0 | Must when user enables probes | AC-C01, AC-C06, AC-Q01, AC-Q07, AC-S01 | Not started — no named capability implementation/evidence found |
| F17 | Rolling jitter and probe-loss metrics/history | V1.5 | Must | AC-Q01, AC-Q02, AC-Q03, AC-Q04 | Not started — no named capability implementation/evidence found |
| F18 | Explainable qualitative connection status | V1.5 | Must | AC-Q03, AC-Q05, AC-Q07 | Not started — no named capability implementation/evidence found |
| F19 | Optional numeric quality score | V2.0 | Should; calibration gate, formula/version shown | AC-Q03, AC-Q06 | Not started — no named capability implementation/evidence found |
| F20 | Per-app live upload/download and totals | V1.5 | Must in supported installed mode | AC-F02, AC-P01, AC-P02, AC-P03, AC-P04, AC-P05, AC-P06, AC-P07, AC-P08, AC-S02 | Not started — no named capability implementation/evidence found |
| F21 | Top bandwidth consumers | V1.5 | Must | AC-P06 | Not started — no named capability implementation/evidence found |
| F22 | App grouping, details, historical trends | V1.5 | Must | AC-P02, AC-P03, AC-P08 | Not started — no named capability implementation/evidence found |
| F23 | Transfer breakdown by direction/application | V1.5 | Must; category tags in V2 | AC-P04, AC-P06, AC-U01 | Not started — no named capability implementation/evidence found |
| F24 | Wi-Fi SSID, signal, channel/band/link info | V1.0 | Must where APIs/permissions expose it | AC-D01, AC-S02 | Not started — no named capability implementation/evidence found |
| F25 | Local IP, gateway, subnet, MAC, DNS, adapter info | V1.0 | Must where exposed | AC-D01 | Partial — W/U: adapter name/description only in UI; required addresses, gateway, DNS and copy actions pending |
| F26 | Public IP lookup, copy and optional change history | V1.0 / V1.5 | Lookup V1; change alerts/history V1.5; opt-in requests | AC-D02, AC-S01 | Not started — no named capability implementation/evidence found |
| F27 | Basic reachability and DNS lookup | V1.0 | Must; user-initiated tools | AC-C06, AC-D03, AC-D07, AC-S01 | Not started — no named capability implementation/evidence found |
| F28 | Traceroute, bounded diagnostic suite and DNS flush | V2.0 | Must; flush explicit and privilege-aware | AC-D03, AC-D04, AC-D05, AC-D07 | Not started — no named capability implementation/evidence found |
| F29 | Built-in download/upload speed test | V2.0 | Must; approved provider/backend gate | AC-N06, AC-S01, AC-T01, AC-T02, AC-T03, AC-T04, AC-T05, AC-T06 | Not started — no named capability implementation/evidence found |
| F30 | Test history and comparisons | V2.0 | Must | AC-T02, AC-T06 | Not started — no named capability implementation/evidence found |
| F31 | Quota/connectivity/latency notifications | V1.0 | Must; relevant monitor must be enabled | AC-C02, AC-N01, AC-N03, AC-N04, AC-N07, AC-Q05, AC-U04 | Not started — no named capability implementation/evidence found |
| F32 | App high-usage and advanced quality alerts | V1.5 | Must | AC-N03, AC-N04, AC-Q05 | Not started — no named capability implementation/evidence found |
| F33 | Transfer-rate threshold alert | V1.5 | Should; explicit transfer watch, never idle = slow | AC-N01, AC-N02, AC-N04 | Not started — no named capability implementation/evidence found |
| F34 | Quiet hours, cooldown, deduplication, alert history | V1.0 | Must | AC-N03, AC-N04, AC-N07, AC-U04 | Not started — no named capability implementation/evidence found |
| F35 | Local baselines and rule-based insights | V1.5 | Must with minimum evidence | AC-N05, AC-N06, AC-N07 | Not started — no named capability implementation/evidence found |
| F36 | CSV/JSON data export | V1.0 | Must for available datasets; expand in later releases | AC-D06, AC-T06, AC-U07, AC-U08 | Not started — no named capability implementation/evidence found |
| F37 | Light/dark/system theme | V1.0 | Must | AC-W08 | Not started — no named capability implementation/evidence found |
| F38 | Font, accent, background, opacity customization | V1.0 | Must with accessibility constraints | AC-W08 | Not started — no named capability implementation/evidence found |
| F39 | Minimal, Compact Graph, Detailed widget layouts | V1.0 | Must | AC-W08 | Not started — no named capability implementation/evidence found |
| F40 | Compact pill, quality card, optional circular display | V2.0 | Should; circular scale semantics must be explicit | AC-X03 | Not started — no named capability implementation/evidence found |
| F41 | Idle dimming/collapse and fullscreen auto-hide | V1.0 | Must | AC-W05 | Not started — no named capability implementation/evidence found |
| F42 | Multi-monitor and DPI handling | V1.0 | Must | AC-F05, AC-W01, AC-W04 | Partial — U: PerMonitorV2 manifest and reset on display change; relative positions/mixed-DPI/removal QA pending |
| F43 | Low-resource/battery mode | V1.0 | Must | AC-F01, AC-G02, AC-T05 | Not started — no named capability implementation/evidence found |
| F44 | Startup with optional delay, close-to-tray behaviour | V1.0 | Must; user-controlled | AC-F03, AC-F05, AC-S07, AC-W06, AC-W07 | Partial — U: close-to-tray and duplicate-instance guard; startup/delay, persisted preference and focus-existing IPC pending |
| F45 | Portable package | V1.0 | Should; published reduced capability matrix | AC-P07, AC-S02, AC-S06, AC-W09 | Not started — no named capability implementation/evidence found |
| F46 | Global hotkeys and complete tray menu | V1.0 | Must; later commands follow feature availability | AC-F05, AC-W02, AC-W03, AC-W07 | Partial — U: dashboard/widget/reset/pause/quit tray entries; full menu, hotkeys and shell recovery QA pending |
| F47 | Local DB, retention, delete controls | V1.0 | Must | AC-F02, AC-F03, AC-F04, AC-M03, AC-M05, AC-M08, AC-Q04, AC-S04, AC-S05, AC-S06, AC-S07, AC-U01, AC-U02, AC-U06 | Not started — no named capability implementation/evidence found |
| F48 | Configuration export/import | V1.0 | Must | AC-S09, AC-U09 | Not started — no named capability implementation/evidence found |
| F49 | Consistent history backup/restore | V2.0 | Must | AC-S09, AC-U09 | Not started — no named capability implementation/evidence found |
| F50 | Gaming overlay/profile | V2.0 | Must for windowed/borderless modes; no injection | AC-X01 | Not started — no named capability implementation/evidence found |
| F51 | OBS-friendly capture overlay | V2.0 | Must; personal data hidden | AC-X02 | Not started — no named capability implementation/evidence found |
| F52 | Optional local browser-source feed | V2 ext. | Conditional secure endpoint design | AC-S09, AC-X06 | Not started — no named capability implementation/evidence found |
| F53 | Optional cloud sync and multi-PC summaries | V2 ext. | Conditional separate opt-in service | AC-S08, AC-X05 | Not started — no named capability implementation/evidence found |
| F54 | Signed updates, release notes, rollback/recovery | V1.0 | Must | AC-S01, AC-S03, AC-S04, AC-S07 | Not started — no named capability implementation/evidence found |
| F55 | Ad-free experience and support workflow | V1.0 | Must | AC-D06, AC-S08 | Partial — U: no ad/account path in current build; Help, support route and redacted bundle pending |
| F56 | Localization-ready UI; Bengali translation | V1.0 / V2.0 | Infrastructure V1; English+Bengali reviewed UI V2 | AC-U07, AC-X04 | Not started — no named capability implementation/evidence found |
| F57 | Optional privacy-preserving product/crash analytics | V1.0 | Should; off by default | AC-S01, AC-S08 | Not started — no named capability implementation/evidence found |
| F58 | Accessible keyboard/screen-reader operation | V1.0 | Must | AC-W08 | Partial — U: selected automation names, standard controls and widget Enter/Escape; Narrator/high contrast/text-scale review pending |
| F59 | Per-app limits, block and scheduled control rules | Future / V2 ext. research | Conditional; separate engineering gate | AC-S09, AC-X06 | Not started — no named capability implementation/evidence found |
| F60 | Windows 10 legacy compatibility | V2 ext. | Conditional OS/runtime support decision | No dedicated case; configuration matrix + release gate (F60/F61) | Not started — no named capability implementation/evidence found |
| F61 | Native ARM64 build | V2 ext. | Conditional native dependency and QA results | No dedicated case; configuration matrix + release gate (F60/F61) | Not started — no named capability implementation/evidence found |
| F62 | User-defined application category tags | V2.0 | Should; no content inspection | AC-P03 | Not started — no named capability implementation/evidence found |
| F63 | Save/export diagnostic report | V1.0 / V2.0 | Basic V1; expanded suite V2 | AC-D06 | Not started — no named capability implementation/evidence found |
| F64 | Accessibility-preserving notification/presentation mode | V1.0 | Must; monitoring continues | AC-N03, AC-W05, AC-W08 | Not started — no named capability implementation/evidence found |
