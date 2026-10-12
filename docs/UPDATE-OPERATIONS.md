# Update publication operations (RC.2)

The public RC manifest is intended for GitHub Pages at
`https://abdullahalmamun-devv.github.io/NetSpeedMeter-Pro/updates/rc.json`.
The stable channel has a separate `updates/stable.json`. The manifest points to a version-specific GitHub Release Setup.exe URL. The desktop client has no token and does not call the GitHub REST API.

## Required external material

- A protected **production manifest private key** provisioned by the release operator outside this repository and build artifacts. The current embedded production public key is provisional. Replace it with the provisioned matching public key, independently review that change, and rebuild the client before publishing a production manifest.
- A trusted production Authenticode certificate and timestamp service for the Setup.exe. The local test-sign mode is only a lab/field RC path.

Never copy private signing material into source, test fixtures, an artifact directory, GitHub, or an installer. Do not publish a manifest until its exact Setup.exe exists and has passed the full trusted signature/publisher check.

## Signing and publishing

Use PowerShell 7 or later. Supply the external private key path explicitly:

```powershell
pwsh ./scripts/create-update-manifest.ps1 `
  -PrivateKeyPath '<protected external PEM path>' `
  -PackagePath './artifacts/release/1.0.0-rc.2/SpeedMeterPro-1.0.0-rc.2-Setup.exe' `
  -PackageUrl 'https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/download/v1.0.0-rc.2/SpeedMeterPro-1.0.0-rc.2-Setup.exe' `
  -ReleaseNotesUrl 'https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/tag/v1.0.0-rc.2' `
  -Version '1.0.0-rc.2' -MinSupportedVersion '1.0.0-rc.1' `
  -Channel rc -TargetDbSchemaVersion 3 -OutputPath './artifacts/release/1.0.0-rc.2/rc.json'
```

The script serializes the same canonical fields as `UpdateVerificationService`, hashes the exact package bytes, signs with RSA-SHA256 and writes only the public JSON. After signing, verify the manifest using the exact production client binary, compare the package SHA-256 and length against release evidence, and confirm WinVerifyTrust, publisher, and version/channel policy. Upload the Setup.exe as a versioned GitHub Release asset, then publish the signed JSON to the Pages path. Inspect both public URLs over HTTPS. Do not replace RC.1 evidence.

The application does not silently download large packages. Users consent separately to daily automatic checks and press Update now to download and install. A failed or offline check leaves core monitoring running. This publication workflow is `EXTERNAL-DEPENDENCY-PENDING` until the real keys, certificate, URLs and end-to-end production check are observed.
