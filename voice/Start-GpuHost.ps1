# Starts the GPU voice host for the phone. Torch stays on this computer.
# Close the desktop pet first: its voice process already occupies the GPU.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repo "voice\python\python.exe"
$running = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
    Where-Object { $_.CommandLine -like "*infer_stdio.py*" }
if ($running) {
    throw "佳怡桌面版正在占用显卡。先退出桌面版，再启动给手机用的语音服务。"
}
& $python (Join-Path $repo "voice\gpu_host.py")
