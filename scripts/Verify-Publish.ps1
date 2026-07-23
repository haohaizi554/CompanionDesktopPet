param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$PublishExePath = '',

    [ValidateRange(1, 120)]
    [int]$SmokeTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolved = (Resolve-Path -LiteralPath $ExePath).Path
$directory = Split-Path -Parent $resolved

$item = Get-Item -LiteralPath $resolved
if ($item.Extension -ne '.exe' -or $item.Length -le 0) {
    throw 'Delivered artifact is not a non-empty EXE.'
}

$deliveryFiles = @(Get-ChildItem -LiteralPath $directory -File)
$deliveryDirectories = @(Get-ChildItem -LiteralPath $directory -Directory)
if ($deliveryDirectories.Count -ne 0) {
    throw "Delivery directory contains forbidden subdirectories: $($deliveryDirectories.Name -join ', ')"
}

$deliveryExecutables = @($deliveryFiles | Where-Object { $_.Extension -eq '.exe' })
if ($deliveryExecutables.Count -ne 1 -or $deliveryExecutables[0].FullName -ne $resolved) {
    throw "Delivery directory must contain exactly one EXE: $directory"
}

$unexpectedFiles = @($deliveryFiles | Where-Object { $_.Extension -notin @('.exe', '.txt') })
if ($unexpectedFiles.Count -ne 0) {
    throw "Delivery directory contains forbidden sidecars: $($unexpectedFiles.Name -join ', ')"
}

$forbiddenPiiMarkers = @(
    (-join ([char[]](0x96F7, 0x7433, 0x73A5)))
    (-join ([char[]](0x5C0F, 0x73A5)))
    (-join ([char[]](0x73A5, 0x73A5)))
    (-join ([char[]](0x6E56, 0x5357)))
    (-join ([char[]](0x957F, 0x6C99)))
    (-join ([char[]](0x5E7F, 0x4E1C)))
    (-join ([char[]](0x6708, 0x85AA)))
    (-join ([char[]](0x5DE5, 0x8D44)))
    (-join ([char[]](0x6253, 0x96F6, 0x5DE5)))
)
$bytePreservingEncoding = [Text.Encoding]::GetEncoding(28591)
$binaryBytes = [IO.File]::ReadAllBytes($resolved)
$binaryText = $bytePreservingEncoding.GetString($binaryBytes)
$markerEncodings = @(
    [Text.Encoding]::UTF8
    [Text.Encoding]::Unicode
    [Text.Encoding]::BigEndianUnicode
)
foreach ($marker in $forbiddenPiiMarkers) {
    foreach ($encoding in $markerEncodings) {
        $needle = $bytePreservingEncoding.GetString($encoding.GetBytes($marker))
        if ($binaryText.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) {
            throw "Delivered EXE contains forbidden PII marker bytes ($($encoding.WebName))."
        }
    }
}
$binaryBytes = $null
$binaryText = $null

if ([string]::IsNullOrWhiteSpace($PublishExePath)) {
    $PublishExePath = Join-Path $repoRoot 'publish\CompanionDesktopPet.exe'
}
$resolvedPublish = (Resolve-Path -LiteralPath $PublishExePath).Path
$publishItem = Get-Item -LiteralPath $resolvedPublish
if ($publishItem.Extension -ne '.exe' -or $publishItem.Length -le 0) {
    throw 'Publish artifact is not a non-empty EXE.'
}

$deliveredHash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
$publishHash = (Get-FileHash -LiteralPath $resolvedPublish -Algorithm SHA256).Hash
if ($deliveredHash -ne $publishHash) {
    throw "Delivered EXE hash differs from publish EXE: delivered=$deliveredHash publish=$publishHash"
}

$verifyDirectory = Join-Path $repoRoot 'outputs\verify'
if (Test-Path -LiteralPath $verifyDirectory) {
    Remove-Item -LiteralPath $verifyDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $verifyDirectory | Out-Null

$isolatedExe = Join-Path $verifyDirectory $item.Name
Copy-Item -LiteralPath $resolved -Destination $isolatedExe

$isolatedFiles = @(Get-ChildItem -LiteralPath $verifyDirectory -File)
if ($isolatedFiles.Count -ne 1 -or $isolatedFiles[0].Extension -ne '.exe') {
    throw 'Isolated verification directory must contain exactly one EXE.'
}

$isolatedHash = (Get-FileHash -LiteralPath $isolatedExe -Algorithm SHA256).Hash
if ($isolatedHash -ne $deliveredHash) {
    throw 'Isolated EXE hash differs from delivered EXE.'
}

$process = $null
$processId = $null
$cleanupFailure = $null
$smokeFailure = $null
try {
    $process = Start-Process `
        -FilePath $isolatedExe `
        -ArgumentList '--smoke-test' `
        -WorkingDirectory $verifyDirectory `
        -PassThru
    $processId = $process.Id

    if (-not $process.WaitForExit($SmokeTimeoutSeconds * 1000)) {
        $smokeFailure = "Smoke-test timed out after $SmokeTimeoutSeconds seconds; forced termination is cleanup only."
    }
    elseif ($process.ExitCode -ne 0) {
        $smokeFailure = "Smoke-test PID $processId exited with non-zero code $($process.ExitCode)."
    }
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            if (-not $process.WaitForExit(10000)) {
                $cleanupFailure = "Desktop pet PID $processId remained alive after forced termination."
            }
        }
    }
}

if ($null -ne $cleanupFailure) {
    throw $cleanupFailure
}

if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
    throw "Desktop pet PID $processId is still running after smoke test cleanup."
}

if ($null -ne $smokeFailure) {
    throw $smokeFailure
}

Write-Output "PASS: one delivered EXE, no runtime sidecars or forbidden PII bytes, matching publish SHA-256, isolated --smoke-test exited 0: $resolved"
