Set-StrictMode -Version Latest

$script:RequiredTrxCounterNames = @(
    'total'
    'executed'
    'passed'
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
    'completed'
    'inProgress'
    'pending'
)

$script:ZeroRequiredTrxCounterNames = @(
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
)

$script:DotNetListedTestItemPattern = '^[\s]+(?<Name>[A-Za-z_][A-Za-z0-9_]*(?:[.+][A-Za-z_][A-Za-z0-9_]*){2,}(?:\([^\r\n]*\))?)\s*$'

function ConvertTo-CiTestEvidenceNonNegativeInteger {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value -notmatch '^(0|[1-9][0-9]*)$') {
        throw "$Context must be a non-negative integer; actual='$Value'."
    }

    try {
        return [Int64]::Parse(
            $Value,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "$Context is outside the supported integer range: '$Value'."
    }
}

function Get-PythonUnittestEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $terminalLines = @(
        foreach ($line in $Lines) {
            $trimmed = $line.Trim()
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $trimmed
            }
        }
    )
    if ($terminalLines.Count -eq 0) {
        throw 'Python unittest output has no terminal summary.'
    }

    $outcome = $terminalLines[$terminalLines.Count - 1]
    $outcomeMatch = [regex]::Match(
        $outcome,
        '^OK(?:\s*\((?<Details>[^)]*)\))?$')
    if (-not $outcomeMatch.Success) {
        throw "Python unittest did not report a successful terminal outcome: $outcome"
    }

    $runSummary = $null
    for ($index = $terminalLines.Count - 2; $index -ge 0; $index--) {
        $candidate = [regex]::Match(
            $terminalLines[$index],
            '^Ran\s+(?<Count>[0-9]+)\s+tests?\s+in\s+.+$')
        if ($candidate.Success) {
            $runSummary = $candidate
            break
        }
    }
    if ($null -eq $runSummary) {
        throw 'Python unittest terminal outcome has no preceding run summary.'
    }

    $testsRun = ConvertTo-CiTestEvidenceNonNegativeInteger `
        -Value $runSummary.Groups['Count'].Value `
        -Context 'Python unittest run count'
    if ($testsRun -le 0) {
        throw 'Python unittest reported zero tests.'
    }

    $counts = [ordered]@{
        Skipped = [Int64]0
        Failures = [Int64]0
        Errors = [Int64]0
        ExpectedFailures = [Int64]0
        UnexpectedSuccesses = [Int64]0
    }
    $details = $outcomeMatch.Groups['Details'].Value
    if (-not [string]::IsNullOrWhiteSpace($details)) {
        $detailParts = @($details -split ',\s*')
        foreach ($detail in $detailParts) {
            $detailMatch = [regex]::Match(
                $detail,
                '^(?<Name>skipped|failures|errors|expected failures|unexpected successes)=(?<Count>[0-9]+)$')
            if (-not $detailMatch.Success) {
                throw "Python unittest terminal outcome contains an unsupported detail: $detail"
            }

            $propertyName = switch ($detailMatch.Groups['Name'].Value) {
                'skipped' { 'Skipped' }
                'failures' { 'Failures' }
                'errors' { 'Errors' }
                'expected failures' { 'ExpectedFailures' }
                'unexpected successes' { 'UnexpectedSuccesses' }
                default { throw "Unsupported Python unittest detail: $detail" }
            }
            $counts[$propertyName] = ConvertTo-CiTestEvidenceNonNegativeInteger `
                -Value $detailMatch.Groups['Count'].Value `
                -Context "Python unittest $($detailMatch.Groups['Name'].Value) count"
        }
    }

    foreach ($entry in $counts.GetEnumerator()) {
        if ($entry.Value -ne 0) {
            throw "Python unittest did not execute a fully successful suite: $($entry.Key)=$($entry.Value)."
        }
    }

    return [pscustomobject][ordered]@{
        TestsRun = $testsRun
        Skipped = $counts.Skipped
        Failures = $counts.Failures
        Errors = $counts.Errors
        ExpectedFailures = $counts.ExpectedFailures
        UnexpectedSuccesses = $counts.UnexpectedSuccesses
    }
}

function Get-DotNetTestDiscoveryEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $listingHeader = 'The following Tests are available:'
    $insideListing = $false
    $foundListingHeader = $false
    $discoveredTestNames = @(
        foreach ($line in $Lines) {
            $candidate = $line.Trim()
            if ($candidate -ceq $listingHeader) {
                $foundListingHeader = $true
                $insideListing = $true
                continue
            }
            if (-not $insideListing) {
                continue
            }
            if ([string]::IsNullOrWhiteSpace($candidate)) {
                $insideListing = $false
                continue
            }
            $listedTest = [regex]::Match($line, $script:DotNetListedTestItemPattern)
            if (-not $listedTest.Success) {
                $insideListing = $false
                continue
            }
            $listedTest.Groups['Name'].Value
        }
    )
    if (-not $foundListingHeader) {
        throw 'The .NET test discovery output contains zero matching tests because the test listing header is missing.'
    }
    if ($discoveredTestNames.Count -le 0) {
        throw 'The .NET test discovery output contains zero matching tests.'
    }

    return [pscustomobject][ordered]@{
        DiscoveredTests = [Int64]$discoveredTestNames.Count
    }
}

function Save-CiTestEvidenceInteger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [Int64]$Value
    )

    if ($Value -lt 0) {
        throw "CI test evidence '$Name' cannot be negative: $Value."
    }

    $directory = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($directory) -or -not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "CI test evidence directory does not exist: $directory"
    }

    [IO.File]::WriteAllText(
        $Path,
        "$Name=$Value$([Environment]::NewLine)",
        [Text.UTF8Encoding]::new($false))
}

function Read-CiTestEvidenceInteger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "CI test evidence file does not exist: $Path"
    }

    $lines = @(
        Get-Content -LiteralPath $Path -Encoding utf8 |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $expectedLinePattern = '^{0}=(?<Value>[0-9]+)$' -f [regex]::Escape($Name)
    $matchingLines = @($lines | Where-Object { $_ -match $expectedLinePattern })
    if ($matchingLines.Count -ne 1 -or $lines.Count -ne 1) {
        throw "CI test evidence file must contain exactly one '$Name' value: $Path"
    }

    $match = [regex]::Match($matchingLines[0], $expectedLinePattern)
    return ConvertTo-CiTestEvidenceNonNegativeInteger `
        -Value $match.Groups['Value'].Value `
        -Context "CI test evidence $Name"
}

