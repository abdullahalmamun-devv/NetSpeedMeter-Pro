function Assert-TestSandboxRoot {
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.Path]::IsPathFullyQualified($Path)) { throw 'Test root must be absolute.' }
    if (($Path -split '[\\/]') -contains '..' -or ($Path -split '[\\/]') -contains '.') { throw 'Test root contains a dot segment.' }
    $root = [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\','/'))
    $parent = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'SpeedMeterPro.Tests')).TrimEnd([char[]]@('\','/'))
    if (-not [string]::Equals([System.IO.Path]::GetDirectoryName($root), $parent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test root is outside the approved temp parent.' }
    $id = [System.IO.Path]::GetFileName($root)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($id, 'N', [ref]$parsed)) { throw 'Test root lacks a GUID session identifier.' }
    $current = $root
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Test root traverses a reparse point.' }
        $next = [System.IO.Path]::GetDirectoryName($current)
        if (-not $next -or $next -eq $current) { break }
        $current = $next
    }
    $sentinel = Join-Path $root '.speedmeter-test-root'
    if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or
        [System.IO.File]::ReadAllText($sentinel) -cne "SpeedMeterPro.Tests:$id") { throw 'Test root sentinel is invalid.' }
    return $root
}

function New-TestSandboxRoot {
    $parent = Join-Path ([System.IO.Path]::GetTempPath()) 'SpeedMeterPro.Tests'
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $root = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $root '.speedmeter-test-root'), "SpeedMeterPro.Tests:$([System.IO.Path]::GetFileName($root))")
    return Assert-TestSandboxRoot $root
}

function Remove-TestSandboxRoot {
    param([Parameter(Mandatory)][string]$Path)
    $root = Assert-TestSandboxRoot $Path
    foreach ($entry in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Test tree contains a reparse point.' }
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}
