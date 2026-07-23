param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$PublishExePath = ''
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
try {
    $process = Start-Process -FilePath $isolatedExe -WorkingDirectory $verifyDirectory -PassThru
    $processId = $process.Id

    if (-not $process.WaitForInputIdle(15000)) {
        throw "Desktop pet PID $processId did not become input-idle within 15 seconds."
    }

    Start-Sleep -Milliseconds 1200
    $process.Refresh()
    if ($process.HasExited) {
        throw "Desktop pet PID $processId exited early with code $($process.ExitCode)."
    }

    $trackedProcess = Get-Process -Id $processId -ErrorAction Stop
    if ($trackedProcess.Id -ne $processId) {
        throw 'Launched PID could not be tracked.'
    }
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
                if (-not $process.WaitForExit(10000)) {
                    $cleanupFailure = "Desktop pet PID $processId remained alive after forced termination."
                }
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

Write-Output "PASS: one delivered EXE, no runtime sidecars, matching publish SHA-256, isolated PID smoke test succeeded: $resolved"
