param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $repoRoot 'scripts\Ci-TestEvidence.Core.psm1'
$scratch = Join-Path $repoRoot 'outputs\ci-test-evidence-contract'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Case
    )

    if ($Actual -cne $Expected) {
        throw "$Case expected '$Expected', got '$Actual'."
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Case,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMessage
    )

    $rejected = $false
    $message = ''
    try {
        & $Action
    }
    catch {
        $rejected = $true
        $message = $_.Exception.Message
    }

    if (-not $rejected) {
        throw "Expected rejection for $Case."
    }
    if ($message -notlike "*$ExpectedMessage*") {
        throw "Rejected '$Case' for the wrong reason: $message"
    }
}

function New-SyntheticTrx {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [hashtable]$Counters,

        [string[]]$ResultSummaryOutcomes = @('Completed'),

        [string[]]$UnitTestResultOutcomes = @('Passed', 'Passed', 'Passed')
    )

    $attributes = @($Counters.Keys | Sort-Object | ForEach-Object {
        '{0}="{1}"' -f $_, $Counters[$_]
    }) -join ' '
    $resultSummaryXml = @(
        for ($index = 0; $index -lt $ResultSummaryOutcomes.Count; $index++) {
            if ($index -eq 0) {
                '  <ResultSummary outcome="{0}"><Counters {1} /></ResultSummary>' -f $ResultSummaryOutcomes[$index], $attributes
            }
            else {
                '  <ResultSummary outcome="{0}" />' -f $ResultSummaryOutcomes[$index]
            }
        }
    ) -join [Environment]::NewLine
    $unitTestResultXml = @(
        for ($index = 0; $index -lt $UnitTestResultOutcomes.Count; $index++) {
            '    <UnitTestResult outcome="{0}" />' -f $UnitTestResultOutcomes[$index]
        }
    ) -join [Environment]::NewLine
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
$resultSummaryXml
  <Results>
$unitTestResultXml
  </Results>
</TestRun>
"@
    [IO.File]::WriteAllText($Path, $xml, [Text.UTF8Encoding]::new($false))
}

$requiredTrxCounters = [ordered]@{
    total = 3
    executed = 3
    passed = 3
    failed = 0
    error = 0
    timeout = 0
    aborted = 0
    inconclusive = 0
    passedButRunAborted = 0
    notRunnable = 0
    notExecuted = 0
    disconnected = 0
    warning = 0
    completed = 0
    inProgress = 0
    pending = 0
}

