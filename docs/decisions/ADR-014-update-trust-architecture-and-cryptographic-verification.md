# ADR-014: Update Trust Architecture and Cryptographic Verification

## Status
Accepted (Wave 5 Release Engineering)

## Context
A major attack vector against desktop software is the delivery of malicious or corrupted update payloads. Per project security rules:
- **Absolute Blocker:** Unverified updater execution is strictly prohibited.
- **TLS is Not Sufficient:** Transport layer encryption (HTTPS) only protects transport transit; it does not protect against compromised CDN origins, man-in-the-middle proxy injection, or stale/corrupted disk caches.
- **Privacy Invariant:** When update checking is disabled, the product must initiate zero network requests. When enabled, update checking must disclose frequency and transmitted metadata.
- **Durability Invariant:** Database migrations triggered by updates must not destroy user data. Rollback must be predictable and safe.

## Trust Model & Architecture

```text
[Update Server]
      │
      │ 1. Download Manifest (HTTPS)
      ▼
[Local App: Staging]
      │
      ├─► 2. Verify Manifest Signature & Schema
      │        (Embedded Public Key / Certificate)
      ├─► 3. Check Channel & Minimum Supported Version
      │
      │ 4. Download Payload (HTTPS)
      ▼
[Quarantine Staging Directory]
      │
      ├─► 5. Compute SHA-256 Digest == Manifest Hash
      ├─► 6. Verify Authenticode Signature of Payload Binary
      ├─► 7. Create Pre-Migration SQLite Snapshot (.bak)
      │
      │ 8. User Approves / Restarts Application
      ▼
[Safe Atomic Extraction & Migration]
      │
      ├─► Success: Apply Schema Migration -> Mark Healthy
      └─► Failure: Restore Snapshot -> Rollback -> Surface Diagnostic
```

### 1. Update Manifest Specification
The update channel serves a structured, cryptographically signed JSON manifest (`update-manifest.json`):
```json
{
  "schemaVersion": 1,
  "product": "SpeedMeterPro",
  "channel": "stable",
  "version": "1.0.0",
  "buildDateUtc": "2026-10-12T00:00:00Z",
  "minSupportedVersion": "1.0.0-rc.1",
  "packageUrl": "https://updates.speedmeterpro.local/releases/v1.0.0/SpeedMeterPro-1.0.0-win-x64-Setup.exe",
  "packageSha256": "4a1c4f727dc9c0bb35bfe041df248955ab574b5b...",
  "packageSizeBytes": 75246259,
  "targetDbSchemaVersion": 3,
  "releaseNotesUrl": "https://updates.speedmeterpro.local/notes/v1.0.0.html",
  "publisher": "SpeedMeter Pro Project",
  "signature": "<Base64-RSA-SHA256-Signature>"
}
```

### 2. Multi-Stage Verification Pipeline
1. **Manifest Integrity:** The manifest signature is validated against the embedded publisher public key. Any tampering with version, URL, package hash, or schema metadata immediately fails verification.
2. **Path Traversal Resistance:** The package filename and target staging path are validated to prevent directory traversal attacks (e.g. `../../windows/system32/evil.exe`). Payload staging occurs strictly inside `%LocalAppData%\SpeedMeterPro\staging\`.
3. **Payload Checksum Verification:** After downloading to quarantine staging, the entire file is hashed with SHA-256 and compared strictly against `packageSha256`. A single flipped byte causes immediate deletion and rejection.
4. **Authenticode Signature Verification & Publisher Enforcement:** The staged binary is verified via Windows Authenticode and X509 certificate chain validation. Rejects unsigned binaries, tampered signatures, and publisher mismatches. Supports dual trust modes: `Production` (strict trusted public root CA chaining and RFC 3161 timestamping) and `DevelopmentTest` (explicit test certificate allowed for lab validation).
5. **Database Pre-Migration Snapshot (Live Online Backup):** Before replacing binaries or launching a newer schema version, an atomic SQLite backup (`speedmeter.pre-migration-<timestamp>.bak`) is generated using `SqliteConnection.BackupDatabase` (native SQLite C `sqlite3_backup_*` API). This guarantees 100% data fidelity even while background writers are actively committing to SQLite WAL mode.
6. **Downgrade Rejection:** A binary with a lower schema version is strictly blocked from opening a database that has already been upgraded to a higher schema (`user_version > CurrentSchemaVersion`), preventing corruption.
7. **Failure Recovery & Rollback:** If a post-update launch fails health checks (corrupt schema or unstartable collector), the pre-migration database snapshot is restored, deleting corrupted main DB and WAL/SHM artifacts and copying the backup snapshot back.

### 3. Privacy & Network Transparency
- **Default Cadence:** When update checking is enabled, the check runs at most once every 24 hours with an exponential backoff jitter on connection failure.
- **Transmitted Metadata:** Only standard HTTP headers (User-Agent containing application version and OS architecture). No analytics, user IDs, MAC addresses, IP tracking, or usage metrics are transmitted.
- **Zero-Network Invariant:** When `Settings.AutoUpdateCheckEnabled` is set to `false`, zero HTTP requests are initiated by the updater.

## Decision
1. Implement the multi-stage cryptographic update verification pipeline in `SpeedMeter.Application` (`UpdateVerificationService` and `UpdateTrustAnchor`).
2. Implement native `SqliteConnection.BackupDatabase` for pre-migration snapshots on live WAL databases.
3. Enforce deterministic canonical manifest bytes (`GetCanonicalManifestBytes`) and pinned RSA public key trust anchor.
4. Enforce Authenticode publisher matching and dual trust modes (`Production` vs `DevelopmentTest`).
5. Add deterministic test fixtures verifying:
   - Successful manifest and package validation.
   - Immediate rejection of tampered manifest properties (version, URL, hash).
   - Immediate rejection of corrupted package payload bytes.
   - Pre-migration SQLite snapshot creation and rollback on live WAL writer.
   - Downgrade prevention.
   - Authenticode publisher enforcement.

## Consequences
- No unverified executable can ever be staged or executed by SpeedMeter Pro.
- User database history is protected against botched updates through guaranteed pre-migration snapshots and rollback drills.
- Zero network traffic occurs when update checks are disabled.
