# Windows, scope and attribution audit

Date: 11 October 2026 (Asia/Dhaka). Owner: independent Windows/scope/security auditor. Reviewer: Project Director. Status: **CHANGES REQUESTED**; no release decision is made here.

Scope: current Windows providers, canonical scope policy and its actual application wiring, taskbar companion, attribution feasibility and associated claims. PRD references: sections 7, 8, 9, 13, 24 and 29. Production, tests, board and status were not edited during this audit. Measurement arithmetic authored earlier by this auditor is outside this independent review.

## Assessment

The selected-interface prototype has a real native counter path and passes the existing checks. The repository does **not** yet demonstrate the P0 route/VPN boundary, docked shell behavior, or app-attribution gate. Several checks confirm the current implementation's assumptions rather than the PRD's required behavior. Existing handoffs, reviews and board declarations are claims, not substitutes for the evidence below.

P1 denotes a correctness/claim issue that must be resolved before the affected capability is exposed or its P0 gate accepted. P2 denotes a narrower defect or missing contract/evidence. An inactive experimental class is distinguished from a defect currently reachable in the shipping prototype.

## Findings

### WS-01 — P1: IPv4 destination routing is presented as canonical default routing with invented metrics

**Evidence:** `src/SpeedMeter.Windows/RouteProvider.cs:31–38,60–71`; `src/SpeedMeter.Core/ScopeDomain.cs:34–43,228–249`.

The provider asks `GetBestInterface` for the route to the literal IPv4 destination `8.8.8.8`, equates a successful result or gateway presence with `HasDefaultRoute`, then supplies **10 or 20** as `RouteMetric`. Those numbers are synthetic preference ranks, not observed route metrics, yet policy displays “Metric 10” and claims lowest-metric default egress. A more-specific route to that one destination can select a different interface from the default route. IPv6 routes and per-family metrics are not represented; `DualStackDisjoint` exists only as an enum value.

This call is a **local route lookup**, not an external probe or contact with Google. No privacy defect is implied by the literal address. Microsoft defines the function as an IPv4 destination route lookup; actual route preference combines route and interface metrics. [GetBestInterface](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getbestinterface), [MIB_IPFORWARD_ROW2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipforward_row2)

**Impact/status:** Incorrect Auto-primary decisions if this dormant provider is wired as proposed; no current automatic route switching occurs in the App. PRD 8.2 requires IPv4 and IPv6 route metadata and partial disclosure for independent egress paths.

**Repair:** Model family-specific observed routes and metrics; enumerate both families with `GetIpForwardTable2`, join interface metrics, distinguish destination-specific routing from default prefixes, and preserve unknown/partial outcomes. Do not label preference ranks as OS metrics. [GetIpForwardTable2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipforwardtable2)

**Tests:** IPv6-only; separate IPv4/IPv6 defaults; same-family equal metrics; route-plus-interface metric disagreement; explicit host route to the lookup destination; no default route; independent simultaneous egress. Compare to a read-only route-table capture in a controlled lab.

### WS-02 — P1: “ValidatedAggregate” declares independence without evidence or explicit membership

**Evidence:** `src/SpeedMeter.Core/ScopeDomain.cs:272–325`; `src/SpeedMeter.Windows/RouteProvider.cs:57–58,99–121`; `tests/SpeedMeter.Checks/ScopeFixtures.cs:118` and subsequent aggregate fixture.

All up `IsHardware` candidates are selected automatically. Absence of a recognized VPN/bridge name is treated as proof of independence. There is no explicit selected boundary set, duplicate-boundary evidence, or membership interval. Furthermore, `RouteProvider` derives `IsHardware` from Ethernet/Wi-Fi classification rather than the native hardware flag: an unrecognized virtual Ethernet interface can qualify. The fixture calls two generic Ethernet candidates “purely disjoint” without supplying evidence that establishes disjointness.

**Impact/status:** A latent duplicate-accounting path, not evidence that the current one-adapter UI already sums traffic twice. PRD 8.2–8.3 forbids a deduplicated aggregate claim without independent-boundary validation.

**Repair:** Keep validated aggregation unavailable until explicitly selected membership and a conservative validation result exist. Unknown topology must remain diagnostic/partial and ineligible for normal quota accounting. Preserve native physical metadata separately from heuristic classification.

**Tests:** Same physical/virtual traffic with unknown vendor names, bridge members, duplicate IDs, explicit independent memberships, unavailable membership evidence, and physical+VPN inputs. Assert refusal/partial state, not just an empty list for known vendor strings.

### WS-03 — P1: VPN branch overrides routing evidence and the PRD boundary policy

