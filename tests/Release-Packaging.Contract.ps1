param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'scripts\Release-Packaging.Core.psm1'
$scratch = Join-Path $repoRoot 'outputs\release-packaging-contract'

try {
    Import-Module -Name $modulePath -Force

    $validVersions = [ordered]@{
        'v0.0.0' = '0.0.0'
        'v1.1.0' = '1.1.0'
        'v10.20.30-rc.1' = '10.20.30-rc.1'
        'v1.2.3-01alpha' = '1.2.3-01alpha'
    }
    foreach ($entry in $validVersions.GetEnumerator()) {
        $actual = ConvertFrom-ReleaseTag -Tag $entry.Key
        if ($actual -cne $entry.Value) {
            throw "Release tag resolved incorrectly: $($entry.Key) -> $actual"
        }
    }

    $invalidTags = @(
        '1.2.3'
        'v1.2'
        'v01.2.3'
        'v1.02.3'
        'v1.2.03'
        'v1.2.3-'
        'v1.2.3-.'
        'v1.2.3-rc.01'
        'v1.2.3+build.1'
    )
    foreach ($tag in $invalidTags) {
        $rejected = $false
        try {
            $null = ConvertFrom-ReleaseTag -Tag $tag
        }
        catch {
            $rejected = $true
        }
        if (-not $rejected) {
            throw "Invalid SemVer release tag was accepted: $tag"
        }
    }

    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $source = Join-Path $scratch 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    $unicodeEntryName = (-join ([char[]](0x4F73, 0x6021))) + '.txt'
    $entries = @('z-last.txt', 'a-first.txt', $unicodeEntryName)
    foreach ($name in $entries) {
        [IO.File]::WriteAllText(
            (Join-Path $source $name),
            "release-contract:$name",
            [Text.UTF8Encoding]::new($false))
    }

    $nestedDirectory = Join-Path $source 'nested'
    New-Item -ItemType Directory -Path $nestedDirectory | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $nestedDirectory 'file.txt'),
        'release-contract:nested',
        [Text.UTF8Encoding]::new($false))

    $unsafeEntryCases = [ordered]@{
        'duplicate entry name' = @('a-first.txt', 'a-first.txt')
        'empty entry name' = @('')
        'current-directory entry name' = @('.')
        'parent-directory entry name' = @('..')
        'forward-slash entry name' = @('nested/file.txt')
        'backslash entry name' = @('nested\file.txt')
        'rooted entry name' = @((Join-Path $source 'a-first.txt'))
        'invalid leaf entry name' = @('bad:name.txt')
    }
    $unsafeCaseIndex = 0
    foreach ($case in $unsafeEntryCases.GetEnumerator()) {
        $unsafeZip = Join-Path $scratch "unsafe-$unsafeCaseIndex.zip"
        $unsafeCaseIndex++
        $rejected = $false
        try {
            New-DeterministicReleaseZip `
                -SourceDirectory $source `
                -EntryNames $case.Value `
                -DestinationPath $unsafeZip
        }
        catch {
            $rejected = $true
        }
        if (-not $rejected) {
            throw "Unsafe release ZIP case was accepted: $($case.Key)"
        }
        if (Test-Path -LiteralPath $unsafeZip) {
            throw "Unsafe release ZIP case created a destination: $($case.Key)"
        }
    }

    $existingZip = Join-Path $scratch 'existing.zip'
    $existingSentinel = 'existing-destination-must-survive'
    [IO.File]::WriteAllText(
        $existingZip,
        $existingSentinel,
        [Text.UTF8Encoding]::new($false))
    $existingRejected = $false
    try {
        New-DeterministicReleaseZip `
            -SourceDirectory $source `
            -EntryNames $entries `
            -DestinationPath $existingZip
    }
    catch {
        $existingRejected = $true
    }
    if (-not $existingRejected) {
        throw 'An existing release ZIP destination was overwritten.'
    }
    if ([IO.File]::ReadAllText($existingZip) -cne $existingSentinel) {
        throw 'An existing release ZIP destination was modified after rejection.'
    }

    $lockedEntryName = 'locked.txt'
    $lockedEntryPath = Join-Path $source $lockedEntryName
    [IO.File]::WriteAllText(
        $lockedEntryPath,
        'release-contract:locked',
        [Text.UTF8Encoding]::new($false))
    $partialZip = Join-Path $scratch 'partial.zip'
    $lockedStream = [IO.File]::Open(
        $lockedEntryPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::None)
    $partialRejected = $false
    try {
        try {
            New-DeterministicReleaseZip `
                -SourceDirectory $source `
                -EntryNames @('a-first.txt', $lockedEntryName) `
                -DestinationPath $partialZip
        }
        catch {
            $partialRejected = $true
        }
    }
    finally {
        $lockedStream.Dispose()
    }
    if (-not $partialRejected) {
        throw 'A forced mid-write release ZIP failure was not rejected.'
    }
    if (Test-Path -LiteralPath $partialZip) {
        throw 'A forced mid-write release ZIP failure left a partial destination.'
    }

    $firstZip = Join-Path $scratch 'first.zip'
    $secondZip = Join-Path $scratch 'second.zip'
    New-DeterministicReleaseZip `
        -SourceDirectory $source `
        -EntryNames $entries `
        -DestinationPath $firstZip

    foreach ($file in Get-ChildItem -LiteralPath $source -File) {
        $file.LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(7)
    }
    New-DeterministicReleaseZip `
        -SourceDirectory $source `
        -EntryNames @($entries[2], $entries[0], $entries[1]) `
        -DestinationPath $secondZip

    $firstHash = (Get-FileHash -LiteralPath $firstZip -Algorithm SHA256).Hash
    $secondHash = (Get-FileHash -LiteralPath $secondZip -Algorithm SHA256).Hash
    if ($firstHash -cne $secondHash) {
        throw "Deterministic ZIP hashes differ: first=$firstHash second=$secondHash"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($firstZip)
    try {
        $actualNames = @($archive.Entries | ForEach-Object FullName)
        $expectedNames = @($entries | Sort-Object -CaseSensitive)
        if (($actualNames -join "`n") -cne ($expectedNames -join "`n")) {
            throw "ZIP entries are not in deterministic ordinal order: $($actualNames -join ', ')"
        }
        $expectedWallClock = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
        foreach ($entry in $archive.Entries) {
            if ($entry.LastWriteTime.DateTime -ne $expectedWallClock) {
                throw "ZIP entry timestamp is not normalized: $($entry.FullName)"
            }
            if ($entry.CompressedLength -ne $entry.Length) {
                throw "ZIP entry must use deterministic store mode: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Output 'PASS: strict release SemVer policy and deterministic ZIP packaging contract.'
}
finally {
    Remove-Module -Name 'Release-Packaging.Core' -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