try {
    Import-Module -Name $modulePath -Force
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null

    $pythonEvidence = Get-PythonUnittestEvidence -Lines @(
        'test_a (tests.test_sample.SampleTests.test_a) ... ok'
        'test_b (tests.test_sample.SampleTests.test_b) ... ok'
        'test_c (tests.test_sample.SampleTests.test_c) ... ok'
        ''
        '----------------------------------------------------------------------'
        'Ran 3 tests in 0.003s'
        ''
        'OK'
    )
    Assert-Equal -Expected 3 -Actual $pythonEvidence.TestsRun -Case 'Python test run count'
    Assert-Equal -Expected 0 -Actual $pythonEvidence.Skipped -Case 'Python skipped count'
    Assert-Equal -Expected 0 -Actual $pythonEvidence.Failures -Case 'Python failure count'
    Assert-Equal -Expected 0 -Actual $pythonEvidence.Errors -Case 'Python error count'
    $pythonCollisionEvidence = Get-PythonUnittestEvidence -Lines @(
        'test_prints_runner_like_text (tests.test_sample.SampleTests.test_prints_runner_like_text) ... Ran 97 tests in 9.7s'
        'OK'
        'test_real (tests.test_sample.SampleTests.test_real) ... ok'
        ''
        '----------------------------------------------------------------------'
        'Ran 3 tests in 0.003s'
        ''
        'OK'
    )
    Assert-Equal -Expected 3 -Actual $pythonCollisionEvidence.TestsRun -Case 'Python terminal summary ignores test-output collisions'

    Assert-Rejected -Case 'zero Python tests' -ExpectedMessage 'zero tests' -Action {
        Get-PythonUnittestEvidence -Lines @('Ran 0 tests in 0.000s', 'OK') | Out-Null
    }
    Assert-Rejected -Case 'skipped Python tests' -ExpectedMessage 'skipped=1' -Action {
        Get-PythonUnittestEvidence -Lines @('Ran 3 tests in 0.003s', 'OK (skipped=1)') | Out-Null
    }
    Assert-Rejected -Case 'failed Python tests' -ExpectedMessage 'failures=1' -Action {
        Get-PythonUnittestEvidence -Lines @('Ran 3 tests in 0.003s', 'FAILED (failures=1)') | Out-Null
    }
    Assert-Rejected -Case 'errored Python tests' -ExpectedMessage 'errors=1' -Action {
        Get-PythonUnittestEvidence -Lines @('Ran 3 tests in 0.003s', 'FAILED (errors=1)') | Out-Null
    }

    $discoveryEvidence = Get-DotNetTestDiscoveryEvidence `
        -Lines @(
            'The following Tests are available:'
            '    CompanionDesktopPet.Tests.AgentTests.First'
            '    CompanionDesktopPet.Tests.AgentTests.Second'
            '    CompanionDesktopPet.Tests.AgentTests.Third'
        )
    Assert-Equal -Expected 3 -Actual $discoveryEvidence.DiscoveredTests -Case '.NET discovery count'
    $aggregateDiscoveryEvidence = Get-DotNetTestDiscoveryEvidence -Lines @(
        'The following Tests are available:'
        '    CompanionDesktopPet.Tests.AgentTests.First'
        '    SeparateTestAssembly.Tests.OtherTests.Second'
    )
    Assert-Equal -Expected 2 -Actual $aggregateDiscoveryEvidence.DiscoveredTests -Case '.NET aggregate discovery count'
    $tailDiscoveryEvidence = Get-DotNetTestDiscoveryEvidence -Lines @(
        'The following Tests are available:'
        '    First.Assembly.Tests.FirstMethod'
        'Workload updates are available. Run dotnet workload list for more information.'
        '    Forged.Assembly.Tests.MustNotBeCounted'
    )
    Assert-Equal -Expected 1 -Actual $tailDiscoveryEvidence.DiscoveredTests -Case '.NET discovery stops at a non-list tail line'

    $discoveryRecord = Join-Path $scratch 'discovery-count.txt'
    Save-CiTestEvidenceInteger `
        -Path $discoveryRecord `
        -Name 'DiscoveredTests' `
        -Value $discoveryEvidence.DiscoveredTests
    $savedDiscoveryCount = Read-CiTestEvidenceInteger `
        -Path $discoveryRecord `
        -Name 'DiscoveredTests'
    Assert-Equal -Expected 3 -Actual $savedDiscoveryCount -Case 'saved .NET discovery count'

    Assert-Rejected -Case 'zero .NET discovered tests' -ExpectedMessage 'zero matching tests' -Action {
        Get-DotNetTestDiscoveryEvidence `
            -Lines @('The following Tests are available:') | Out-Null
    }
    Assert-Rejected -Case 'build output masquerading as .NET discovery' -ExpectedMessage 'zero matching tests' -Action {
        Get-DotNetTestDiscoveryEvidence `
            -Lines @('CompanionDesktopPet.Tests.AgentTests.SomeMethod') | Out-Null
    }

    $passingTrx = Join-Path $scratch 'passing.trx'
    New-SyntheticTrx -Path $passingTrx -Counters $requiredTrxCounters
    $trxEvidence = Get-DotNetTrxEvidence `
        -TrxPaths @($passingTrx) `
        -ExpectedDiscoveredTests $savedDiscoveryCount
    Assert-Equal -Expected 3 -Actual $trxEvidence.Total -Case 'TRX total count'
    Assert-Equal -Expected 3 -Actual $trxEvidence.Executed -Case 'TRX executed count'
    Assert-Equal -Expected 3 -Actual $trxEvidence.Passed -Case 'TRX passed count'
    Assert-Equal -Expected 0 -Actual $trxEvidence.Failed -Case 'TRX failed count'
    Assert-Equal -Expected 0 -Actual $trxEvidence.NotExecuted -Case 'TRX not-executed count'
    $recordedTrxLines = @(ConvertTo-CiTestEvidenceLines -Prefix 'TRX' -Evidence $trxEvidence)
    foreach ($counterName in $requiredTrxCounters.Keys) {
        if (-not ($recordedTrxLines | Where-Object { $_ -like "TRX$counterName=*" })) {
            throw "TRX evidence did not record counter '$counterName'."
        }
    }

    $abortedSummaryTrx = Join-Path $scratch 'aborted-summary.trx'
    New-SyntheticTrx `
        -Path $abortedSummaryTrx `
        -Counters $requiredTrxCounters `
        -ResultSummaryOutcomes @('Aborted')
    Assert-Rejected -Case 'TRX aborted result summary with forged counters' -ExpectedMessage 'ResultSummary outcome' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($abortedSummaryTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $failedResultTrx = Join-Path $scratch 'failed-result.trx'
    New-SyntheticTrx `
        -Path $failedResultTrx `
        -Counters $requiredTrxCounters `
        -UnitTestResultOutcomes @('Passed', 'Failed', 'Passed')
    Assert-Rejected -Case 'TRX failed UnitTestResult with forged counters' -ExpectedMessage "UnitTestResult outcome 'Failed'" -Action {
        Get-DotNetTrxEvidence -TrxPaths @($failedResultTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $notExecutedResultTrx = Join-Path $scratch 'not-executed-result.trx'
    New-SyntheticTrx `
        -Path $notExecutedResultTrx `
        -Counters $requiredTrxCounters `
        -UnitTestResultOutcomes @('Passed', 'NotExecuted', 'Passed')
    Assert-Rejected -Case 'TRX not-executed UnitTestResult with forged counters' -ExpectedMessage "UnitTestResult outcome 'NotExecuted'" -Action {
        Get-DotNetTrxEvidence -TrxPaths @($notExecutedResultTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $shortResultsTrx = Join-Path $scratch 'short-results.trx'
    New-SyntheticTrx `
        -Path $shortResultsTrx `
        -Counters $requiredTrxCounters `
        -UnitTestResultOutcomes @('Passed', 'Passed')
    Assert-Rejected -Case 'TRX result count smaller than forged counters' -ExpectedMessage 'UnitTestResult count' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($shortResultsTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $duplicateSummaryTrx = Join-Path $scratch 'duplicate-summary.trx'
    New-SyntheticTrx `
        -Path $duplicateSummaryTrx `
        -Counters $requiredTrxCounters `
        -ResultSummaryOutcomes @('Completed', 'Completed')
    Assert-Rejected -Case 'TRX duplicate result summaries' -ExpectedMessage 'exactly one ResultSummary' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($duplicateSummaryTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $skippedCounters = [ordered]@{} + $requiredTrxCounters
    $skippedCounters.executed = 2
    $skippedCounters.passed = 2
    $skippedCounters.notExecuted = 1
    $skippedTrx = Join-Path $scratch 'skipped.trx'
    New-SyntheticTrx -Path $skippedTrx -Counters $skippedCounters
    Assert-Rejected -Case 'TRX skipped or not-run test' -ExpectedMessage 'UnitTestResult count' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($skippedTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $failedCounters = [ordered]@{} + $requiredTrxCounters
    $failedCounters.passed = 2
    $failedCounters.failed = 1
    $failedTrx = Join-Path $scratch 'failed.trx'
    New-SyntheticTrx -Path $failedTrx -Counters $failedCounters
    Assert-Rejected -Case 'TRX failed test' -ExpectedMessage 'failed=1' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($failedTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    foreach ($counterName in @(
        'failed'
        'error'
        'timeout'
        'aborted'
        'inconclusive'
        'passedButRunAborted'
        'notRunnable'
        'notExecuted'
        'disconnected'
        'warning'
        'inProgress'
        'pending'
    )) {
        $nonzeroCounters = [ordered]@{} + $requiredTrxCounters
        $nonzeroCounters[$counterName] = 1
        $nonzeroTrx = Join-Path $scratch "$counterName.trx"
        New-SyntheticTrx -Path $nonzeroTrx -Counters $nonzeroCounters
        Assert-Rejected -Case "TRX $counterName counter" -ExpectedMessage "$counterName=1" -Action {
            Get-DotNetTrxEvidence -TrxPaths @($nonzeroTrx) -ExpectedDiscoveredTests 3 | Out-Null
        }
    }

    Assert-Rejected -Case 'TRX discovery mismatch' -ExpectedMessage 'discovered test count' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($passingTrx) -ExpectedDiscoveredTests 4 | Out-Null
    }

    $missingCounterCounters = [ordered]@{} + $requiredTrxCounters
    $null = $missingCounterCounters.Remove('pending')
    $missingCounterTrx = Join-Path $scratch 'missing-counter.trx'
    New-SyntheticTrx -Path $missingCounterTrx -Counters $missingCounterCounters
    Assert-Rejected -Case 'TRX missing required counter' -ExpectedMessage 'missing required counter' -Action {
        Get-DotNetTrxEvidence -TrxPaths @($missingCounterTrx) -ExpectedDiscoveredTests 3 | Out-Null
    }

    $workflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github\workflows\ci-cd.yml') -Raw -Encoding utf8
    foreach ($requiredHybridEvidence in @(
        '82,132'
        '15,000/15,000'
        '30.07%'
        'Validation: 0 hard errors, 1 warnings'
        '--title $releaseTitle'
    )) {
        if (-not $workflow.Contains($requiredHybridEvidence)) {
            throw "CI workflow is missing v1.4.0 hybrid release evidence: $requiredHybridEvidence"
        }
    }
}
finally {
    Remove-Module -Name 'Ci-TestEvidence.Core' -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

Write-Output 'PASS: CI test evidence accepts complete Python/.NET runs and rejects zero, skipped, failed, incomplete, malformed, and discovery-mismatched evidence.'
