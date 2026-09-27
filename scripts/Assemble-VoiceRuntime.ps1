# Copies the inference runtime out of the GPT-SoVITS checkout into voice\python
# and voice\engine, then publishes an exe folder that does not point back at it.
param(
    [string]$Source = "D:\desktop\GPT-SoVITS-v2pro-20250604",
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$voice = Join-Path $repo "voice"
$pythonDest = Join-Path $voice "python"
$engine = Join-Path $voice "engine"
$runtimeJson = Join-Path $voice "runtime.json"
if (Test-Path -LiteralPath $runtimeJson) {
    Remove-Item -LiteralPath $runtimeJson -Force
}

if (-not (Test-Path -LiteralPath (Join-Path $Source "runtime\python.exe"))) {
    throw "GPT-SoVITS runtime was not found at $Source"
}

function Invoke-Robocopy {
    param([string]$From, [string]$To, [string[]]$Extra = @())
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    & robocopy $From $To /E /NFL /NDL /NP /MT:8 /XD __pycache__ @Extra
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed ($LASTEXITCODE): $From -> $To"
    }
}

Write-Host "Copying Python runtime"
$unusedPackages = @(
    "gradio", "gradio_client", "gradio_pdf",
    "mecab_ko_dic", "eunjeon", "pyopenjtalk", "ipadic",
    "cmake", "pyarrow", "pandas", "pymupdf",
    "ctranslate2", "faiss", "faiss_cpu"
)
Invoke-Robocopy (Join-Path $Source "runtime") $pythonDest (@("/XD") + $unusedPackages)

Write-Host "Copying inference code"
$engineSource = Join-Path $Source "GPT_SoVITS"
foreach ($name in @("AR", "BigVGAN", "configs", "eres2net", "feature_extractor", "module", "text", "TTS_infer_pack")) {
    Invoke-Robocopy (Join-Path $engineSource $name) (Join-Path $engine "GPT_SoVITS\$name")
}
foreach ($file in @("sv.py", "process_ckpt.py")) {
    Copy-Item -LiteralPath (Join-Path $engineSource $file) -Destination (Join-Path $engine "GPT_SoVITS\$file") -Force
}
Invoke-Robocopy (Join-Path $Source "tools\i18n") (Join-Path $engine "tools\i18n")
Copy-Item -LiteralPath (Join-Path $Source "tools\audio_sr.py") -Destination (Join-Path $engine "tools\audio_sr.py") -Force
Invoke-Robocopy (Join-Path $Source "tools\AP_BWE_main") (Join-Path $engine "tools\AP_BWE_main")

Write-Host "Copying feature models"
$pretrained = Join-Path $engine "GPT_SoVITS\pretrained_models"
foreach ($name in @("chinese-roberta-wwm-ext-large", "chinese-hubert-base", "fast_langdetect")) {
    Invoke-Robocopy (Join-Path $engineSource "pretrained_models\$name") (Join-Path $pretrained $name)
}
New-Item -ItemType Directory -Force -Path (Join-Path $pretrained "sv") | Out-Null
Copy-Item -LiteralPath (Join-Path $engineSource "pretrained_models\sv\pretrained_eres2netv2w24s4ep4.ckpt") `
    -Destination (Join-Path $pretrained "sv\pretrained_eres2netv2w24s4ep4.ckpt") -Force

if ($PublishDir -eq "") {
    Write-Host "Bundle is in $voice"
    return
}

Write-Host "Publishing exe to $PublishDir"
dotnet publish (Join-Path $repo "src\CompanionDesktopPet\CompanionDesktopPet.csproj") `
    -c Release -o $PublishDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}
Invoke-Robocopy $pythonDest (Join-Path $PublishDir "voice\python")
Invoke-Robocopy $engine (Join-Path $PublishDir "voice\engine")
Invoke-Robocopy (Join-Path $voice "packs") (Join-Path $PublishDir "voice\packs")
Copy-Item -LiteralPath (Join-Path $voice "infer_stdio.py") -Destination (Join-Path $PublishDir "voice\infer_stdio.py") -Force
Write-Host "Standalone folder: $PublishDir"
