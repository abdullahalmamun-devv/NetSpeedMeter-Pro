$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prd = Get-Content -LiteralPath (Join-Path $root 'Premium-Speed-Meter-PRD.md')
# Analyst-maintained associations. PRD text and release qualifications are copied verbatim.
$mapping = @'
AC-M01;F01,F07;V1.0
AC-M02;F01,F06,F07;V1.0
AC-M03;F01,F11,F47;V1.0
AC-M04;F01,F02,F07;V1.0
AC-M05;F01,F07,F15,F47;V1.0
AC-M06;F01,F11;V1.0
AC-M07;F01,F03,F04,F06;V1.0
AC-M08;F01,F08,F09,F47;V1.0
AC-A01;F11,F12;V1.0
AC-A02;F11;V1.0
AC-A03;F13,F14;V1.0
AC-A04;F13;V1.0
AC-A05;F10,F11;V1.0
AC-A06;F12,F13;V1.0
AC-W01;F03,F42;V1.0
AC-W02;F03,F46;V1.0
AC-W03;F05,F46;V1.0
AC-W04;F04,F05,F42;V1.0
AC-W05;F41,F64;V1.0
AC-W06;F44;V1.0
AC-W07;F44,F46;V1.0
AC-W08;F02,F37,F38,F39,F58,F64;V1.0;V2 regression for Bengali/styles
AC-W09;F45;V1.0 if portable ships
AC-G01;F06;V1.0
AC-G02;F06,F43;V1.0
AC-G03;F07,F08,F09;V1.0
AC-G04;F02,F07;V1.0
AC-U01;F08,F10,F23,F47;V1.0;V1.5 app domain
AC-U02;F08,F09,F47;V1.0
AC-U03;F08,F09;V1.0
AC-U04;F09,F31,F34;V1.0
AC-U05;F09,F13;V1.0
AC-U06;F08,F09,F47;V1.0
AC-U07;F36,F56;V1.0;Bengali input required before translated UI
AC-U08;F36;V1.0
AC-U09;F48,F49;V1.0 settings / V2.0 backup;Split execution records
AC-C01;F15,F16;V1.0
AC-C02;F15,F31;V1.0
AC-C03;F15;V1.0
AC-C04;F15;V1.0
AC-C05;F15;V1.0
AC-C06;F15,F16,F27;V1.0
AC-C07;F15;V1.0
AC-P01;F20;V1.5;P0 provider feasibility first
AC-P02;F20,F22;V1.5;P0 lifecycle feasibility first
AC-P03;F20,F22,F62;V1.5 grouping / V2.0 tags
AC-P04;F14,F20,F23;V1.5;P0 topology feasibility first
AC-P05;F20;V1.5;P0 loss/privilege feasibility first
AC-P06;F20,F21,F23;V1.5
AC-P07;F20,F45;V1.5
AC-P08;F20,F22;V1.5
AC-Q01;F16,F17;V1.0 outcomes / V1.5 loss
AC-Q02;F17;V1.5
AC-Q03;F17,F18,F19;V1.5 quality / V2.0 score
AC-Q04;F17,F47;V1.5
AC-Q05;F18,F31,F32;V1.0 mean-ping / V1.5 quality
AC-Q06;F19;V2.0 if score ships
AC-Q07;F16,F18;V1.0 endpoint context / V1.5 quality
AC-N01;F31,F33;V1.0 invariant / V1.5 watch
AC-N02;F33;V1.5 if watch ships
AC-N03;F31,F32,F34,F64;V1.0 / V1.5 additional rules
AC-N04;F31,F32,F33,F34;V1.0 / V1.5 additional rules
AC-N05;F35;V1.5
AC-N06;F35,F29;V1.5 baseline / V2.0 active-test exclusion
AC-N07;F31,F34,F35;V1.0 alerts / V1.5 insights
AC-D01;F24,F25;V1.0
AC-D02;F26,F14;V1.0 lookup / V1.5 automatic history
AC-D03;F27,F28;V1.0 / V2.0 expanded tools
AC-D04;F28;V2.0
AC-D05;F28;V2.0
AC-D06;F36,F55,F63;V1.0 basic / V2.0 expanded report
AC-D07;F27,F28;V1.0 gateway evidence / V2.0 hops
AC-T01;F29;V2.0
AC-T02;F29,F30;V2.0
AC-T03;F29;V2.0
AC-T04;F29;V2.0
AC-T05;F29,F43;V2.0;Scheduling Should if offered
AC-T06;F29,F30,F36;V2.0
AC-X01;F50;V2.0
AC-X02;F51;V2.0
AC-X03;F40;V2.0 if styles ship
AC-X04;F56;V2.0
AC-X05;F53;Conditional extension
AC-X06;F52,F59;Conditional extensions;Separate browser/control threat reviews
AC-S01;F15,F16,F26,F27,F29,F54,F57;V1.0 and every later release
AC-S02;F11,F20,F24,F45;V1.0 / V1.5 helper
AC-S03;F54;V1.0
AC-S04;F47,F54;V1.0 and every schema upgrade
AC-S05;F47;V1.0
AC-S06;F47,F45;V1.0
AC-S07;F44,F47,F54;V1.0
AC-S08;F55,F57,F53;V1.0 consent / commercial and cloud when offered
AC-S09;F48,F49,F52,F59;V1.0 settings / V2.0 backup / extensions
AC-F01;F01,F03,F04,F43;V1.0;P0 initial benchmark before release budget claims
AC-F02;F06,F08,F20,F47;V1.0 dashboard/history / V1.5 app load
AC-F03;F01,F11,F44,F47;V1.0 and later regression
AC-F04;F01,F06,F11,F47;V1.0 and later regression
AC-F05;F01,F03,F04,F12,F42,F44,F46;V1.0;P0 lifecycle findings
'@
$map = @{}
foreach ($line in ($mapping -split '\r?\n')) {
    $c = $line.Split(';')
    $map[$c[0]] = @{ Features = $c[1]; Release = $c[2]; Note = $(if ($c.Length -gt 3) { $c[3] } else { '' }) }
}
$partial = @{
 F01='M/W: raw single-adapter rates, integer session bytes, monotonic timing; persistent epochs/gaps and supported-OS validation pending'
 F02='M/U: decimal auto bytes/bits and 0.5/1/2 s controls; precision, fixed units, hysteresis and smoothing pending'
 F04='U: real floating widget with show/hide; persisted position, required interactions and display QA pending'
 F05='U: drag and fixed topmost present; snap, lock, opacity, click-through and recovery controls pending'
 F06='G: 60 s two-series graph with gap breaks; remaining ranges, extrema downsampling and inspection pending'
 F07='M/U: session bytes/reset and measured duration; mean, peak interval context and durable-history separation pending'
 F11='W/U: native inventory and explicit selection; stable incarnation epochs and broader hardware QA pending'
 F25='W/U: adapter name/description only in UI; required addresses, gateway, DNS and copy actions pending'
 F42='U: PerMonitorV2 manifest and reset on display change; relative positions/mixed-DPI/removal QA pending'
 F44='U: close-to-tray and duplicate-instance guard; startup/delay, persisted preference and focus-existing IPC pending'
 F46='U: dashboard/widget/reset/pause/quit tray entries; full menu, hotkeys and shell recovery QA pending'
 F55='U: no ad/account path in current build; Help, support route and redacted bundle pending'
 F58='U: selected automation names, standard controls and widget Enter/Escape; Narrator/high contrast/text-scale review pending'
}
$evidence = @{
 'AC-M01'='Partial evidence: baseline/exact integer/conversion fixtures in T; full release verification open'
 'AC-M02'='Partial evidence: delayed-tick fixture in T; end-to-end coarse history/peak handling open'
 'AC-M03'='Partial evidence: reset fixture in T; persistent epoch/gap and interface recreation open'
 'AC-M04'='Partial evidence: side-effect fixture and prior smoke in V; fixed units/precision/smoothing absent'
 'AC-M05'='Partial evidence: interruption fixture and prior pause smoke; real sleep/hibernate and durable gaps untested'
 'AC-M06'='Not executed: prior native/managed counter bracket is not independent 60-second accuracy evidence'
 'AC-M07'='Partial evidence: prior dashboard value match; quick panel/docked surface absent, full skew experiment pending'
 'AC-M08'='Partial evidence: wall-clock reversal fixture; reporting event/timezone mapping absent'
 'AC-A02'='Partial evidence: wrong/missing selected source fixture; real adapter removal UI test pending'
 'AC-W06'='Partial implementation: mutex rejects duplicate process; existing UI is not focused'
 'AC-W07'='Partial evidence: prior pause/resume smoke; close/quit/relaunch lifecycle scenario not fully recorded'
 'AC-G01'='Partial implementation: 60 s gap-aware graph; timeframe/extrema fixture suite absent'
 'AC-G02'='Partial implementation: IsVisible gates invalidation; resize/hidden-rendering experiment absent'
 'AC-G03'='Partial evidence: session reset fixture/smoke; persistent daily/quota data absent'
}
$frows = @($prd | Where-Object { $_ -match '^\| F\d\d \|' })
$arows = @($prd | Where-Object { $_ -match '^\| AC-[A-Z]\d\d \|' })
if ($frows.Count -ne 64 -or $arows.Count -ne 98 -or $map.Count -ne 98) { throw 'Unexpected PRD or mapping cardinality.' }
$header = @'
# Feature traceability — PRD revision 2.0

