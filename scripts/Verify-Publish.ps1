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

$allowedDeliveryTextFiles = @(
    (-join ([char[]](0x4F7F, 0x7528, 0x8BF4, 0x660E))) + '.txt'
)
$unexpectedFiles = @($deliveryFiles | Where-Object {
    $_.FullName -ne $resolved -and $_.Name -notin $allowedDeliveryTextFiles
})
if ($unexpectedFiles.Count -ne 0) {
    throw "Delivery directory contains forbidden sidecars: $($unexpectedFiles.Name -join ', ')"
}

if ([string]::IsNullOrWhiteSpace($PublishExePath)) {
    $PublishExePath = Join-Path $repoRoot 'publish\CompanionDesktopPet.exe'
}
$resolvedPublish = (Resolve-Path -LiteralPath $PublishExePath).Path
$publishItem = Get-Item -LiteralPath $resolvedPublish
if ($publishItem.Extension -ne '.exe' -or $publishItem.Length -le 0) {
    throw 'Publish artifact is not a non-empty EXE.'
}

$publishDirectory = Split-Path -Parent $resolvedPublish
$publishFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File)
$publishDirectories = @(Get-ChildItem -LiteralPath $publishDirectory -Directory)
if ($publishDirectories.Count -ne 0 -or
    $publishFiles.Count -ne 1 -or
    $publishFiles[0].FullName -ne $resolvedPublish -or
    $publishItem.Name -ne 'CompanionDesktopPet.exe') {
    $publishContents = @($publishFiles.Name) + @($publishDirectories.Name | ForEach-Object { "$_\" })
    throw "Publish directory must contain only CompanionDesktopPet.exe: $($publishContents -join ', ')"
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
        -WindowStyle Hidden `
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

Write-Output "PASS: one delivered EXE, no runtime sidecars; SmokePID=$processId; ExitCode=0; SHA-256: publish=$publishHash delivery=$deliveredHash isolated=$isolatedHash; EXE=$resolved"
