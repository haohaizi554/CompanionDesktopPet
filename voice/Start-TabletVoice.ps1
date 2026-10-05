# Launches the tablet voice path: this PC's GPU host, then the frpc tunnel.
# Running it again only starts what is missing. It does not stop an existing host.
param([switch]$NoPause)

$ErrorActionPreference = "Stop"
try {
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
    & "$env:SystemRoot\System32\chcp.com" 65001 > $null
    $Host.UI.RawUI.WindowTitle = "佳怡平板语音"
} catch {
}

$repo = Split-Path -Parent $PSScriptRoot
$voiceDir = Join-Path $repo "voice"
$python = Join-Path $voiceDir "python\python.exe"
$gpuHost = Join-Path $voiceDir "gpu_host.py"
$manifestPath = Join-Path $voiceDir "packs\jiayi\manifest.json"
$frpc = Join-Path $env:LOCALAPPDATA "CompanionDesktopPet\frp\frp_0.61.1_windows_amd64\frpc.exe"
$frpcConfig = Join-Path $env:LOCALAPPDATA "CompanionDesktopPet\frp\frpc.toml"
$problems = New-Object System.Collections.Generic.List[string]

function Write-Status([string]$name, [string]$state, [string]$detail) {
    Write-Host ("{0,-6}{1}" -f $name, $state)
    if ($detail) {
        Write-Host ("       {0}" -f $detail)
    }
}

function Get-CommandLines([string]$imageName) {
    $rows = Get-CimInstance Win32_Process -Filter "Name = '$imageName'" -ErrorAction SilentlyContinue
    foreach ($row in @($rows)) {
        if ($row.ProcessId -and $row.CommandLine) {
            [pscustomobject]@{
                ProcessId = [int]$row.ProcessId
                CommandLine = [string]$row.CommandLine
            }
        }
    }
}

function Get-ListenerPids([int]$port) {
    $pattern = ":$port\s"
    $found = New-Object System.Collections.Generic.List[int]
    $lines = & "$env:SystemRoot\System32\netstat.exe" -ano -p tcp
    foreach ($line in @($lines)) {
        if ($line -notmatch "LISTENING") { continue }
        if ($line -notmatch $pattern) { continue }
        $parts = ($line -split "\s+") | Where-Object { $_ }
        $owner = 0
        if ([int]::TryParse([string]$parts[-1], [ref]$owner) -and $owner -gt 0 -and -not $found.Contains($owner)) {
            $found.Add($owner) | Out-Null
        }
    }
    return @($found)
}

function Get-ProcessText([int]$processId) {
    $row = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
    if (-not $row) { return "未知程序 ($processId)" }
    $command = ([string]$row.CommandLine).Trim()
    if (-not $command) { $command = [string]$row.Name }
    if ($command.Length -gt 140) { $command = $command.Substring(0, 140) + "…" }
    "{0} ({1})" -f $command, $processId
}

