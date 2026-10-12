# V1-CONS-UPDATE-001 handoff

Owner: Update/Application + UI engineer. Reviewer: Security engineer and Project Director. PRD: F54, §§24–25 and AC-S03. Dependency: Gate E1 and ADR-014. Scope: V1.0 RC.2 consumer update discovery and install flow.

## Implementation

- `UpdateService` uses the static GitHub Pages RC manifest URL, with no GitHub API or account requirement. Manual checks are explicit. Automatic checks require `AutoUpdateCheckEnabled`, which defaults false, and use a persisted 24-hour successful-check cadence with a one-hour retry backoff.
- The service parses a bounded manifest, verifies its RSA signature against the production pinned key, enforces SemVer and channel policy, and requires a version-pinned GitHub Release Setup.exe URL. Downloads occur only on Update now, stream to a unique quarantine file with a 512 MiB size cap and 15-minute timeout, report byte progress, and support cancellation. SHA-256, WinVerifyTrust, publisher, and schema policy are checked before promoting to a ready installer. A schema-increasing update requires an online SQLite snapshot path; otherwise it fails closed.
- Install holds a read handle that denies write/delete sharing across final size/hash and Authenticode verification and `Process.Start`, then launches the canonical Setup.exe with `-Silent -LaunchAfterInstall`. The installer owns graceful shutdown and relaunch. The update state reports installer launch, not confirmed installation success. Its staged file remains available after application exit. A native Windows launch rehearsal is needed to confirm WinVerifyTrust and process creation both work under this sharing mode.
- First run and Settings disclose GitHub contact and offer opt-in consent. Settings has manual check, state, version/channel, download progress, Update now, Later, release notes, and cancellation. Release notes open only from a verified manifest.
- Manifest canonical bytes now include `ReleaseNotesUrl`; the production key cannot be replaced through the development test override. The release signer must use the revised canonical field order.

## Acceptance and evidence

- Dedicated executable fixtures: `ConsumerUpdateFixtures.Run(Check)` covers zero requests without consent, cadence suppression, manual malformed manifest rejection, SemVer ordering, signed release notes and production key separation, offline behavior, signed newer/current/older version results, tampered manifest rejection, package hash/size failures, partial download cancellation, ready-state retention across a recheck, and orphan staging cleanup on restart. Director registered the fixture in the executable runner.
- Integrated build, fixture runner, native Windows package verification, live GitHub Pages availability, and RC.1→RC.2 update rehearsal remain Director/reviewer evidence gates. No GA key or verified live manifest exists in this handoff.

## Wiring and limitations

- `SettingsViewModel` creates `UpdateService` and starts a consent-gated automatic check at construction. The first-run settings save event triggers a check after opt-in. There is no need for a second updater startup hook in `App.xaml.cs`.
- The static Pages URL is `https://abdullahalmamun-devv.github.io/NetSpeedMeter-Pro/updates/rc.json`; publishing and reachability are unverified. The production manifest public key is currently a provisional 2048-bit key. Provision a protected external production private key and pin its public counterpart before a real production feed is trusted. No development trust mode is exposed in the consumer UI.
- `UpdateService` accepts internal HTTP/URI, DevelopmentTest trust, and fake Authenticode seams for deterministic fixtures; the public consumer constructor always uses the fixed Pages URL, production trust, and native Authenticode. The test seams are not available through Settings or the public constructor.
- For a future schema-changing update, pass the live database path through application composition or extend the service construction. With no path, schema-increasing packages are rejected. RC.1→RC.2 keeps DB schema 3.
- Metered-network suppression for downloads and dashboard banner are still open; Settings offers non-destructive update state and explicit user initiation. These should be resolved before declaring the feature Done. Actual installed success cannot be inferred from successful installer launch; installer completion/restart needs independent RC rehearsal.