Audit date: 11 October 2026 (Asia/Dhaka). Owner: Product / PRD Analyst. Review: Project Director. This is the initial P0 baseline snapshot; concurrent engineering changes require reconciliation after integration. No feature is declared Done and no scope is deferred here.

The PRD remains authoritative. The target and qualification columns reproduce its feature register. F26, F56 and F63 have release qualifications but no explicit Must/Should token; retain their requirements and obtain priority clarification rather than silently treating them as optional. See [open decisions](OPEN-DECISIONS.md).

**Status:** Partial = implemented sub-capability with unmet requirements or validation. Not started = no implementation of the named capability found. Implemented primitives are listed below; none establishes full feature completion. Future/conditional work stays outside core-release completion.

**Evidence keys:** M = `src/SpeedMeter.Core/Measurement.cs`; W = `src/SpeedMeter.Windows/AdapterProvider.cs`; U = `src/SpeedMeter.App/{App,MainWindow,WidgetWindow}` and `app.manifest`; G = `TrafficChart.cs`; T = `tests/SpeedMeter.Checks/Program.cs`; V = `docs/VALIDATION.md` (14 deterministic checks, one native comparison and UI smoke on Windows build 19045 only). Paths are repository-relative. Prior test outcomes were reviewed, not rerun by this documentation task.