function Get-VoiceView {
    $hosts = New-Object System.Collections.Generic.List[object]
    foreach ($row in @(Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" -ErrorAction SilentlyContinue)) {
        if ([string]$row.CommandLine -notlike "*gpu_host.py*") { continue }
        $parentId = [int]$row.ParentProcessId
        $parent = $null
        if ($parentId -gt 0) {
            $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $parentId" -ErrorAction SilentlyContinue
        }
        $hosts.Add([pscustomobject]@{
            ProcessId = [int]$row.ProcessId
            ParentProcessId = $parentId
            HasConsole = [bool]($parent -and $parent.Name -eq "cmd.exe")
            Started = [datetime]$row.CreationDate
        }) | Out-Null
    }
    $listeners = @(Get-ListenerPids 8765)
    $hostIds = @($hosts.ProcessId)
    $foreign = @($listeners | Where-Object { $hostIds -notcontains $_ })
    $foreignText = ""
    if ($foreign.Count -gt 0) {
        $names = foreach ($foreignId in $foreign) { Get-ProcessText $foreignId }
        $foreignText = "8765 上还有：" + ($names -join "；")
    }
    [pscustomobject]@{
        Hosts = [object[]]$hosts.ToArray()
        Listening = @($listeners | Where-Object { $hostIds -contains $_ }).Count -gt 0
        Foreign = [int[]]$foreign
        ForeignText = $foreignText
    }
}

function Stop-VoiceTree([object[]]$hosts) {
    $hostIds = @($hosts | ForEach-Object { [int]$_.ProcessId })
    $infer = @(Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -like "*infer_stdio.py*" -and $hostIds -contains [int]$_.ParentProcessId
        })
    foreach ($row in $infer) {
        Stop-Process -Id ([int]$row.ProcessId) -Force -ErrorAction SilentlyContinue
    }
    foreach ($id in $hostIds) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 400
        $view = Get-VoiceView
        $listeners = @(Get-ListenerPids 8765)
        if (@($view.Hosts).Count -eq 0 -and $listeners.Count -eq 0) { return $true }
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-VoiceWaitDetail($view, $health) {
    $hostId = $view.Hosts[0].ProcessId
    $window = if ($view.Hosts[0].HasConsole) {
        "看标题为「佳怡语音」的窗口。"
    } else {
        "原来的窗口已经不在。"
    }
    if (-not $view.Listening) {
        return "进程 $hostId 在，8765 还没开始听。模型装进显卡通常要一两分钟。$window"
    }
    if ($health -and $health.Known) {
        if ($health.Error) { return [string]$health.Error }
        return "进程 $hostId 已经在听 8765，模型还在装进显卡。$window"
    }
    if ($health -and $health.Reached) { return "8765 有回应，但不是佳怡语音。$window" }
    $reason = if ($health -and $health.Detail) { [string]$health.Detail } else { "没有回应" }
    "进程 $hostId 在听 8765，但连接被关掉（$reason）。$window"
}

function Wait-ForLocalVoice {
    $started = Get-Date
    $deadline = $started.AddMinutes(4)
    $lastDetail = ""
    $lastPrint = [datetime]::MinValue
    while ($true) {
        $view = Get-VoiceView
        if (@($view.Hosts).Count -eq 0) {
            if (((Get-Date) - $started).TotalSeconds -lt 15) {
                Start-Sleep -Seconds 1
                continue
            }
            Write-Status "语音" "没有留住" "「佳怡语音」窗口里有报错，进程已经退出。"
            $problems.Add("语音") | Out-Null
            return $null
        }
        if (@($view.Foreign).Count -gt 0) {
            Write-Status "语音" "端口被挡住" $view.ForeignText
            $problems.Add("语音") | Out-Null
            return $null
        }
        $health = $null
        if ($view.Listening) {
            $health = Get-VoiceHealth "http://127.0.0.1:8765/health" 4000
            if ($health.Known -and $health.Ok) {
                Write-Status "语音" "已就绪" $health.Gpu
                $script:voiceReady = $true
                return $health
            }
        }
        $detail = Get-VoiceWaitDetail $view $health
        $now = Get-Date
        if ($detail -ne $lastDetail -or ($now - $lastPrint).TotalSeconds -ge 10) {
            $waited = [int]($now - $started).TotalSeconds
            Write-Status "语音" "还在加载" ("已等 {0} 秒。{1}" -f $waited, $detail)
            $lastDetail = $detail
            $lastPrint = $now
        }
        if ($now -ge $deadline) {
            Write-Status "语音" "还没就绪" $detail
            $problems.Add("语音") | Out-Null
            return $health
        }
        Start-Sleep -Seconds 2
    }
}

function Read-FrpAddress([string]$path) {
    $address = $null
    $port = $null
    foreach ($line in @(Get-Content -LiteralPath $path -Encoding UTF8 -ErrorAction Stop)) {
        if ($line -match '^\s*#' ) { continue }
        if ($line -match '^\s*serverAddr\s*=\s*"([^"]+)"') { $address = $Matches[1] }
        if ($line -match '^\s*remotePort\s*=\s*(\d+)\s*$') { $port = [int]$Matches[1] }
    }
    if (-not $address -or -not $port) { return $null }
    "http://{0}:{1}" -f $address, $port
}

function Get-VoiceHealth([string]$url, [int]$timeoutMs) {
    $result = [ordered]@{
        Reached = $false
        Status = 0
        Known = $false
        Ok = $false
        Gpu = ""
        Error = ""
        Detail = ""
    }
    $request = [System.Net.HttpWebRequest]::Create($url)
    $request.Proxy = $null
    $request.Timeout = $timeoutMs
    $request.ReadWriteTimeout = $timeoutMs
    $request.AllowAutoRedirect = $false
    $request.UserAgent = "jiayi-voice-launcher"
    $response = $null
    $text = ""
    try {
        $response = $request.GetResponse()
    } catch [System.Net.WebException] {
        $result.Detail = [string]$_.Exception.Message
        if ($result.Detail.Length -gt 80) { $result.Detail = $result.Detail.Substring(0, 80) }
        $response = $_.Exception.Response
        if (-not $response) {
            if (-not $result.Detail) { $result.Detail = "没有回应" }
            return [pscustomobject]$result
        }
    }
    try {
        $result.Status = [int]$response.StatusCode
        $result.Reached = $true
        $stream = $response.GetResponseStream()
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            try { $text = $reader.ReadToEnd() } finally { $reader.Close() }
        }
    } finally {
        if ($response) { $response.Close() }
    }
    if (-not $text) { return [pscustomobject]$result }
    try {
        $payload = $text | ConvertFrom-Json
    } catch {
        $result.Detail = "8765 上不是佳怡语音"
        return [pscustomobject]$result
    }
    if ($null -eq $payload.ok -or $null -eq $payload.device) {
        $result.Detail = "8765 上不是佳怡语音"
        return [pscustomobject]$result
    }
    $result.Known = $true
    $result.Ok = [bool]$payload.ok
    $result.Gpu = [string]$payload.gpu
    $result.Error = [string]$payload.error
    [pscustomobject]$result
}

