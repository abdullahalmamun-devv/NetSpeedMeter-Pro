# RC.2 consumer source integration evidence

**Date:** 12 October 2026 (Asia/Dhaka)
**Baseline:** public/local `main` `37b5f57a086fcb9d11ae248e136b1c20f6ff78af` (confirmed through GitHub repository connector after local `git ls-remote` could not connect).
**Version model:** Product `1.0.0-rc.2`, file `1.0.0.2`, assembly `1.0.0.0`, DB schema `3`, channel `rc`.
**Gate:** RC.2 source integration evidence only. Consumer closure and Gate E2 remain unpassed.

## Observed results

| Check | Observed result |
|---|---|
| `scripts/build.ps1` | Initial restore failed `NU1900` because `api.nuget.org` could not be reached for the vulnerability feed. Offline local rerun with `NuGetAudit=false`: 7 projects, 0 warnings, 0 errors. Package vulnerability freshness is not evidenced. |
| `scripts/check.ps1` | 268 PASS, 1 intentional SKIP, 0 FAIL, 269 evaluated, exit 0 under normal user access. Earlier sandbox run denied LocalAppData lock creation; an initial fixture bug was found and fixed before the passing rerun. |
| `scripts/check.ps1 -Windows` | 276 PASS, 1 intentional SKIP, 0 FAIL, 277 evaluated, exit 0 on Windows build 19045. |
| `scripts/smoke.ps1` | Exit 0; live collector readings and all named WPF navigation/widget/tray assertions True; artifact `artifacts/smoke-20261012-065004`. |

The existing Wave 5 installer fixture previously pointed a sandbox install to the real user data directory on purge. A live database lock prevented deletion during the observed run. It has been corrected to pass a dedicated temporary user-data directory and to omit shell registration for noncanonical sandbox targets. No further purge drill was run against real account data. The current user data was not inspected for content changes before that run; no unaffected-data claim is made.

## Independent review

- Security/Privacy review: source defects in installer post-commit launch rollback, staged file growth, and verification-to-launch race were repaired. Review remains CHANGES REQUESTED pending actual production-path updater, installer and crash drills and external trust material.
- Accessibility/Product review: source layout, labels and live-state announcement fixes were applied after review; native keyboard/Narrator, 150% DPI, enlarged text and High Contrast observations remain pending. Review remains CHANGES REQUESTED.

## Unverified acceptance

- Public GitHub Pages manifest and Release asset live URLs.
- Production manifest signing authority and trusted Authenticode certificate.
- RC.1 → RC.2 update via the installed app's actual Production trust path, graceful restart, and post-update health/data-preservation matrix.
- Physical interactive Windows Apps uninstall/purge/reinstall and controlled UI/background crash recovery UX with Narrator and display scaling.
- Zero external traffic observation with all optional request features disabled.
- Windows 11 field matrix and long-duration Gate E2 work (intentionally not started).

These are recorded as `EXTERNAL-DEPENDENCY-PENDING` or `ENVIRONMENT-PENDING` where appropriate. They are not silently converted into passing claims by the fixture results.
