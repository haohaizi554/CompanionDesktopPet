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
$defaultTimeoutMatch = [Regex]::Match(
    $verifierText,
    '\[int\]\$SmokeTimeoutSeconds\s*=\s*(?<Seconds>[0-9]+)'
)
if (-not $defaultTimeoutMatch.Success -or
    [int]$defaultTimeoutMatch.Groups['Seconds'].Value -lt 30) {
    throw 'Verify-Publish.ps1 default smoke timeout must be at least 30 seconds.'
}
if ($verifierText -notmatch "--smoke-test") {
    throw 'Verify-Publish.ps1 must launch the isolated executable with --smoke-test.'
}
if ($verifierText -match 'WaitForInputIdle|CloseMainWindow') {
    throw 'Verify-Publish.ps1 must not treat input-idle or manual window closing as smoke-test success.'
}
if ($verifierText -notmatch '(?m)^\s*-WindowStyle\s+Hidden\s*`?\s*$') {
    throw 'Verify-Publish.ps1 must launch the smoke process with -WindowStyle Hidden.'
}
if ($verifierText -notmatch 'Stop-Process\s+-Id\s+\$processId') {
    throw 'Verify-Publish.ps1 must clean up only the PID returned by Start-Process.'
}
if ($verifierText -match '(?i)(?:Get|Stop)-Process[^\r\n]*\s-Name(?:\s|$)') {
    throw 'Verify-Publish.ps1 must never inspect or clean up smoke processes by process name.'
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
    Copy-Item -LiteralPath $source -Destination (Join-Path $publish 'CompanionDesktopPet.exe')
    return [pscustomobject]@{
        Delivery = $delivery
        Publish = $publish
        PublishExe = Join-Path $publish 'CompanionDesktopPet.exe'
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

function Assert-Accepted {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Case,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Arrange,

        [switch]$AssertSuccessEvidence
    )

    $paths = Reset-Scratch
    & $Arrange $paths

    try {
        $verifierOutput = @(& $verifier `
            -ExePath (Join-Path $paths.Delivery 'candidate.exe') `
            -PublishExePath $paths.PublishExe `
            -SmokeTimeoutSeconds 30)
    }
    catch {
        throw "Expected Verify-Publish.ps1 to accept case '$Case': $($_.Exception.Message)"
    }

    if ($AssertSuccessEvidence) {
        $successText = $verifierOutput -join [Environment]::NewLine
        $candidatePath = Join-Path $paths.Delivery 'candidate.exe'
        $expectedHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
        $escapedHash = [Regex]::Escape($expectedHash)

        if ($successText -notmatch 'SmokePID=(?<SmokePid>[1-9][0-9]*)') {
            throw "Verify-Publish.ps1 success output must identify the current smoke PID: $successText"
        }
        $smokePid = [int]$Matches['SmokePid']
        if ($successText -notmatch 'ExitCode=0') {
            throw "Verify-Publish.ps1 success output must include ExitCode=0: $successText"
        }
        if ($successText -notmatch "SHA-256: publish=$escapedHash delivery=$escapedHash isolated=$escapedHash") {
            throw "Verify-Publish.ps1 success output must include identical publish, delivery, and isolated SHA-256 values: $successText"
        }
        if ($null -ne (Get-Process -Id $smokePid -ErrorAction SilentlyContinue)) {
            throw "Verify-Publish.ps1 reported smoke PID $smokePid, but that process is still running."
        }
    }
}

try {
    Assert-Accepted -Case 'the documented delivery readme' -Arrange {
        param($paths)
        $readmeName = (-join ([char[]](0x4F7F, 0x7528, 0x8BF4, 0x660E))) + '.txt'
        Set-Content -LiteralPath (Join-Path $paths.Delivery $readmeName) -Value 'contract test'
    } -AssertSuccessEvidence

    Assert-Rejected -Case 'publish DLL sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Publish 'CompanionDesktopPet.dll') -Value 'contract test'
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'publish JSON sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Publish 'CompanionDesktopPet.runtimeconfig.json') -Value '{}'
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'publish PDB sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Publish 'CompanionDesktopPet.pdb') -Value 'contract test'
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'publish arbitrary extra file' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Publish 'notes.txt') -Value 'contract test'
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'publish nested directory' -Arrange {
        param($paths)
        New-Item -ItemType Directory -Path (Join-Path $paths.Publish 'runtimes') | Out-Null
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'adjacent runtime sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Delivery 'unexpected.pdb') -Value 'contract test'
    } -ExpectedMessage 'forbidden sidecars'

    Assert-Rejected -Case 'unapproved delivery text file' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Delivery 'notes.txt') -Value 'contract test'
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

    Assert-Accepted -Case 'UTF-8 exact-manifest-approved identity bytes' -Arrange {
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
    }

    Assert-Accepted -Case 'UTF-16 exact-manifest-approved identity bytes' -Arrange {
        param($paths)
        $marker = -join ([char[]](0x5C0F, 0x73A5))
        $bytes = [Text.Encoding]::Unicode.GetBytes($marker)
        $binaryPaths = @(
            (Join-Path $paths.Delivery 'candidate.exe')
            $paths.PublishExe
        )
        foreach ($path in $binaryPaths) {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Append, [IO.FileAccess]::Write)
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
    }

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

Write-Output 'PASS: publish verifier accepts exact-manifest-approved identity bytes after a real smoke test, and rejects publish sidecars, delivery sidecars, extra EXEs, nested dependencies, forced smoke termination, and non-zero smoke exit.'