function Start-Console([string]$title, [string]$file, [string[]]$arguments, [string]$workingDirectory) {
    $quoted = foreach ($argument in @($file) + @($arguments)) {
        '"' + ($argument -replace '"', '\"') + '"'
    }
    $command = "title $title & " + ($quoted -join " ")
    Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" `
        -ArgumentList @("/k", $command) `
        -WorkingDirectory $workingDirectory `
        -WindowStyle Normal | Out-Null
}

function Test-RequiredFile([string]$label, [string]$path) {
    if (Test-Path -LiteralPath $path -PathType Leaf) { return $true }
    Write-Status $label "找不到文件" $path
    $problems.Add($label) | Out-Null
    return $false
}

Write-Host ""
Write-Host "佳怡平板语音"
Write-Host ""
Write-Host "平板对话走公网模型。"
Write-Host "这台电脑负责显卡语音，再转发到平板。"
Write-Host ""

$voiceReadyToStart = (Test-RequiredFile "语音" $python) -and (Test-RequiredFile "语音" $gpuHost)
$weightPath = $null
if ($voiceReadyToStart -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
        $weightPath = Join-Path (Join-Path $voiceDir "packs\jiayi") ([string]$manifest.gpt)
        $sovitsPath = Join-Path (Join-Path $voiceDir "packs\jiayi") ([string]$manifest.sovits)
        if (-not (Test-Path -LiteralPath $weightPath -PathType Leaf) -or -not (Test-Path -LiteralPath $sovitsPath -PathType Leaf)) {
            Write-Status "语音" "模型不完整" (Join-Path $voiceDir "packs\jiayi\weights")
            $problems.Add("语音") | Out-Null
            $voiceReadyToStart = $false
        }
    } catch {
        Write-Status "语音" "清单读不了" $manifestPath
        $problems.Add("语音") | Out-Null
        $voiceReadyToStart = $false
    }
} elseif ($voiceReadyToStart) {
    Write-Status "语音" "找不到清单" $manifestPath
    $problems.Add("语音") | Out-Null
    $voiceReadyToStart = $false
}

$localHealth = $null
$script:voiceReady = $false
if ($voiceReadyToStart) {
    $view = Get-VoiceView
    $needsStart = $false
    if (@($view.Hosts).Count -gt 1) {
        Write-Status "语音" "有多份进程" ("已有 {0} 个语音进程，没有再开" -f @($view.Hosts).Count)
        $problems.Add("语音") | Out-Null
    } elseif (@($view.Foreign).Count -gt 0 -and @($view.Hosts).Count -eq 0) {
        Write-Status "语音" "端口被占用" ("{0}。没有再开一份。" -f $view.ForeignText)
        $problems.Add("语音") | Out-Null
    } elseif (@($view.Hosts).Count -eq 0) {
        $needsStart = $true
    } else {
        $health = $null
        if ($view.Listening) {
            $health = Get-VoiceHealth "http://127.0.0.1:8765/health" 4000
            if (-not $health.Reached) {
                Start-Sleep -Seconds 1
                $health = Get-VoiceHealth "http://127.0.0.1:8765/health" 4000
            }
        }
        $healthy = $health -and $health.Known -and $health.Ok
        $booting = $health -and $health.Known -and -not $health.Ok
        $ageMinutes = 0.0
        if ($view.Hosts[0].Started -gt [datetime]"2000-01-01") {
            $ageMinutes = ((Get-Date) - $view.Hosts[0].Started).TotalMinutes
        }
        $stuck = $view.Listening -and $health -and -not $health.Reached
        $tooOldWithoutPort = -not $view.Listening -and $ageMinutes -ge 3
        $abandoned = -not $view.Hosts[0].HasConsole -and -not $view.Listening -and $ageMinutes -ge 1
        if ($healthy) {
            $note = [string]$health.Gpu
            if (-not $view.Hosts[0].HasConsole) { $note = "进程还在，窗口已经关掉。$note" }
            Write-Status "语音" "已就绪" $note
            $localHealth = $health
            $script:voiceReady = $true
        } elseif (@($view.Foreign).Count -gt 0) {
            Write-Status "语音" "端口被挡住" ("佳怡的进程在 ({0})，但 {1}。先关掉那个程序。" -f $view.Hosts[0].ProcessId, $view.ForeignText)
            $problems.Add("语音") | Out-Null
        } elseif ($stuck -or $tooOldWithoutPort -or $abandoned) {
            $why = if (-not $view.Hosts[0].HasConsole) {
                "原来的窗口已经不在，服务也没有回应。"
            } else {
                "窗口在，但健康检查进不去。"
            }
            Write-Status "语音" "卡住了" "$why 正在关掉并重新打开。"
            if (Stop-VoiceTree $view.Hosts) {
                $needsStart = $true
            } else {
                Write-Status "语音" "关不掉" "旧进程还占着，没有再开一份。"
                $problems.Add("语音") | Out-Null
            }
        }
    }
    if ($needsStart) {
        Write-Status "语音" "正在打开" "新窗口标题是「佳怡语音」。桌面版若占着显卡，它会在那个窗口里等。"
        Start-Console "佳怡语音" $python @($gpuHost) $repo
        $localHealth = Wait-ForLocalVoice
    } elseif (-not $script:voiceReady -and -not $problems.Contains("语音")) {
        $localHealth = Wait-ForLocalVoice
    }
}

