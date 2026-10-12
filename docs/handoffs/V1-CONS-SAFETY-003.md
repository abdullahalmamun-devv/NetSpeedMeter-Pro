# V1-CONS-SAFETY-003 — Installer safety handoff

**Owner:** Windows Installer specialist  
**Reviewers:** Independent Security/Privacy, QA, Director  
**Authority:** PRD §§24, 25.1–25.2; AC-S03, AC-S07  
**Dependency:** Gate E1; RC.3 public baseline `b5f7ffe`  
**Gate:** Consumer safety closure remains open pending independent review and native packaged drill. Gate E2 is not authorized.

## Scope and design

- Removed the deferred `cmd.exe`/batch `rd /s /q` uninstall path and its CMD-specific path character rejection.
- An installed `Uninstall.exe` copies its exact bytes to a unique `%TEMP%\SpeedMeterPro.Cleanup\<nonce>` directory, verifies SHA-256 equality, and starts the copy in private `-CleanupExecutor` mode. The parent and helper prove each other's live process IDs through a current-user named pipe; the helper also checks the parent's process start time and waits for termination.
- The helper request is size-bounded and binds target path, original executable, SHA-256, parent PID/start time, nonce, and original command line. It is accepted only from the expected product temp location and a marked product installation. Validation runs again after the parent exits, just before uninstall mutation. The engine checks the marker again immediately before moving application files.
- Unmarked legacy canonical directories are now refused by uninstall validation; this avoids deleting a merely lookalike directory and requires a separate repair path if such an old installation is encountered.
- The helper runs the normal GUI or silent uninstaller outside the installation, allowing synchronous completion/failure reporting. Its result file records `Pending`, `Completed`, or `Failed` for the exact normalized target. A packaged drill must wait for `Completed`, confirm the target is absent, and confirm no quarantine sibling remains.
- Closing the initial GUI is exit code 1602 (canceled), so the helper result cannot claim `Completed` for a canceled uninstall.
- Purge and program deletion use same-parent quarantine moves followed by checked, non-recursive traversal. Every traversed item is checked for reparse attributes and containment. A failed purge still removes the app but reports that some local data could not be deleted. A failed binary cleanup reports an incomplete uninstall.
- The copied helper tries immediate self-deletion at process exit, then native delete-on-reboot scheduling. A later uninstall prunes only stale, GUID-named, non-running helper directories with expected files and no reparse points. The bounded `cleanup.log` records when cleanup scheduling fails.
- Helper result files are keyed by target-path SHA-256 (one per target) and contain only `TargetHash`, nonce, state, message and timestamp for release evidence, avoiding a persistent profile path. They are pruned after 30 days on a later uninstall if their names match the exact product pattern and they are not reparse points. They can remain in `%TEMP%` until that cleanup or normal Windows temp maintenance.

## Validation to date

- Final installer project Release build: succeeded with zero warnings and zero errors.
- QA's final standard fixture runner: **281 passed, 1 skipped, 0 failed**. Windows fixture runner: **288 passed, 1 skipped, 1 failed**; the sole failure is the native WLAN SSID assertion, outside this installer scope. All installer/safety fixtures passed in both runs, including a real junction fallback for reparse rejection, metacharacter paths, request rejection, sentinelled purge and locked-file residual reporting. The single intentional skip is outside these 13 consumer installer/safety checks. A normal restore attempt stopped at NU1900 because the NuGet vulnerability feed was unavailable; the offline runs used `NuGetAudit=false` and did not provide a fresh vulnerability audit.
- No real profile purge or Gate E2 activity was performed by this specialist.

## Remaining evidence and limits

- The final QA runs executed the positive sentinelled purge and rejected stale-parent request. The native WLAN SSID failure remains under separate ownership.
- Release Engineering must run a packaged, sentinelled native uninstall and wait for helper result and complete quarantine removal. Parent process exit code alone is insufficient evidence.
- Independent Security/Privacy review must decide acceptance; the Director alone can declare consumer closure.
- Checked managed traversal narrows but cannot eliminate a malicious same-user mutation race without handle-relative Windows APIs. If a reparse substitution is detected, cleanup stops and can leave a named quarantine directory. This is reported as incomplete.
- Package/test cleanup scripts still contain recursive filesystem deletion outside the production installer engine; their sentinel and path guards need continued review against same-user mutation during native drills.
- A signed temporary helper may remain until reboot or a later cleanup if Windows denies both immediate and delayed self-deletion. This is a known residual requiring native observation; the request and product data are not placed in that helper directory.
