param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$PublishExePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $repoRoot 'outputs\verify-contract-test'
$helperDirectory = Join-Path $repoRoot 'outputs\verify-contract-helpers'
$source = (Resolve-Path -LiteralPath $ExePath).Path
$verifier = Join-Path $repoRoot 'scripts\Verify-Publish.ps1'
$verifierText = Get-Content -LiteralPath $verifier -Raw
if ($verifierText -notmatch "--smoke-test") {
    throw 'Verify-Publish.ps1 must launch the isolated executable with --smoke-test.'
}
if ($verifierText -match 'WaitForInputIdle|CloseMainWindow') {
    throw 'Verify-Publish.ps1 must not treat input-idle or manual window closing as smoke-test success.'
}
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
    $delivery = Join-Path $scratch 'delivery'
    $publish = Join-Path $scratch 'publish'
    New-Item -ItemType Directory -Path $delivery | Out-Null
    New-Item -ItemType Directory -Path $publish | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $delivery 'candidate.exe')
    Copy-Item -LiteralPath $source -Destination (Join-Path $publish 'candidate.exe')
    return [pscustomobject]@{
        Delivery = $delivery
        PublishExe = Join-Path $publish 'candidate.exe'
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Case,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Arrange,

        [string]$ExpectedMessage = '',

        [string]$CandidateSource = '',

        [int]$SmokeTimeoutSeconds = 1
    )

    $paths = Reset-Scratch
    if (-not [string]::IsNullOrWhiteSpace($CandidateSource)) {
        Copy-Item -LiteralPath $CandidateSource -Destination (Join-Path $paths.Delivery 'candidate.exe') -Force
        Copy-Item -LiteralPath $CandidateSource -Destination $paths.PublishExe -Force
    }
    & $Arrange $paths

    $rejected = $false
    $rejectionMessage = ''
    try {
        & $verifier `
            -ExePath (Join-Path $paths.Delivery 'candidate.exe') `
            -PublishExePath $paths.PublishExe `
            -SmokeTimeoutSeconds $SmokeTimeoutSeconds
    }
    catch {
        $rejected = $true
        $rejectionMessage = $_.Exception.Message
    }

    if (-not $rejected) {
        throw "Expected Verify-Publish.ps1 to reject case: $Case"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMessage) -and
        $rejectionMessage -notlike "*$ExpectedMessage*") {
        throw "Verify-Publish.ps1 rejected '$Case' for the wrong reason: $rejectionMessage"
    }
}

try {
    Assert-Rejected -Case 'adjacent runtime sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Delivery 'unexpected.pdb') -Value 'contract test'
    } -ExpectedMessage 'forbidden sidecars'

    Assert-Rejected -Case 'extra executable' -Arrange {
        param($paths)
        Copy-Item -LiteralPath (Join-Path $paths.Delivery 'candidate.exe') -Destination (Join-Path $paths.Delivery 'extra.exe')
    } -ExpectedMessage 'exactly one EXE'

    Assert-Rejected -Case 'nested runtime dependency directory' -Arrange {
        param($paths)
        $runtimeDirectory = Join-Path $paths.Delivery 'runtimes\win-x64\native'
        New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
        Set-Content -LiteralPath (Join-Path $runtimeDirectory 'dependency.dll') -Value 'contract test'
    } -ExpectedMessage 'forbidden subdirectories'

    Assert-Rejected -Case 'UTF-8 reviewed PII marker bytes' -Arrange {
        param($paths)
        $marker = -join ([char[]](0x96F7, 0x7433, 0x73A5))
        $bytes = [Text.Encoding]::UTF8.GetBytes($marker)
        $binaryPaths = @(
            (Join-Path $paths.Delivery 'candidate.exe')
            $paths.PublishExe
        )
        foreach ($path in $binaryPaths) {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Append, [IO.FileAccess]::Write)
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
    } -ExpectedMessage 'forbidden PII marker'

    Assert-Rejected -Case 'UTF-16 reviewed PII marker bytes' -Arrange {
        param($paths)
        $marker = -join ([char[]](0x5DE5, 0x8D44))
        $bytes = [Text.Encoding]::Unicode.GetBytes($marker)
        $binaryPaths = @(
            (Join-Path $paths.Delivery 'candidate.exe')
            $paths.PublishExe
        )
        foreach ($path in $binaryPaths) {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Append, [IO.FileAccess]::Write)
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
    } -ExpectedMessage 'forbidden PII marker'

    if (Test-Path -LiteralPath $helperDirectory) {
        Remove-Item -LiteralPath $helperDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $helperDirectory | Out-Null
    $hangingExe = Join-Path $helperDirectory 'hanging-smoke.exe'
    Add-Type -Language CSharp -OutputType ConsoleApplication -OutputAssembly $hangingExe -TypeDefinition @'
using System.Threading;

public static class HangingSmokeProgram
{
    public static int Main(string[] args)
    {
        Thread.Sleep(30000);
        return 0;
    }
}
'@
    Assert-Rejected -Case 'smoke timeout requiring forced termination' -Arrange {
        param($paths)
    } -CandidateSource $hangingExe -ExpectedMessage 'timed out'

    $failingExe = Join-Path $helperDirectory 'failing-smoke.exe'
    Add-Type -Language CSharp -OutputType ConsoleApplication -OutputAssembly $failingExe -TypeDefinition @'
public static class FailingSmokeProgram
{
    public static int Main(string[] args)
    {
        return 7;
    }
}
'@
    Assert-Rejected -Case 'smoke process returns a non-zero exit code' -Arrange {
        param($paths)
    } -CandidateSource $failingExe -SmokeTimeoutSeconds 5 -ExpectedMessage 'non-zero code 7'
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    if (Test-Path -LiteralPath $helperDirectory) {
        Remove-Item -LiteralPath $helperDirectory -Recurse -Force
    }
}

Write-Output 'PASS: publish verifier rejects sidecars, extra EXEs, nested dependencies, reviewed PII bytes, forced smoke termination, and non-zero smoke exit.'