$publicBase = $null
$frpcReady = (Test-RequiredFile "转发" $frpc) -and (Test-RequiredFile "转发" $frpcConfig)
if ($frpcReady) {
    try {
        $publicBase = Read-FrpAddress $frpcConfig
    } catch {
        $publicBase = $null
    }
    if (-not $publicBase) {
        Write-Status "转发" "配置不完整" "frpc.toml 里没有服务器地址或远程端口。"
        $problems.Add("转发") | Out-Null
        $frpcReady = $false
    }
}

$frpcRows = @()
if ($frpcReady) {
    $configLeaf = [IO.Path]::GetFileName($frpcConfig)
    $frpcRows = @(Get-CommandLines "frpc.exe")
    $matching = @($frpcRows | Where-Object { $_.CommandLine -like "*$configLeaf*" })
    if ($frpcRows.Count -eq 0) {
        Write-Status "转发" "正在打开" $publicBase
        Start-Console "佳怡frp" $frpc @("-c", $frpcConfig) (Split-Path -Parent $frpc)
        Start-Sleep -Seconds 2
        $frpcRows = @(Get-CommandLines "frpc.exe" | Where-Object { $_.CommandLine -like "*$configLeaf*" })
        if ($frpcRows.Count -eq 0) {
            Write-Status "转发" "没有留住" "「佳怡frp」窗口里有报错。"
            $problems.Add("转发") | Out-Null
        }
    } elseif ($matching.Count -eq 0) {
        Write-Status "转发" "有别的 frpc" "正在运行的不是这份配置，没有再开一个。"
        $problems.Add("转发") | Out-Null
    } elseif ($matching.Count -gt 1) {
        Write-Status "转发" "有多份进程" ("已有 {0} 个 frpc，没有再开" -f $matching.Count)
    }
}

if ($frpcReady -and $publicBase -and $localHealth -and $localHealth.Known) {
    $publicHealth = Get-VoiceHealth ($publicBase.TrimEnd("/") + "/health") 8000
    if ($publicHealth.Known) {
        if ($publicHealth.Ok) {
            Write-Status "转发" "公网已通" $publicBase
        } else {
            Write-Status "转发" "公网已通" ("语音还在等待。{0}" -f $publicBase)
        }
    } else {
        Write-Status "转发" "公网没通" ("本机语音在，{0} 没有回应。看「佳怡frp」窗口。" -f $publicBase)
        $problems.Add("转发") | Out-Null
    }
} elseif ($frpcReady -and $frpcRows.Count -gt 0 -and $publicBase) {
    Write-Status "转发" "已打开" ("等本机语音起来后，平板再用 {0}" -f $publicBase)
}

Write-Host ""
if ($publicBase) {
    Write-Host ("平板地址  {0}" -f $publicBase)
} else {
    Write-Host "平板地址  还不能确定"
}
Write-Host ""
if ($problems.Count -eq 0 -and $script:voiceReady) {
    Write-Host "这个窗口可以关掉。"
    Write-Host "语音和转发在各自的窗口里。"
} elseif (-not $script:voiceReady) {
    Write-Host "语音还没到「已就绪」。"
    Write-Host "请留着标题为「佳怡语音」的窗口，里面能看到它卡在哪一步。"
} else {
    Write-Host "上面有一项没就绪。这个窗口可以关掉，已打开的语音和转发会继续留着。"
}
Write-Host ""
if (-not $NoPause) {
    Write-Host "按任意键关闭这个窗口。"
    try { [void][Console]::ReadKey($true) } catch { }
}
if ($problems.Count -gt 0) { exit 1 }
exit 0
