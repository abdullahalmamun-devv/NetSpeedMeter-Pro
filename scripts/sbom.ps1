param(
    [string]$Version = '1.0.0-rc.1'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host "=== Generating Software Bill of Materials (SBOM) and Third-Party Notices ===" -ForegroundColor Cyan

$releaseDir = Join-Path $projectRoot 'artifacts\release'
if (-not (Test-Path -LiteralPath $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

$spdxJsonPath = Join-Path $releaseDir 'sbom.spdx.json'
$noticesRootPath = Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'
$noticesReleasePath = Join-Path $releaseDir 'THIRD-PARTY-NOTICES.md'

$creationDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Catalog of direct and transitive dependencies
$dependencies = @(
    @{
        Name = "Microsoft.Data.Sqlite"
        Version = "9.0.2"
        License = "MIT"
        Supplier = "Organization: Microsoft Corporation"
        Homepage = "https://learn.microsoft.com/dotnet/standard/data/sqlite/"
        Copyright = "Copyright (c) Microsoft Corporation. All rights reserved."
        Notice = @"
MIT License

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@
    },
    @{
        Name = "Microsoft.Data.Sqlite.Core"
        Version = "9.0.2"
        License = "MIT"
        Supplier = "Organization: Microsoft Corporation"
        Homepage = "https://learn.microsoft.com/dotnet/standard/data/sqlite/"
        Copyright = "Copyright (c) Microsoft Corporation. All rights reserved."
        Notice = "MIT License - Copyright (c) Microsoft Corporation. All rights reserved."
    },
    @{
        Name = "SQLitePCLRaw.bundle_e_sqlite3"
        Version = "2.1.12"
        License = "Apache-2.0"
        Supplier = "Person: Eric Sink"
        Homepage = "https://github.com/ericsink/SQLitePCL.raw"
        Copyright = "Copyright (c) 2014-2024 SourceGear LLC"
        Notice = @"
Apache License, Version 2.0

Copyright (c) 2014-2024 SourceGear LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
"@
    },
    @{
        Name = "SQLitePCLRaw.core"
        Version = "2.1.12"
        License = "Apache-2.0"
        Supplier = "Person: Eric Sink"
        Homepage = "https://github.com/ericsink/SQLitePCL.raw"
        Copyright = "Copyright (c) 2014-2024 SourceGear LLC"
        Notice = "Apache-2.0 License - Copyright (c) 2014-2024 SourceGear LLC"
    },
    @{
        Name = "SQLitePCLRaw.provider.e_sqlite3"
        Version = "2.1.12"
        License = "Apache-2.0"
        Supplier = "Person: Eric Sink"
        Homepage = "https://github.com/ericsink/SQLitePCL.raw"
        Copyright = "Copyright (c) 2014-2024 SourceGear LLC"
        Notice = "Apache-2.0 License - Copyright (c) 2014-2024 SourceGear LLC"
    },
    @{
        Name = "SQLitePCLRaw.lib.e_sqlite3"
        Version = "2.1.12"
        License = "Apache-2.0"
        Supplier = "Person: Eric Sink"
        Homepage = "https://github.com/ericsink/SQLitePCL.raw"
        Copyright = "Copyright (c) 2014-2024 SourceGear LLC; SQLite is in the Public Domain"
        Notice = "Apache-2.0 License - Copyright (c) 2014-2024 SourceGear LLC"
    },
    @{
        Name = ".NET Runtime and Windows Desktop Component Libraries"
        Version = "10.0.12"
        License = "MIT"
        Supplier = "Organization: .NET Foundation and Contributors"
        Homepage = "https://github.com/dotnet/runtime"
        Copyright = "Copyright (c) .NET Foundation and Contributors. All rights reserved."
        Notice = "MIT License - Copyright (c) .NET Foundation and Contributors."
    }
)

# 1. Generate SPDX 2.3 JSON Document
$spdxPackages = @()
$spdxRelationships = @()

$rootPackageId = "SPDXRef-Package-SpeedMeterPro"
$spdxPackages += @{
    SPDXID = $rootPackageId
    name = "SpeedMeter Pro"
    versionInfo = $Version
    downloadLocation = "NOASSERTION"
    filesAnalyzed = $false
    licenseConcluded = "NOASSERTION"
    licenseDeclared = "NOASSERTION"
    copyrightText = "Copyright (c) SpeedMeter Pro Contributors"
    description = "Precision Network Performance Monitor for Windows"
}

foreach ($dep in $dependencies) {
    $depId = "SPDXRef-Package-$($dep.Name.Replace('.', '-').Replace(' ', '-'))"
    $spdxPackages += @{
        SPDXID = $depId
        name = $dep.Name
        versionInfo = $dep.Version
        downloadLocation = "NOASSERTION"
        filesAnalyzed = $false
        supplier = $dep.Supplier
        homepage = $dep.Homepage
        licenseConcluded = $dep.License
        licenseDeclared = $dep.License
        copyrightText = $dep.Copyright
    }
    $spdxRelationships += @{
        spdxElementId = $rootPackageId
        relationshipType = "DEPENDS_ON"
        relatedSpdxElement = $depId
    }
}

$spdxDoc = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "SpeedMeterPro-$Version"
    documentNamespace = "https://speedmeterpro.local/spdx/$Version"
    creationInfo = [ordered]@{
        created = $creationDate
        creators = @(
            "Tool: SpeedMeter-SBOM-Generator-1.0",
            "Organization: SpeedMeter Pro Team"
        )
    }
    documentDescribes = @($rootPackageId)
    packages = $spdxPackages
    relationships = $spdxRelationships
}

$jsonOutput = $spdxDoc | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($spdxJsonPath, $jsonOutput, [System.Text.Encoding]::UTF8)
Write-Host "SPDX 2.3 SBOM generated at: $spdxJsonPath" -ForegroundColor Green

# 2. Generate THIRD-PARTY-NOTICES.md
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# Third-Party Software Notices and Information")
[void]$sb.AppendLine()
[void]$sb.AppendLine("SpeedMeter Pro incorporates components from the open-source software listed below.")
[void]$sb.AppendLine("The SpeedMeter Pro project gratefully acknowledges these open source contributions.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()

foreach ($dep in $dependencies) {
    [void]$sb.AppendLine("## $($dep.Name) (Version $($dep.Version))")
    [void]$sb.AppendLine("- **Supplier:** $($dep.Supplier)")
    [void]$sb.AppendLine("- **License:** $($dep.License)")
    [void]$sb.AppendLine("- **Homepage:** $($dep.Homepage)")
    [void]$sb.AppendLine("- **Copyright:** $($dep.Copyright)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine($dep.Notice.Trim())
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine()
}

$noticesContent = $sb.ToString()
[System.IO.File]::WriteAllText($noticesRootPath, $noticesContent, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText($noticesReleasePath, $noticesContent, [System.Text.Encoding]::UTF8)
Write-Host "Third-Party Notices generated at: $noticesRootPath and $noticesReleasePath" -ForegroundColor Green
