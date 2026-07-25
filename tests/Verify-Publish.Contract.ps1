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
$verifierCore = Join-Path $repoRoot 'scripts\Verify-Publish.Core.psm1'
$repoPidRecord = Join-Path $repoRoot 'smoke-process.pid'
$verifyPidRecord = Join-Path $repoRoot 'outputs\verify\smoke-process.pid'
$siblingPidRecord = Join-Path $helperDirectory 'sibling\smoke-process.pid'

$coreModule = Import-Module -Name $verifierCore -Force -PassThru
try {
    $defaultSmokeTimeoutSeconds = Get-PublishSmokeDefaultTimeoutSeconds
}
finally {
    Remove-Module -Name $coreModule.Name -Force
}
if ($defaultSmokeTimeoutSeconds -ne 30) {
    throw "Core smoke timeout policy must be exactly 30 seconds; actual=$defaultSmokeTimeoutSeconds."
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

function Set-HiddenItem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $item = Get-Item -LiteralPath $LiteralPath -Force
    $item.Attributes = $item.Attributes -bor [IO.FileAttributes]::Hidden
}

function Assert-RecordedSmokeProcessStopped {
    if (-not (Test-Path -LiteralPath $verifyPidRecord)) {
        throw 'Smoke helper did not record the PID started by Verify-Publish.ps1.'
    }

    $recordedProcessId = [int](Get-Content -LiteralPath $verifyPidRecord -Raw)
    $remainingProcess = Get-Process -Id $recordedProcessId -ErrorAction SilentlyContinue
    if ($null -ne $remainingProcess) {
        Stop-Process -Id $recordedProcessId -Force -ErrorAction SilentlyContinue
        throw "Verify-Publish.ps1 left timed-out smoke PID $recordedProcessId running."
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

        [string]$CandidateSource = '',

        [int]$SmokeTimeoutSeconds = 30,

        [switch]$UseDefaultSmokeTimeout,

        [switch]$AssertSuccessEvidence
    )

    $paths = Reset-Scratch
    if (-not [string]::IsNullOrWhiteSpace($CandidateSource)) {
        Copy-Item -LiteralPath $CandidateSource -Destination (Join-Path $paths.Delivery 'candidate.exe') -Force
        Copy-Item -LiteralPath $CandidateSource -Destination $paths.PublishExe -Force
    }
    & $Arrange $paths

    try {
        $verifierArguments = @{
            ExePath = Join-Path $paths.Delivery 'candidate.exe'
            PublishExePath = $paths.PublishExe
        }
        if (-not $UseDefaultSmokeTimeout) {
            $verifierArguments['SmokeTimeoutSeconds'] = $SmokeTimeoutSeconds
        }
        $verifierOutput = @(& $verifier @verifierArguments)
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
    Assert-Rejected -Case 'explicit smoke timeout below the supported range' -Arrange {
        param($paths)
    } -SmokeTimeoutSeconds 0

    Assert-Rejected -Case 'explicit smoke timeout above the supported range' -Arrange {
        param($paths)
    } -SmokeTimeoutSeconds 121

    Assert-Accepted -Case 'the documented delivery readme' -Arrange {
        param($paths)
        $readmeName = (-join ([char[]](0x4F7F, 0x7528, 0x8BF4, 0x660E))) + '.txt'
        Set-Content -LiteralPath (Join-Path $paths.Delivery $readmeName) -Value 'contract test'
    } -UseDefaultSmokeTimeout -AssertSuccessEvidence

    Assert-Rejected -Case 'publish DLL sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Publish 'CompanionDesktopPet.dll') -Value 'contract test'
    } -ExpectedMessage 'Publish directory'

    Assert-Rejected -Case 'hidden publish DLL sidecar' -Arrange {
        param($paths)
        $sidecar = Join-Path $paths.Publish 'CompanionDesktopPet.dll'
        Set-Content -LiteralPath $sidecar -Value 'contract test'
        Set-HiddenItem -LiteralPath $sidecar
    } -ExpectedMessage 'Publish directory' -SmokeTimeoutSeconds 30

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

    Assert-Rejected -Case 'hidden publish nested directory' -Arrange {
        param($paths)
        $sidecar = Join-Path $paths.Publish 'runtimes'
        New-Item -ItemType Directory -Path $sidecar | Out-Null
        Set-HiddenItem -LiteralPath $sidecar
    } -ExpectedMessage 'Publish directory' -SmokeTimeoutSeconds 30

    Assert-Rejected -Case 'adjacent runtime sidecar' -Arrange {
        param($paths)
        Set-Content -LiteralPath (Join-Path $paths.Delivery 'unexpected.pdb') -Value 'contract test'
    } -ExpectedMessage 'forbidden sidecars'

    Assert-Rejected -Case 'hidden adjacent runtime sidecar' -Arrange {
        param($paths)
        $sidecar = Join-Path $paths.Delivery 'CompanionDesktopPet.runtimeconfig.json'
        Set-Content -LiteralPath $sidecar -Value '{}'
        Set-HiddenItem -LiteralPath $sidecar
    } -ExpectedMessage 'forbidden sidecars' -SmokeTimeoutSeconds 30

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

    Assert-Rejected -Case 'hidden nested runtime dependency directory' -Arrange {
        param($paths)
        $runtimeDirectory = Join-Path $paths.Delivery 'runtimes'
        New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
        Set-HiddenItem -LiteralPath $runtimeDirectory
    } -ExpectedMessage 'forbidden subdirectories' -SmokeTimeoutSeconds 30

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
    } -SmokeTimeoutSeconds 120

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

    $launchContractExe = Join-Path $helperDirectory 'launch-contract.exe'
    Add-Type -Language CSharp -OutputType ConsoleApplication -OutputAssembly $launchContractExe -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LaunchContractProgram
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--smoke-test")
        {
            return 21;
        }

        IntPtr console = GetConsoleWindow();
        if (console != IntPtr.Zero && IsWindowVisible(console))
        {
            return 22;
        }

        return 0;
    }
}
'@
    Assert-Accepted -Case 'isolated launch receives the smoke argument and has no visible console' -Arrange {
        param($paths)
    } -CandidateSource $launchContractExe -SmokeTimeoutSeconds 5 -AssertSuccessEvidence

    $hangingExe = Join-Path $helperDirectory 'hanging-smoke.exe'
    Add-Type -Language CSharp -OutputType ConsoleApplication -OutputAssembly $hangingExe -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

