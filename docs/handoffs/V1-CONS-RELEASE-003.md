# V1-CONS-RELEASE-003 — RC.4 release/build handoff

**Owner:** Release/Build specialist  
**Reviewers:** independent Security/Privacy and Project Director  
**Authority:** PRD §§24, 25.1–25.2; AC-S03; `docs/tasks/V1-CONS-RELEASE-003.md`  
**Status:** Script preparation and read-only audit complete; clean-source RC.4 package and native release drill pending source freeze. This handoff makes no Gate E2 decision.

## Implemented and files changed

- `scripts/package.ps1`: default package version is `1.0.0-rc.4`.
- `scripts/release.ps1`: RC.4 banner, explicit package/SBOM version arguments, release directory, Setup filename, and drill summary.
- `scripts/sbom.ps1`: default SBOM/notice version is `1.0.0-rc.4`.
- `docs/handoffs/V1-CONS-RELEASE-003.md`: this audit and release procedure.

The Director owns the linked `Directory.Build.props`, `ProductIdentity.cs`, lockfile and fixture version changes. `AssemblyVersion=1.0.0.0` and database schema 3 must remain stable. No RC.3 binary is relabelled RC.4.

## Package-output deletion audit

`package.ps1` restricts `$Version` to a filename-safe version grammar, resolves the release parent and child to full paths, and requires the child to remain beneath `artifacts/release`. `Remove-GuardedReleaseDirectory` checks absolute paths and dot segments, checks each existing ancestor for a reparse point, scans the selected release tree for reparse entries, and only then calls `Remove-Item -LiteralPath ... -Recurse -Force`. It is used for an existing version directory and `tmp_setup_build` after publishing. `release.ps1` uses `New-TestSandboxRoot` and `Remove-TestSandboxRoot`; the sentinel, direct GUID child of `%TEMP%\SpeedMeterPro.Tests`, and reparse checks are defined in `test-sandbox.ps1`. The release drill installs only to a child of this generated test root.

These checks give lexical containment and pre-delete reparse rejection. They do **not** make deletion atomic: another process with write access can change entries between the scan and recursive deletion. The same limitation applies to the test sandbox cleanup. No package or destructive drill was run during this audit. Security should review this remaining local mutable-filesystem risk before the Director accepts the release process. A package run must never be used to clean an uninspected pre-existing output tree.

## RC.3 size evidence and RC.4 expectation

Read-only measurements of existing local release artifacts:

| Artifact | RC.2 bytes | RC.3 bytes |
|---|---:|---:|
| Canonical Setup.exe | 217,575,696 | 217,586,960 |
| Self-contained app ZIP | 77,681,116 | 77,681,139 |
| Setup less app ZIP | 139,894,580 | 139,905,821 |
| Framework-dependent app ZIP | 1,729,557 | 1,729,579 |

The RC.3 self-contained app ZIP is 35.7% of Setup bytes. Its ZIP central directory has 486 entries. The largest entries include `System.Private.CoreLib.dll` (16,033,576 uncompressed bytes), `PresentationFramework.dll` (15,816,488), and `System.Windows.Forms.dll` (13,715,280). `SpeedMeter.Installer.csproj` embeds that ZIP as `payload.zip`; `package.ps1` publishes the installer self-contained and single-file with native libraries included. The installer itself is WPF (`UseWPF=true`), so a substantial runtime is packaged in addition to the compressed app payload. The 139,905,821-byte residual includes installer host, runtime, WPF/native dependencies, embedded-resource and signing overhead; exact byte attribution within that residual was not measured. RC.2 to RC.3 Setup grew only 11,264 bytes. Size alone is not a safety failure and does not justify replacing the installer architecture.

Record the RC.4 Setup, both ZIPs, entry count, and residual after a clean build. Do not infer RC.4 size from RC.3.

## Public GitHub transport and production trust

The public repository API currently reports no releases (`[]`), `has_pages=false`, and `has_downloads=false`; no public update asset or Pages manifest retrieval was observed. The client points to `https://abdullahalmamun-devv.github.io/NetSpeedMeter-Pro/updates/rc.json` and accepts a version-specific `github.com/.../releases/download/v<version>/...Setup.exe` package URL. `docs/UPDATE-OPERATIONS.md` describes the intended publication sequence. GitHub source push is not equivalent to hosting either endpoint.

**Status: ENVIRONMENT-PENDING — GitHub update publication.** A separately labelled non-GA prerelease asset and separate static test manifest can establish public HTTPS transport when publication is coordinated. Check public unauthenticated retrieval, exact asset length/hash, and manifest URL fields. A test manifest must not replace the production `updates/rc.json` channel. DevelopmentTest update mode is separately compiled and constrained to loopback; publishing a test manifest does not prove production-client trust. Production manifest signing authority and trusted Authenticode signing remain external dependencies; a test-signed RC.4 may support field validation but cannot be GA.

## Tests and observed results

- PowerShell parser: zero syntax errors for `package.ps1`, `release.ps1`, `sbom.ps1`, and `test-sandbox.ps1` after edits.
- RC.3 artifact size and ZIP-entry inspection: read-only, values above.
- GitHub repository/release metadata: read-only API inspection, values above.
- Package build, release drill, fresh RC.4 bytes/hash/signature, installed `ReleaseSourceSha`, and public transport retrieval: **not executed or observed** in this handoff.

## Precise RC.4 release procedure

1. Complete and independently review safety, QA and version changes. Commit the exact final RC.4 source; call that clean commit `RC4ReleaseSourceSha`. Check `git status --short --branch`, `git rev-parse HEAD`, `git rev-parse origin/main`, and the version triplet (`1.0.0-rc.4`, `1.0.0.4`, `1.0.0.0`) plus schema 3. Do not build a canonical candidate from a dirty tree or reuse an RC.3 binary.
2. Run `scripts/build.ps1`, `scripts/check.ps1`, `scripts/check.ps1 -Windows`, and `scripts/smoke.ps1`, recording executed fixture counts and native observations. These checks must pass before release packaging.
3. Inspect `artifacts/release/1.0.0-rc.4` and the planned `%TEMP%\SpeedMeterPro.Tests` drill boundary for existing content or reparse points. Coordinate any cleanup with the Director and Security. Then run `scripts/release.ps1 -SignMode Test` from clean committed source; it builds the package and performs the guarded silent install/uninstall drill. Never direct a drill at the current user's real product profile.
4. Record exact Setup filename, byte length, SHA-256, Authenticode status/signing mode, both ZIP sizes and contents, release hashes and SBOM. Inspect the installed DLL ProductVersion for the exact `RC4ReleaseSourceSha`; compare `RELEASE-HASHES.sha256` with the actual files. Ensure no generated legacy install/uninstall scripts or test assemblies are in the app ZIP.
5. Conduct the separately assigned disposable-profile native consumer, crash, privacy, update-consent, security and accessibility work. A script's silent sandbox drill cannot substitute for Windows Settings Installed Apps observation or independent reviews.
6. Write final evidence, commit and push normally, then verify `git status --short --branch`, local `HEAD`, `origin/main`, and `git ls-remote origin refs/heads/main` agree. The Director alone decides whether closure is verified. Do not start Gate E2 automatically.

## Known limitations and next handoff

The guarded PowerShell recursive deletes have a validation-to-delete race under a concurrently mutable local filesystem. RC.4 package and public transport results are unmeasured. Security should review the script boundary and the final installer helper; the Director should coordinate source freeze, clean release build, evidence commit and gate decision.
