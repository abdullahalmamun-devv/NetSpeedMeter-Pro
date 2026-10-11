# Open product decisions and scope clarifications — P0 audit correction

Date: 11 October 2026 (Asia/Dhaka). Owner: Auditor A/J; reviewer: Audit Director + QA. Authority: PRD revision 2.0 sections 3–4, 30.2–30.3. Status: **OPEN — P0 audit and feasibility evidence pending**.

The previous register reused D02/D05/D14 for counter/storage/app decisions and misidentified F63 as portable. Those statements are superseded; the original mismatch is preserved in [requirements audit](../audit/REQUIREMENTS-ADR-AUDIT.md), with historical handoffs/reviews retained. PRD decision IDs may not be repurposed. A code fixture or documentation proposal does not resolve a product gate.

## Original PRD decision register

| ID | Original decision | Proposed default / gate | Current status and owner |
|---|---|---|---|
| D01 | Final brand/name | Working name SpeedMeter Pro; before public beta branding | Open; product owner |
| D02 | Supported Windows builds/editions | Supported Windows 11 x64 matrix; P0 exit and each release | Open; Release + Windows QA. PRD 3.3 asks for a Windows 11 25H2 reference lane. Observed Windows 10 19045 is developer evidence only; no product port approval |
| D03 | Docked/taskbar implementation and wording | Safe companion with floating fallback; P0 exit | Open; Windows + Product. Standalone geometry spike and floating UI exist; App docking/Explorer recovery and target build validation pending |
| D04 | Runtime/UI stack | Supported .NET 10 LTS + WPF; P0 exit | Stack choice supported by source (ADR-001); Release must verify target supported patch/OS and resource results |
| D05 | Per-app provider and privileges | ETW candidate; optional signed helper if required; P0 feasibility, final before V1.5 | Open; Research + Windows + Security. PID/time fixture and role query are not event capture, completeness or least-privilege evidence; no helper authorized |
| D06 | Approved probe/IP endpoints | Documented low-rate approved endpoints and manual target; before V1 remote beta | Open; Product + Security. No endpoint service/request implemented |
| D07 | Signing/update/distribution provider | Signed installer and verified update channel; before V1 beta | Open; Release + Security |
| D08 | Speed-test provider/backend | Terms/cost/geography/quality approval; before V2 speed-test implementation | Open; Product + Research |
| D09 | Pricing/trial/license device rules | No engineering restrictions; validate local Pro proposal; before paid launch | Open; product owner |
| D10 | Support staffing/route/response target | Real staffed route; before public launch | Open; Product + Support |
| D11 | Optional score calibration | Off until validated; V2 review | Open; Product + QA |
| D12 | Cloud/business case | Off; independent opt-in extension; before hosted implementation | Open; product owner |
| D13 | Windows 10/ARM64 compatibility | Separate conditional ports; before advertising each | Open; Release + Windows. Local Windows 10 execution does not authorize a port |
| D14 | Final resource targets | Section 23 benchmarked proposals; after P0 and each release | Open; Performance + Product. Prior burst latency/memory observations do not establish CPU/private-working-set/soak targets |

## Separate engineering choices

| Engineering reference | Choice | Status |
|---|---|---|
| ENG-COUNTERS | IP Helper GetIfTable2 provider | Real prototype; ADR-002 reopened for lifecycle/conformance evidence |
| ENG-SCOPE | Single selected accounting boundary, experimental scope policy | Production manual scope exists; route/aggregate/VPN algorithm remains gated under PRD 8, not a replacement D02 |
| ENG-STORAGE | SQLite single writer/WAL/exact ledger | Proposed design only; ADR-005 reopened, no storage implementation. FULL baseline and checked integer/provenance/recovery requirements recorded |
| ENG-RECOVERY | Widget click-through independent recovery | Requires actual native interaction evidence and accessible tray route; not a replacement D05 |

## Probe windows and sufficient samples

PRD 14.1 offers 1/2/5/10-second quality probe intervals. Section 14.4 requires 20 completed attempts for loss-based classification in the default 60-second window; a 5-second cadence supplies about 12 attempts and a 10-second cadence about six. The optional score in section 14.5 requires 60 completed attempts in five minutes; a 10-second cadence supplies about 30. Section 15.2 V1 mean-ping alert requires ten successful replies in 30 seconds; 5/10-second intervals cannot reliably satisfy it. These are feasibility/product consistency questions, not existing quality implementation failures.

Status: unresolved. Owner: Product + Measurement; resolve before each affected monitor/alert is built. Possible decisions are compatible setting/window validation, visibly enlarged windows or explicitly unavailable assessments. Do not silently change count minimums or call a five-minute score minimum a jitter requirement. Record affected feature/AC and wording before implementation. Relevant cases include AC-Q01/Q03/Q05/Q06 and AC-N04.

## Time-bound network membership

PRD 8.4, 11.3 and 22 require stable local profile mapping and time-bound membership. One interface may change networks without changing adapter identity. Revised storage draft records immutable scope/membership and per-interval profile, plus unresolved identity; multi-network aggregate attribution and transitions remain reviewed design prerequisites. Owner: Storage + Measurement. No historical data is currently persisted.

## Features without explicit register Must/Should tokens

F26, F56 and F63 reproduce their PRD release qualifications without an invented priority token. Detailed behaviours remain required as described; priority clarification belongs to the product owner before affected release planning.

- F26: manual public-IP lookup/copy in V1, optional automatic change/history in V1.5; requests obey PRD 16.2/24 enablement.
- F56: V1 localization infrastructure/resource-based strings per 18.4; reviewed English/Bengali UI in V2. Bengali input/export encoding is independently required by AC-U07.
- F63: basic diagnostic report in V1, expanded report in V2, with redaction preview (16.3–16.4, AC-D06). Portable package is F45, V1 Should, with its own declared capabilities and AC-W09.

No Must feature is deferred by this register. No new feature is authorized by this audit correction.