**Evidence:** `src/SpeedMeter.Core/ScopeDomain.cs:145–165`; `src/SpeedMeter.Windows/RouteProvider.cs:99–104`; `tests/SpeedMeter.Checks/ScopeFixtures.cs:99` and subsequent VPN fixture.

The first up VPN-classified candidate with a gateway/default flag is chosen before any metric comparison, even if its metric is worse than the physical default or multiple VPNs exist. The result asserts “primary egress” with `IsAmbiguous=false`. Classification includes mutable names containing “vpn” and every PPP interface. These hints cannot establish full-tunnel coverage. The PRD says to prefer a **known physical egress accounting boundary** for overall full-tunnel usage and show tunnel measurements separately; the fixture instead requires the tunnel as primary.

**Impact/status:** Latent mis-scoping/under-counting and overstated confidence, not current duplicate summation. Selecting one boundary avoids one particular duplication mechanism but does not prove canonical overall usage.

**Repair:** Separate observed tunnel hints from proven route coverage and accounting boundary choice. Prefer a demonstrated physical boundary when available; otherwise disclose scoped fallback/ambiguity and require selection. Treat split-tunnel or multiple tunnel cases as unresolved until demonstrated.

**Tests:** Higher-metric inactive-for-egress VPN, two VPNs, renamed physical adapter, ordinary PPP, full/split tunnel and physical egress changes. Tests must agree with PRD 8.2/8.5, or document an explicitly approved product change.

### WS-04 — P1: Scope decisions and revisions are not integrated

**Evidence:** `src/SpeedMeter.Application/Collector.cs:17–24,64–75,147–148`; `src/SpeedMeter.App/MonitorSession.cs:15–18,31`; `src/SpeedMeter.App/MainWindow.xaml.cs:169–175`. Repository search finds no `RouteProvider` or `ScopePolicy` call in App/Application.

The real pipeline supports selection of one adapter string ID. It does not consume `CanonicalScopeDecision`, route updates, scope revision, mode, memberships, ambiguity, or the proposed settling period. The fixture named “Scope revision change triggers MeasurementEngine rebaseline” manually calls `SelectScope` with a different ID; it does not exercise a scope revision or the collector integration.

**Impact/status:** Missing implementation and overstated evidence, not a reason to remove the working explicitly selected-adapter path. “Canonical scope engine verified/integrated” and “scope revision changes force rebaseline” are not supported by the application.

**Repair:** Record policy as an experimental proposal until corrected; then add an application-owned scope coordinator with tested revision/membership boundaries. Preserve selected-adapter no-fallback semantics. Display partial/ambiguous state in the same canonical snapshot. No UI-driven guessed Auto scope.

**Tests:** Drive scope decisions through the actual collector, including a revision change that retains the same visible adapter ID, settling flaps and selected disappearance/reappearance. Do not merely call the engine's existing selection method.

### WS-05 — P1: Taskbar geometry helper is not a demonstrated docked feature or Explorer subscription

**Evidence:** `src/SpeedMeter.Windows/TaskbarCompanion.cs:68–71`; `src/SpeedMeter.App/MainWindow.xaml.cs:40–57,194`; `docs/handoffs/P0-TASKBAR-001.md` subscription claim. Search finds no `TaskbarCompanion` caller in App/Application.

`GetTaskbarCreatedMessageId()` only registers/returns a message ID and is not called. There is no companion-window creation, HWND message handler, docked positioning application, taskbar state-change response or shell-restart geometry refresh. The live check returned unavailable geometry and evaluated fallback coordinates against a hardcoded 1920×1040 work area; it did not position a window or exercise Explorer. Microsoft's example separately registers the message and handles it in the window procedure. [Taskbar creation notifications](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar)

