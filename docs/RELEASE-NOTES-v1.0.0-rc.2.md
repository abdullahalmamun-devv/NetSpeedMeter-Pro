# SpeedMeter Pro 1.0.0-rc.2 release candidate notes

RC.2 targets the final V1.0 consumer experience before Gate E2 field validation. It adds a graphical per-user Setup.exe and uninstall choice, a user-consented GitHub-hosted update check with signed manifest and verified Setup.exe flow, and local crash recovery with redacted report export. Core monitoring, history and quota remain local-first and usable offline.

Automatic update checks are off until enabled and are limited to approximately once a day. Manual checks are available in Settings. A package downloads only after **Update now**. Crash reports stay on the PC unless the user manually saves and shares one; **Report issue** opens a browser page with short version/build metadata.

The current production manifest key is provisional, the public Pages manifest and RC.2 Release asset require publication verification, and a publicly trusted Authenticode certificate remains pending. The produced test-signed RC.2 artifact is for field/lab use and is not a GA release. Gate E2 physical Windows 11 and long-run validation have not begun.

See [known issues](KNOWN-ISSUES.md), [privacy](PRIVACY.md), and [update operations](UPDATE-OPERATIONS.md) for current limits.
