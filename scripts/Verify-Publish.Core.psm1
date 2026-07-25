function Get-PublishSmokeDefaultTimeoutSeconds {
    [CmdletBinding()]
    param()

    return 30
}

function Invoke-PublishSmokeTest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 120)]
        [int]$TimeoutSeconds
    )

    $process = $null
    $processId = $null
    $cleanupFailure = $null
    $smokeFailure = $null

    try {
        try {
            $process = Start-Process `
                -FilePath $ExePath `
                -ArgumentList '--smoke-test' `
                -WorkingDirectory $WorkingDirectory `
                -WindowStyle Hidden `
                -PassThru
            $processId = $process.Id

            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $smokeFailure = "Smoke-test timed out after $TimeoutSeconds seconds; forced termination is cleanup only."
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

        if ($null -ne $processId -and
            $null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            throw "Desktop pet PID $processId is still running after smoke test cleanup."
        }

        if ($null -ne $smokeFailure) {
            throw $smokeFailure
        }

        return [pscustomobject]@{
            ProcessId = $processId
            ExitCode = 0
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

Export-ModuleMember -Function @(
    'Get-PublishSmokeDefaultTimeoutSeconds'
    'Invoke-PublishSmokeTest'
)
