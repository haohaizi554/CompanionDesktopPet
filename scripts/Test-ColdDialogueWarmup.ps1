param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tests\CompanionDesktopPet.WarmupProbe\CompanionDesktopPet.WarmupProbe.csproj"

dotnet run --project $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Cold dialogue warmup probe failed with exit code $LASTEXITCODE."
}
