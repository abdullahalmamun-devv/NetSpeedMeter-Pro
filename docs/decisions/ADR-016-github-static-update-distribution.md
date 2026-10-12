# ADR-016: GitHub static update distribution for V1

## Status
Accepted for RC.2 implementation; public hosting and production signing authority remain unverified external dependencies.

## Context
V1 must discover updates without an account, custom server, embedded token, or background request before consent. ADR-014 makes the embedded public key and package signature the trust anchors. GitHub is only a distribution service.

## Options

| Option | Evaluation |
|---|---|
| GitHub Pages static manifest | Preferred. Stable HTTPS URL, no client API credential, static `updates/rc.json` or `updates/stable.json` per channel. Pages must be enabled and publication verified before release. [GitHub Pages HTTPS documentation](https://docs.github.com/en/pages/getting-started-with-github-pages/securing-your-github-pages-site-with-https) |
| Raw GitHub-hosted file | Viable fallback for a signed manifest, but the URL follows repository branch/ref layout and is less clearly separated from source changes. [GitHub raw file documentation](https://docs.github.com/en/repositories/creating-and-managing-repositories/repository-limits) |
| GitHub Releases | Chosen for version-specific Setup.exe assets and release notes. The signed manifest points to an exact release asset URL, not a mutable `latest` URL. [GitHub release linking documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases) |
| GitHub REST API | Rejected for installed-client discovery: no need for token, API pagination, or API authentication behavior. |

## Decision
The RC channel manifest is intended at `https://abdullahalmamun-devv.github.io/NetSpeedMeter-Pro/updates/rc.json`. Stable uses a separate `updates/stable.json` after its own release gate. A signed manifest points to `https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/download/<immutable-tag>/<versioned-Setup.exe>`. Release notes are displayed only after manifest signature verification and opened as a safe HTTPS URL. The installed app always verifies canonical manifest bytes with the pinned **Production** key, then package size, SHA-256, native WinVerifyTrust and publisher before installer launch. DevelopmentTest is limited to internal fixtures.

Automatic manifest requests require an explicit opt-in and occur at most once per 24 hours after a successful check. Manual checks are a direct user action. Downloads are user initiated, asynchronous, streamed, cancellable and bounded. Network errors do not affect monitoring.

## Key provisioning and publication
The repository's production public PEM is provisional and 2048 bit; no final external production private key has been provisioned here. The operator must provision protected private signing material outside the repository, provide its path to an offline manifest-signing tool, replace and independently review the pinned public key before a GA release, and publish only the signed public JSON to GitHub Pages. Never commit a private key or silently generate one in the release pipeline. A production Authenticode certificate is also pending. These are `EXTERNAL-DEPENDENCY-PENDING` and block GA claims.

Publication of Pages and the RC.2 GitHub Release asset is an external operation; the local implementation or a test-signed installer does not prove that either public URL is live.