public static class HangingSmokeProgram
{
    public static int Main(string[] args)
    {
        File.WriteAllText(
            Path.Combine(Environment.CurrentDirectory, "smoke-process.pid"),
            Process.GetCurrentProcess().Id.ToString());
        Thread.Sleep(30000);
        return 0;
    }
}
'@
    Assert-Rejected -Case 'smoke timeout requiring forced termination' -Arrange {
        param($paths)
    } -CandidateSource $hangingExe -ExpectedMessage 'timed out'
    Assert-RecordedSmokeProcessStopped

    $siblingDirectory = Join-Path $helperDirectory 'sibling'
    New-Item -ItemType Directory -Path $siblingDirectory | Out-Null
    $siblingExe = Join-Path $siblingDirectory 'candidate.exe'
    Copy-Item -LiteralPath $hangingExe -Destination $siblingExe
    $siblingProcess = Start-Process `
        -FilePath $siblingExe `
        -ArgumentList '--smoke-test' `
        -WorkingDirectory $siblingDirectory `
        -WindowStyle Hidden `
        -PassThru
    try {
        Assert-Rejected -Case 'timeout cleanup leaves an unrelated same-name process alive' -Arrange {
            param($paths)
        } -CandidateSource $hangingExe -ExpectedMessage 'timed out'
        Assert-RecordedSmokeProcessStopped

        if (Test-Path -LiteralPath $repoPidRecord) {
            throw "Sibling smoke helper leaked its PID record into the repository root: $repoPidRecord"
        }
        if (-not (Test-Path -LiteralPath $siblingPidRecord)) {
            throw "Sibling smoke helper did not use its isolated working directory: $siblingDirectory"
        }

        $siblingProcess.Refresh()
        if ($siblingProcess.HasExited) {
            throw 'Verify-Publish.ps1 cleanup terminated an unrelated process with the same executable name.'
        }
    }
    finally {
        $siblingProcess.Refresh()
        if (-not $siblingProcess.HasExited) {
            Stop-Process -Id $siblingProcess.Id -Force -ErrorAction SilentlyContinue
            $null = $siblingProcess.WaitForExit(10000)
        }
        $siblingProcess.Dispose()
    }

    $inputIdleExe = Join-Path $helperDirectory 'input-idle-smoke.exe'
    Add-Type `
        -Language CSharp `
        -OutputType WindowsApplication `
        -OutputAssembly $inputIdleExe `
        -ReferencedAssemblies 'System.Windows.Forms.dll', 'System.Drawing.dll' `
        -TypeDefinition @'
using System;
using System.Windows.Forms;

public static class InputIdleSmokeProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--smoke-test")
        {
            return 31;
        }

        Application.EnableVisualStyles();
        using (Form form = new Form())
        {
            form.Text = "publish-contract-input-idle";
            Application.Run(form);
        }
        return 0;
    }
}
'@
    Assert-Rejected -Case 'GUI input-idle is not mistaken for a completed smoke test' -Arrange {
        param($paths)
    } -CandidateSource $inputIdleExe -ExpectedMessage 'timed out'

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
    foreach ($pidRecord in @($repoPidRecord, $verifyPidRecord, $siblingPidRecord)) {
        if (Test-Path -LiteralPath $pidRecord -PathType Leaf) {
            Remove-Item -LiteralPath $pidRecord -Force
        }
    }
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    if (Test-Path -LiteralPath $helperDirectory) {
        Remove-Item -LiteralPath $helperDirectory -Recurse -Force
    }
}

Write-Output 'PASS: publish verifier behavior accepts approved payloads and rejects sidecars, nested dependencies, visible/wrong-mode launches, GUI input-idle, timeouts, and non-zero exits without terminating unrelated same-name processes.'
