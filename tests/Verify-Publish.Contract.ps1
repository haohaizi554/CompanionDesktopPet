param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$PublishExePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $repoRoot 'outputs\verify-contract-test'
$source = (Resolve-Path -LiteralPath $ExePath).Path
$verifier = Join-Path $repoRoot 'scripts\Verify-Publish.ps1'
if ([string]::IsNullOrWhiteSpace($PublishExePath)) {
    $PublishExePath = Join-Path $repoRoot 'publish\CompanionDesktopPet.exe'
}
$publishSource = (Resolve-Path -LiteralPath $PublishExePath).Path

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$publishHash = (Get-FileHash -LiteralPath $publishSource -Algorithm SHA256).Hash
if ($sourceHash -ne $publishHash) {
    throw 'Contract precondition failed: ExePath and PublishExePath hashes must match.'
}

function Reset-Scratch {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    New-Item -ItemType Directory -Path $scratch | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $scratch 'candidate.exe')
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Case,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Arrange
    )

    Reset-Scratch
    & $Arrange $scratch

    $rejected = $false
    try {
        & $verifier `
            -ExePath (Join-Path $scratch 'candidate.exe') `
            -PublishExePath $publishSource
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Expected Verify-Publish.ps1 to reject case: $Case"
    }
}

try {
    Assert-Rejected -Case 'adjacent runtime sidecar' -Arrange {
        param($directory)
        Set-Content -LiteralPath (Join-Path $directory 'unexpected.pdb') -Value 'contract test'
    }

    Assert-Rejected -Case 'extra executable' -Arrange {
        param($directory)
        Copy-Item -LiteralPath (Join-Path $directory 'candidate.exe') -Destination (Join-Path $directory 'extra.exe')
    }

    Assert-Rejected -Case 'nested runtime dependency directory' -Arrange {
        param($directory)
        $runtimeDirectory = Join-Path $directory 'runtimes\win-x64\native'
        New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
        Set-Content -LiteralPath (Join-Path $runtimeDirectory 'dependency.dll') -Value 'contract test'
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

Write-Output 'PASS: publish verifier rejects sidecars, extra EXEs, and nested dependencies.'