function Get-DotNetTrxEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$TrxPaths,

        [Parameter(Mandatory = $true)]
        [Int64]$ExpectedDiscoveredTests
    )

    if ($ExpectedDiscoveredTests -le 0) {
        throw "Expected discovered test count must be positive; actual=$ExpectedDiscoveredTests."
    }
    if ($TrxPaths.Count -le 0) {
        throw 'The .NET test run produced no TRX evidence.'
    }

    $totals = [ordered]@{}
    foreach ($counterName in $script:RequiredTrxCounterNames) {
        $totals[$counterName] = [Int64]0
    }

    foreach ($trxPath in $TrxPaths) {
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            throw "TRX evidence file does not exist: $trxPath"
        }

        try {
            [xml]$trx = [IO.File]::ReadAllText($trxPath)
        }
        catch {
            throw "TRX evidence file could not be parsed: $trxPath"
        }

        $resultSummaryNodes = @($trx.SelectNodes("//*[local-name()='ResultSummary']"))
        if ($resultSummaryNodes.Count -ne 1) {
            throw "TRX file must contain exactly one ResultSummary element: $trxPath"
        }
        $resultSummary = $resultSummaryNodes[0]
        $resultSummaryOutcome = $resultSummary.GetAttribute('outcome')
        if (-not $resultSummary.HasAttribute('outcome') -or $resultSummaryOutcome -cne 'Completed') {
            throw "TRX ResultSummary outcome must be Completed; actual='$resultSummaryOutcome': $trxPath"
        }

        $counterNodes = @($resultSummary.SelectNodes("./*[local-name()='Counters']"))
        if ($counterNodes.Count -ne 1) {
            throw "TRX file must contain exactly one counters element: $trxPath"
        }
        $counters = $counterNodes[0]

        $fileCounters = [ordered]@{}

        foreach ($counterName in $script:RequiredTrxCounterNames) {
            if (-not $counters.HasAttribute($counterName)) {
                throw "TRX file is missing required counter '$counterName': $trxPath"
            }
            $counterValue = ConvertTo-CiTestEvidenceNonNegativeInteger `
                -Value $counters.GetAttribute($counterName) `
                -Context "TRX counter '$counterName' in $trxPath"
            $fileCounters[$counterName] = $counterValue
            $totals[$counterName] += $counterValue
        }

        $unitTestResultNodes = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        if ($unitTestResultNodes.Count -ne $fileCounters.total -or $unitTestResultNodes.Count -ne $fileCounters.executed) {
            throw "TRX UnitTestResult count $($unitTestResultNodes.Count) must equal total $($fileCounters.total) and executed $($fileCounters.executed): $trxPath"
        }
        foreach ($unitTestResult in $unitTestResultNodes) {
            $outcome = $unitTestResult.GetAttribute('outcome')
            if (-not $unitTestResult.HasAttribute('outcome') -or $outcome -cne 'Passed') {
                throw "TRX UnitTestResult outcome '$outcome' must be Passed: $trxPath"
            }
        }
    }

    if ($totals.total -le 0) {
        throw 'TRX evidence reports zero tests.'
    }
    if ($totals.total -ne $ExpectedDiscoveredTests) {
        throw "TRX total $($totals.total) does not equal discovered test count $ExpectedDiscoveredTests."
    }
    if ($totals.executed -ne $totals.total) {
        throw "TRX executed count $($totals.executed) does not equal total $($totals.total)."
    }
    foreach ($counterName in $script:ZeroRequiredTrxCounterNames) {
        if ($totals[$counterName] -ne 0) {
            throw "TRX counter $counterName=$($totals[$counterName]) must be zero."
        }
    }
    if ($totals.passed -ne $totals.total) {
        throw "TRX passed count $($totals.passed) does not equal total $($totals.total)."
    }

    return [pscustomobject][ordered]@{
        TrxFiles = [Int64]$TrxPaths.Count
        ExpectedDiscoveredTests = $ExpectedDiscoveredTests
        Total = $totals.total
        Executed = $totals.executed
        Passed = $totals.passed
        Failed = $totals.failed
        Error = $totals.error
        Timeout = $totals.timeout
        Aborted = $totals.aborted
        Inconclusive = $totals.inconclusive
        PassedButRunAborted = $totals.passedButRunAborted
        NotRunnable = $totals.notRunnable
        NotExecuted = $totals.notExecuted
        Disconnected = $totals.disconnected
        Warning = $totals.warning
        Completed = $totals.completed
        InProgress = $totals.inProgress
        Pending = $totals.pending
    }
}

function ConvertTo-CiTestEvidenceLines {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix,

        [Parameter(Mandatory = $true)]
        [psobject]$Evidence
    )

    foreach ($property in $Evidence.PSObject.Properties) {
        "$Prefix$($property.Name)=$($property.Value)"
    }
}

Export-ModuleMember -Function @(
    'Get-PythonUnittestEvidence'
    'Get-DotNetTestDiscoveryEvidence'
    'Save-CiTestEvidenceInteger'
    'Read-CiTestEvidenceInteger'
    'Get-DotNetTrxEvidence'
    'ConvertTo-CiTestEvidenceLines'
)
