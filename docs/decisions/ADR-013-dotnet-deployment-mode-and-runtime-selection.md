# ADR-013: .NET Deployment Mode and Runtime Selection

## Status
Accepted (Wave 5 Release Engineering)

## Context
SpeedMeter Pro is built on supported .NET 10 LTS with WPF on Windows x64. When producing distribution artifacts, we must select the deployment model that best balances user installation friction, download size, startup latency, and runtime servicing.

We evaluated two realistic deployment candidates and one alternative:
1. **Framework-Dependent (`win-x64`)**: Relies on a machine-wide .NET 10 Desktop Runtime installation.
2. **Self-Contained (`win-x64`)**: Bundles the .NET 10 CoreCLR runtime, base class libraries, and WPF native components with the application binaries.
3. **Native AOT (`win-x64`)**: Compiles directly to native machine code ahead-of-time.

## Empirical Benchmark & Measurements

Measurements were performed on the authoritative codebase using the pinned .NET 10 SDK (10.0.401):

| Metric | Framework-Dependent (`win-x64`) | Self-Contained (`win-x64`) | Ratio / Delta |
|---|---|---|---|
| Published Files | 18 files | 265 files | +247 files |
| Total Payload Size | 3,302,173 bytes (~3.15 MB) | 182,566,998 bytes (~174.11 MB) | 55.3x |
| Compressed Package Size (ZIP) | 1,630,560 bytes (~1.55 MB) | 75,246,259 bytes (~71.76 MB) | 46.1x |
| Runtime Prerequisite | .NET 10 Desktop Runtime required | None (zero prerequisites) | Completely standalone |
| Fresh Machine Launch | **Failed without pre-installed runtime**<br>(Windows dialog prompt) | **Instant launch (Exit code 0)**<br>(18/18 smoke assertions True) | Frictionless |
| Cold Startup to Valid Rate | ~1.4 seconds (when runtime present) | ~1.6 seconds | +0.2s |
| Memory Footprint (Working Set) | ~155 MB | ~158 MB | +3 MB (negligible) |

## Evaluation

### Framework-Dependent Evaluation
- **Pros:** Extremely compact download (1.55 MB compressed), rapid publish time, OS-level runtime security patches apply automatically.
- **Cons:** Fatal consumer friction. Standard Windows 10/11 machines do not ship with .NET 10 Desktop Runtime out of the box. Running the application host executable on a machine without .NET 10 prompts the user to download a runtime installer from Microsoft, creating an immediate barrier for standard users who may lack administrator permissions or technical familiarity.

### Self-Contained Evaluation
- **Pros:** The application runs out of the box on any standard Windows 10/11 x64 system. Zero runtime installation prompts, zero elevation prompts, and guaranteed runtime version consistency. The compressed download size of ~71 MB is well within standard broadband expectations for a high-performance desktop telemetry suite.
- **Cons:** Larger initial download and installer package size. Requires SpeedMeter Pro releases to ship .NET runtime servicing updates when upstream LTS patches occur.

### Native AOT Evaluation
- **Decision:** **Strictly rejected for V1.** While Native AOT offers smaller package sizes and faster JIT-free startup, .NET 10 WPF does not support full Native AOT without experimental trim warnings, reflection disabling, and risk to WPF dynamic styling, XAML compilation, and COM interop. Switching to Native AOT would violate the core project principle of preserving proven, feature-complete code.

## Decision
1. **Primary Public Distribution:** Select **`win-x64 self-contained`** as the default production distribution model.
   - Guaranteed immediate execution on standard user machines without runtime prompts.
   - Fully compatible with standard-user installation into `%LocalAppData%\Programs\SpeedMeterPro`.
2. **Optional Managed Channel:** Provide **`win-x64 framework-dependent`** as a secondary option in the release evidence and advanced downloads for enterprise administrators with fleet-managed .NET 10 Desktop Runtimes.
3. **Runtime Model Preservation:** Retain standard CoreCLR JIT compilation; no Native AOT or unsafe aggressive trimming.

## Consequences
- The public installer package size will be approximately 72–75 MB compressed, unpacking to ~175 MB on disk.
- Users can install and launch SpeedMeter Pro with zero external prerequisites on any supported Windows x64 machine.
- Security updates to the .NET 10 runtime will be delivered through standard SpeedMeter Pro application updates.
