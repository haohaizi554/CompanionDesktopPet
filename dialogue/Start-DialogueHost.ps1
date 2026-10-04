# Starts the dialogue host for the phone. The model stays on this computer.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repo "dialogue\python\Scripts\python.exe"
& $python (Join-Path $repo "dialogue\host.py")