Implemented sub-capabilities: native 64-bit interface reads, explicit single-adapter selection, baseline/delta arithmetic, interruption rebaseline, raw session byte totals, decimal unit conversion, real dashboard/widget values, 60-second graph, basic tray and pause/resume/reset. Missing features are not replaced with fabricated observations.

AC associations below identify relevant tests, not exhaustive coverage or passed results. The [acceptance register](ACCEPTANCE-MAP.md) retains every original scenario and criterion. Global accessibility/privacy/performance gates also apply to every feature even where omitted from a row.

| ID | Capability | PRD target | PRD priority / qualification | Relevant AC | Baseline status and implementation evidence |
|---|---|---|---|---|---|
'@
$out = [System.Collections.Generic.List[string]]::new()
$out.Add($header)
foreach ($row in $frows) {
 $c = $row.Split('|'); $id = $c[1].Trim()
 $acs = @($map.Keys | Where-Object { $map[$_].Features.Split(',') -contains $id } | Sort-Object) -join ', '
 if (-not $acs) { $acs = 'No dedicated case; configuration matrix + release gate (F60/F61)' }
 $state = if ($partial.ContainsKey($id)) { 'Partial — ' + $partial[$id] } else { 'Not started — no named capability implementation/evidence found' }
 $out.Add("| $id | $($c[2].Trim()) | $($c[3].Trim()) | $($c[4].Trim()) | $acs | $state |")
}
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'FEATURE-MAP.md') -Value $out -Encoding utf8
$out.Clear()
$out.Add(@'
# Acceptance traceability — 98 original cases

Audit date: 11 October 2026 (Asia/Dhaka). Source: PRD revision 2.0 section 27. Owner: Product / PRD Analyst; reviewer: Project Director. Each original ID appears once. Release splits below are analysis, not PRD changes. Execute separate sub-scenarios under the original ID where a case crosses releases. P0 feasibility evidence supports eventual acceptance but never substitutes for a release gate.

All cases remain open for release acceptance. Evidence notes distinguish prior fixture/smoke evidence, partial implementation and no recorded experiment. T and V refer to the test runner and validation report described in [FEATURE-MAP.md](FEATURE-MAP.md). No tests were rerun for this documentation-only task.

Every eventual execution record needs setup, expected/actual results, build/commit, OS/configuration, evidence path, reviewer and outstanding failures. Carry forward each earlier-release case as regression coverage. Read-only source review cannot prove a Windows or privacy test passed.

| ID | Features | Release / qualification | Original condition | Original pass criterion | Current evidence / remaining work |
|---|---|---|---|---|---|
'@)
foreach ($row in $arows) {
 $c = $row.Split('|'); $id = $c[1].Trim()
 if (-not $map.ContainsKey($id)) { throw "Missing $id" }
 $entry = $map[$id]
 $note = if ($evidence.ContainsKey($id)) { $evidence[$id] } else { 'Open — no scenario-level acceptance evidence recorded' }
 if ($entry.Note) { $note += '. ' + $entry.Note }
 $out.Add("| $id | $($entry.Features -replace ',', ', ') | $($entry.Release) | $($c[2].Trim()) | $($c[3].Trim()) | $note |")
}
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ACCEPTANCE-MAP.md') -Value $out -Encoding utf8
Write-Output "Generated and validated $($frows.Count) feature rows and $($arows.Count) acceptance rows; all 98 IDs mapped."
