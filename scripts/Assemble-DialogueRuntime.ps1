$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pythonRoot = Join-Path $root "dialogue\python"
$python = Join-Path $pythonRoot "Scripts\python.exe"
if (-not (Test-Path $python)) {
    py -3 -m venv $pythonRoot
}
& $python -m pip install --upgrade pip
& $python -m pip install -r (Join-Path $root "dialogue\requirements.txt")
Write-Output "dialogue runtime ready"