**Important distinction:** The App uses `System.Windows.Forms.NotifyIcon`. Upstream WinForms includes its own `TaskbarCreated` handler and icon recreation, so absence of an application handler is **not proof that the tray icon fails recovery**. It does prove that this helper has no demonstrated companion recovery. No real Explorer restart was performed. [WinForms NotifyIcon source](https://raw.githubusercontent.com/dotnet/winforms/main/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs)

**Repair:** Correct the feature/evidence claims; implement a real companion coordinator only after placement safety is resolved. Attach/detach a window hook, recompute after relevant shell/display events, and preserve the floating fallback with explanation.

**Tests:** Actual Windows 11 companion HWND, taskbar hide/show and Explorer recreation in a designated lab, per-monitor DPI changes, monitor removal, floating explanation and tray recovery. Synthetic geometry is useful but cannot satisfy PRD 9.1 or 29.1 alone.

### WS-06 — P2: Placement can claim safe docking outside the work area, and fallback query discards auto-hide state

**Evidence:** `src/SpeedMeter.Windows/TaskbarCompanion.cs:108–114,125–188`; `tests/SpeedMeter.Checks/TaskbarFixtures.cs`.

Successful docking branches ignore `workArea` entirely and accept arbitrary dimensions. A source-level counterexample is bottom taskbar `(0,80,100,100)`, work area `(0,0,100,80)`, companion `200×40`: returned position is `(-112,36)` with `UseFloatingFallback=false`. The floating branches also cannot ensure the whole window fits when oversized. If AppBar position fails but `GetWindowRect` succeeds, the query guesses an edge from fixed pixel thresholds and returns `IsAutoHide=false` without checking auto-hide; this bypasses the stated safety rule precisely when geometry reliability is reduced.

**Repair:** Validate finite positive dimensions/rectangles, express geometry in a documented single unit and monitor space, prove containment and nonintersection, and resize/fallback when no full fit exists. Treat unknown auto-hide/edge as unavailable rather than false. Native pixels need explicit conversion before assigning WPF DIPs.

**Tests:** Narrow/oversized work areas, negative monitor coordinates, zero/NaN/infinite dimensions, intersecting rectangles, mixed DPI, unknown edge and position-query failure with auto-hide active. Existing six fixtures test nominal arithmetic, not these safety properties. Counterexample above is source arithmetic, not a separately executed new fixture.

### WS-07 — P1: Administrator role is incorrectly reported as complete attribution

**Evidence:** `src/SpeedMeter.Windows/AppAttributionSpike.cs:48–59,69–77`; `tests/SpeedMeter.Checks/AppAttributionFixtures.cs:44–59`; `docs/handoffs/P0-APPATTR-001.md` provider coverage/privilege claims.

`DetermineProviderCapability` reports `Complete` whenever the token is an administrator. It does not initialize any trace, parse an event, count bytes, measure loss, or test TCP/UDP/IPv4/IPv6. Current three checks compare identity records and repeat the same role predicate. No process lifecycle event stream or application mapping is implemented. Having start time in a record is useful design, but does not prove end-to-end PID-reuse-safe attribution.

Microsoft documents distinct permissions for general ETW sessions and NT Kernel Logger sessions, as well as non-permission initialization failures. A specific kernel-session permission constraint does not establish that every possible per-app provider universally requires elevation. Admin membership never proves completeness. [StartTrace permissions and failure outcomes](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracea)

**Repair:** Report NotEvaluated/Unsupported until an actual provider spike supplies availability; keep availability, required privilege and measured attribution completeness separate. Correct the universal provider/QUIC/byte-coverage claims and complete the bounded P0 experiment before accepting the helper decision.

**Tests:** Actual provider attempts under standard/elevated tokens, permission denial/loss, session conflict, TCP/UDP on both families, short-lived processes/PID reuse, event loss and user isolation. Preserve unresolved bytes only within the observed provider domain. This is an unmet explicit P0 gate (PRD 13.2 and 29.1), not merely V1.5 UI work.

### WS-08 — P2: Native counter lifetime identity is still unspecified

**Evidence:** `src/SpeedMeter.Windows/AdapterProvider.cs:35–39,71–73`.

Every native `AdapterCounter` omits `SourceEpoch`; all use its unspecified default. Interface GUID is normalized correctly and index is not used as permanent identity, but LUID/lifecycle information is discarded. The provider cannot signal recreated counter lifetimes that happen to produce increasing values by the next observation. Domain fixtures with synthetic epochs do not validate native lifetime detection.

**Repair:** Document unspecified continuity in the provider capability notes; carry a conservative observation/lifecycle epoch when supported by enumeration changes and notifications. Do not pretend the OS exposes an exact counter-generation token through this struct. Retain gaps/rebaseline when continuity cannot be established. [MIB_IF_ROW2 identity fields](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)

**Tests:** Injected disappearance/reappearance, replacement identity and changed source epoch with increasing counters; controlled real adapter cycle/sleep only in the lab. Distinguish inferred observed lifecycle from proof of every intervening reset.

### WS-09 — P2: Partial route discovery silently appears complete

**Evidence:** `src/SpeedMeter.Windows/RouteProvider.cs:40–43,46–90`.

A failure reading one interface's properties exits the entire enumeration through a blanket catch and returns any earlier candidates without a partial/failure marker. Native lookup failure is also discarded. Consumers cannot distinguish complete one-interface topology from a truncated snapshot, so policy can make confident primary/aggregate decisions from missing candidates.

**Repair:** Use typed provider availability plus per-family/interface evidence, preserve failures and keep enumeration failures isolated where safe. Do not classify an incomplete inventory as validated topology.

**Tests:** Failure before enumeration, on the first NIC and on a later NIC; unsupported IPv4 on an IPv6-only interface; route lookup denied/unavailable. Assert partial/unknown outputs and disabled validation.

### WS-10 — P2: Scope revision does not consistently identify boundary changes

**Evidence:** `src/SpeedMeter.Core/ScopeDomain.cs:73–84,88–104,129–141,309–313,330–335`.

Selected up→missing keeps the same revision when the selected ID is unchanged, although membership changes from one interface to none; clearing selection also retains revision. Conversely every unchanged no-active-egress evaluation increments revision. Aggregate and raw modes treat input ordering as membership change. These inconsistencies will matter once revision is an accounting boundary rather than an unused field.

**Repair:** Define revision as a change in normalized immutable membership/mode/boundary validity, compare sets deterministically, and emit a gap boundary on loss of active membership. Keep repeated identical unavailable observations stable.

**Tests:** Active→missing→missing→active; selected→none; candidate reorder without membership change; raw/aggregate mode change; same ID with changed boundary evidence. Verify coordinator behavior as well as the pure policy result.

## Native ABI and privacy review

- `AdapterProvider`'s sequential field order, one-byte native status flags, fixed UTF-16 arrays, 64-bit counters and x64 row stride agree with the inspected native declaration. The guard checks size 1352; offset 8 matches this x64 layout. The successful allocation is released in `finally`, including managed parsing failure. No demonstrated successful-path leak or field-layout defect was found. Windows documents native allocation ownership and alignment padding. [GetIfTable2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getiftable2)
- This is x64 host evidence only. `Is64BitProcess` is not an explicit X64 architecture check; ARM64 support should not be inferred from it. Native loading at line 18 is outside the provider catch, so the handoff promise that `ReadSnapshot()` never throws is too broad; the collector currently catches ordinary provider exceptions. `Read()` intentionally throws on typed failure. Parsing failure includes raw `ex.Message`, contrary to the handoff's unconditional sanitization claim. These are contract/documentation qualifications, not observed leaks.
- The manifest requests `asInvoker` and `uiAccess=false`. Inspected counter/route/taskbar paths use local APIs. No packet capture, outbound probes, helper installation or privileged IPC is implemented. No current cross-user app-history leak was demonstrated because no app-traffic collection/history path exists. Device/interface counters can include other sessions; future reporting must preserve that scope label (PRD 13.3).
- No network settings, routes, VPNs, adapter states, taskbar settings or Explorer processes were changed during this audit. No elevation was requested to make an unavailable experiment pass.

## Commands and observed evidence

Executed from the workspace:

```powershell
. .\scripts\common.ps1
& $dotnetPath run --project tests/SpeedMeter.Checks -c Release --no-restore -- --windows
```

Result: **69 checks passed; exit code 0.** This comprises 62 ordinary fixtures, four live Windows checks and three performance checks, not 69 purely deterministic tests.

- Native parity: 35 adapters compared against managed OS statistics; typed snapshot returned Available with 35 adapters.
- Route provider: four candidates, one default flag, selected Wi-Fi. This verifies one host enumeration, not dual-stack/VPN correctness.
- Taskbar query: unavailable in this session; only calculated fallback `(1704,984)` using hardcoded work area. No live docked behavior verified.
- Observed performance during this run: provider average 3.956 ms / 50 iterations; collector average 1.507 ms / 50 iterations; working set 27 MB, nine threads, 242 handles. These short host-dependent readings are not new soak-test evidence.
- `Get-NetRoute | Where-Object { $_.DestinationPrefix -in @('0.0.0.0/0', '::/0') } ...` returned **Access denied**. No independent OS route metric comparison was obtained. The following environment queries confirmed Windows NT 10.0.19045.0 and X64. This denial does not establish that every standard-user route API is unavailable.
- Source search for `ScopePolicy`, `RouteProvider`, `TaskbarCompanion` and `TaskbarCreated` confirmed absent application integration. Source search for network/trace/helper APIs found no implemented app-attribution trace path.

## Director repair order recommendation

1. Correct P0 gate/completeness/integration claims and leave P0 open; accept no V1 expansion based on these fixtures alone.
2. Make experimental scope validation and attribution capability fail honestly; preserve the working selected-interface prototype.
3. Build real route evidence and scope coordination with boundary/partial-state integration tests.
4. Harden and wire taskbar geometry, then obtain the chosen Windows 11 shell matrix evidence.
5. Complete a bounded attribution provider experiment and record provider-specific privilege/loss limitations before deciding on an optional helper.

Remaining Windows 11, real shell restart, adapter-cycle, VPN/virtual-switch and attribution evidence requires controlled lab work. An audit has identified these obligations; it has not silently waived them.
