# 佳怡桌宠（CompanionDesktopPet）

Windows x64 WPF 桌宠，以及配套的可审计中文角色语料系统。自动台词、点击、拖动、菜单和托盘不读取输入内容、剪贴板、窗口标题，也不枚举或读取用户文件名、用户目录内容。没有热更新、自动更新或联网下载代码的机制；升级时由用户手动下载并替换程序。正常运行时，角色偏好、冷却历史和剧情状态保存在 `%LOCALAPPDATA%\CompanionDesktopPet`；只有用户主动启用开机自启动时，才会另在当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 项保存桌宠自身的 EXE 路径。`--smoke-test` 发布验证使用系统临时目录中的独立状态，并在退出时清理。

对话开关默认关闭。打开之后，用户写下的句子会发给用户自己填写的 OpenAI 兼容接口，可以是本机的 Ollama、LM Studio、llama.cpp、vLLM，也可以是另一台电脑，或 OpenAI、DeepSeek 及其他兼容服务。语音仍在本机用 GPT-SoVITS 合成，不把声音交给那个接口。不打开对话时，桌宠不发起这些请求。

自动台词使用四个精确的随机间隔窗口：本地时间 `06:00–17:59:59` 为 `5–15` 分钟，`18:00–22:59:59` 为 `10–20` 分钟，`23:00–05:59:59` 为 `30–60` 分钟；当前台前窗口明确检测为全屏时，不分时段改用 `60–120` 分钟。上下界均可被取到。全屏探测失败或台前 HWND 在读取期间变化时，原始观测保持 `unknown`，不会伪造为“非全屏”；有效安静模式会保留最近一次明确观测，直到后续明确结果更新它。

全屏探测只读取台前 HWND 及其有效性、可见/最小化状态和窗口样式，DWM 的 cloaked 状态与扩展边框几何，以及相交显示器的完整边界。它不读取窗口标题、进程名称或进程内容，不读取键鼠输入、剪贴板、用户文件、屏幕像素或网络数据。该探测只改变自动台词频率，不理解屏幕内容或用户正在做什么。

> 运行库仍是 v1.4.0 合并后的结果：30,000 条逐条 authored 文案与 v1.2.1 已审计的 52,132 条 legacy 运行时，合计 82,132 条、1,723 个语义场景，SHA-256 为 `339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece`。v1.5.0 与 v1.6.0 都沿用这份库。调度器先执行全部安全门禁，再以最近 100 次播放为窗口把 legacy 暴露稳定在约 30%；库存更大不会让 legacy 主导人格。

左键点击会显示爱心、按点击位置向相反方向轻轻倾斜，并给出一句回复。点击回复有长期运行兜底：即使冷却历史逐渐累积，或从旧版容易陷入静默的本地记忆恢复，后续点击也不会永久失声。桌宠保留自然单次/偶发双次眨眼，启动后会显示一次本地“嗨♡”，也可从右键面板选择 `打个招呼♡`；这些都是纯本地 UI 动作，不由语料驱动。

窗口先显示，再在后台异步加载记忆、语料和场景。完整语料尚未就绪时，启动与点击立即使用短小的内置本地 fallback，不阻塞 UI；自动播报可以保持安静。预热成功后切换到完整场景，瞬态失败按 1、5、30 秒退避重试，结构或隐私契约错误则保持 fallback 并阻止发布烟测把它误判为“完整语料已就绪”。

## 当前公开版本

最新 Release 是 [v1.6.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.6.0)，2026-09-27 发布。标签指向 `6d8eebf1487c4bd08121dc71165e785cf39ba95e`，主程序 `ProductVersion` 为 `1.6.0+6d8eebf1487c4bd08121dc71165e785cf39ba95e`。这是带本地语音的安装版：装完后同一目录里有主程序、语音运行时和对话程序。安装包由本机 Inno Setup 6.7.3 从当时的 `outputs\standalone` 打出，不由下面的单 EXE 流水线生成。安装程序和主程序都没有 Authenticode 签名，从网络下载时 Windows SmartScreen 或安全软件可能提示信誉不足。

GitHub 单个附件不能超过 2GB，所以安装包分成四段。四段都要下到同一个文件夹，再运行 `Jiayi-Desktop-Pet-Setup.exe`。缺任何一段都装不完。`SHA256SUMS.txt` 中的哈希是：

```text
1cd5ed1a5627bd414ea305cca3f32ebba502bc92cf8d8c412e3f60887eb9f1c0  Jiayi-Desktop-Pet-Setup.exe
f9dc4b6030bfadb58fc87ab05768c33bf4d159e303729e254ec217415fbb6a35  Jiayi-Desktop-Pet-Setup-1.bin
6b03aaa854e2de9198cecae09f9e011b299137cdb8a7185766add4d3256717ff  Jiayi-Desktop-Pet-Setup-2.bin
741102f361755e0a988a6a6e2a84b75939e6996aeea6c6116a8f618109bcb1ab  Jiayi-Desktop-Pet-Setup-3.bin
```

上一版 [v1.5.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.5.0) 仍是离线单 EXE，2026-09-24 发布，提交 `780d4b97354268b2a1f956e93c2605e15d4d6855`。`ProductVersion=1.5.0+780d4b97354268b2a1f956e93c2605e15d4d6855`，大小为 `84,063,826` 字节，SHA-256 为 `ccb4a6416b7ad4d8ecfd42ff23b81a37094a72f2d064109d2b869cbcd38ec4c7`。这一版把右键菜单放到人物旁边，开发者模式可以按分类查看语料，测试参数放在「开发者模式 → 测试参数」里，只影响这一次运行，不改语料文件。

[v1.4.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.4.0) 是这份 82,132 条运行库的混合运行时发布。EXE 从提交 `d30e0102a3896b173ce1554b6769a705158effbe` 使用 .NET SDK `9.0.301` 构建，`ProductVersion=1.4.0+d30e0102a3896b173ce1554b6769a705158effbe`，大小为 `84,047,575` 字节，SHA-256 为 `19472bed0ea847b90cd73274b66c00d420cf4884ea2ae32ad4984029b3247387`。标签流水线当时通过 Python `390/390`、.NET Release `645/645`、15,000 次模拟与完整打包门禁。完整证据见[发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。`docs/release/v1.4.0.md` 只记录那一次标签。

仓库里的单 EXE 构建产物仍然是：

```text
outputs/CompanionDesktopPet/佳怡桌宠.exe
outputs/CompanionDesktopPet/使用说明.txt
```

`佳怡桌宠.exe` 是 `win-x64` 自包含单文件应用，运行时不需要另行安装 .NET，也不依赖旁置或外部应用 DLL、JSON、PDB 等运行时 sidecar。“自包含单 EXE”不表示进程绝不加载 DLL；作为 Windows 桌面应用，它仍会正常使用操作系统提供的系统 DLL 与系统组件。这条单 EXE 不包含语音权重、GPT-SoVITS 运行时或对话用的 Python 环境。这些文件在 v1.6.0 安装包里；源码树用 `.gitignore` 排除 `voice/python/`、`voice/engine/`、`dialogue/python/` 和 `config/llm.runtime.json`。

当前 `main` 可以比 v1.6.0 标签更靠前。标签记录的是已发布安装包的构建提交，不是仓库里的每一个后续提交。

## 体验与操作

- 左键单击人物：显示爱心、按点击位置向相反方向轻轻倾斜，并按当前场景说一句话。
- 按住左键拖动：移动桌宠，移动时按方向倾斜，松手后回弹。
- 气泡与人物之间保持 30 DIP 的视觉距离；鼠标停在人物或气泡上时，只暂停当前气泡剩余的消失倒计时，移开后从剩余时间继续。
- 右键人物：在人物旁边打开卡哇伊风格控制面板，可说句话、`打个招呼♡`、暂停/继续动画、调整大小、切换置顶、设置开机自启动、恢复位置、藏到托盘或退出。v1.5.0 起菜单不再挡住佳怡；开发者模式可以按分类查看语料，测试参数在「开发者模式 → 测试参数」里。
- 本地语音：v1.6.0 安装版在本机合成。连续点击会立刻把气泡换成最新一句，还没开始推理的旧句子不再接着读。把她明显挪开再松开，不会当成想让她说话；原地点击仍会说话。没有语音运行时的单 EXE 会保持安静的文字气泡。
- 对话：默认关闭，开关在本地语音旁边。打开后可以用输入框说话，也可以用对话技能改间隔、气泡、语速和菜单状态。审计不过的模型原文不会拿去朗读。
- 托盘：双击图标切换显示/隐藏；右键菜单可显示/隐藏、说句话、暂停/继续、切换开机自启动或退出。
- v1.1.0 已支持 Windows 高对比度模式：气泡与控制面板会采用系统颜色和无阴影样式，关闭后恢复卡哇伊主题。

暂停会复位并暂停待机动作、自动眨眼和问候；点击爱心与左右倾斜、拖动/落地、手动说话和托盘操作仍可使用。

## 托盘与窗口恢复

如果托盘图标异常、窗口被藏起后找不到，重新运行同一个 `佳怡桌宠.exe` 即可把已经运行的窗口强制恢复到正常状态并激活；不会再启动一只重复桌宠。正常退出会先冻结交互、尽力保存设置与角色记忆，再清理托盘图标并关闭进程。

## 开机自启动

开机自启动默认关闭，只会在用户主动勾选后写入当前用户注册表：

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
值名：CompanionDesktopPet
```

它不会写入系统级启动项。若勾选后移动或重命名了 EXE，请先关闭“开机自启动”，再重新开启一次，让保存的路径更新为新位置。

正常运行时，本机偏好、冷却历史和微剧情进度保存在 `%LOCALAPPDATA%\CompanionDesktopPet`；开机自启动的当前用户注册表项与发布烟测的临时目录是上文已经明确说明的两个独立边界。

## 已验证能力

- 冻结并校验 75,375 条无表头不可变源物理数据行及其字节副本；它们只用于审计、复核和来源映射。
- legacy 分区保留 v1.2.1 的 806 条 curated 内容与 51,326 条 hash-bound surface，合计 52,132 条；authored 分区仍为 100 个批次、每批 300 条。
- 100 个 authored 批次各 300 条，合计 30,000 条；manifest 绑定每批文本与元数据摘要，ledger 逐行绑定 variant、关系画像与根哈希。
- 当前运行时精确为 82,132 条：30,000 条 authored 加 52,132 条 legacy，按唯一 `semantic_group` 聚合为 1,723 个场景。
- 来源档位由 `source_kind` 派生；最近 100 次播放的 legacy 总体门禁为 25%–35%，正式 100-seed 模拟实测 30.07%。
- 21 列 v2 元数据包含 `relationship_profile`；受控值为 `neutral`、`warm_friend`、`playful_friend`、`nickname_easter_egg`。
- 运行时按场景优先：先执行触发器/上下文、语义冷却、每日上限、最小间隔、滚动小时预算、夜间预算、组配额与关系画像配额，再在所选场景内选择合格变体。
- 发布模拟门禁是 legacy 25%–35%、Easter egg 8%–12%、seasoning 0.5%–1.5%、dry-sharp 0%–4%；它们是 30 天 × 100 seeds 的播放暴露率，不是 TSV 行数占比。
- 正式模拟结果为 15,000/15,000 次输出、legacy 30.07%、0 hard violations；联合 validator 为 0 hard errors，唯一 warning 是不参与播放验收的 legacy surface 原始库存观察。

下面的 806/51,326/52,132/533/20 列条目仅保留为 v1.2.1 历史基线，不是当前发布门禁：
- 当前运行时精确集成 52,132 条启用语料：806 条 curated core 加 51,326 条通过安全筛选并由 manifest 精确绑定的 legacy surfaces；按唯一 `semantic_group` 聚合后精确为 533 个场景。
- 20 列 v2 元数据、严格校验器、确定性离线选择器与 30 天 × 10 seeds 模拟已经接入。
- 运行时按场景优先：先执行触发器/上下文、语义冷却、每日上限、最小间隔、滚动小时预算、夜间预算与组配额并选择 `semantic_group` 场景，再在场景内选择合格变体；变体多的场景不会因此获得更大权重。
- 发布模拟的播放比例硬门禁是 Easter egg 8%–12%、seasoning 3%–6%、dry-sharp 2%–4%；这些是播放暴露率，不是 TSV 行数占比。
- 点击专用恢复路径通过 8 小时连续会话、900 次连续点击和旧版静默记忆恢复测试；主动播报仍遵守原有静默预算。
- 启动不在 UI 线程同步构建大型目录；预热期间使用固定本地 fallback，且真实 WPF 烟测只接受完整语料产生的启动回复。
- WPF 只嵌入已集成的 v2 运行时资源；75,375 条源物理数据行及其 archive 证据不会整体进入运行时。
- `v1.2.1` 已在目标 annotated tag 上重新完成 Python `372/372`、.NET Release `637/637`、干净 self-contained single-file publish、源/副本 SHA-256、固定种子重建、隔离单 EXE 烟测、8 项资产上传与代理回下载复验；未沿用旧版本的测试数字或二进制哈希。

完整语料维护契约、20 字段说明和精确命令见 [README-persona-corpus.md](README-persona-corpus.md)。

## 目录

```text
src/CompanionDesktopPet/       WPF 桌宠
src/persona_corpus/            离线语料流水线、选择器与模拟器
dialogue/                      可选对话服务；默认不启动，密钥不入库
voice/                         本地语音脚本与参考音频；推理运行时不入库
packaging/                     本机 Inno Setup 安装脚本
data/source/                   不可变原始语料副本
data/intermediate/             可追溯抽取产物与来源映射
data/optimized/                v2、归档与人工复核 TSV
data/persona/                  从 authored 语料蒸馏出的人物卡
config/                        调度配置、复核白名单与对话接口示例
reports/                       审计、改写、人工复核与模拟报告
scripts/                       发布隔离验证与本机运行时组装脚本
tools/                         语料命令行入口
tests/                         语料 Python、PowerShell 与 .NET 测试
dialogue/tests/                对话服务测试，不在上面的 tests/ 发现路径里
outputs/CompanionDesktopPet/   单 EXE 交付；不含语音与对话运行时
```

## 环境

- Windows x64
- .NET SDK 9.0.301（由根目录 `global.json` 精确锁定）
- Python 3.11 或更高版本。语料流水线只使用标准库；对话测试另需 `dialogue/requirements.txt` 里钉住的包

运行 v1.5.0 单 EXE 不需要 Python 或 .NET SDK。v1.6.0 安装包自带语音运行时和对话程序，也不需要用户另装 Python 或 .NET SDK。下面的工具只用于源码验证、重新构建，以及在没有安装包时自己组装语音或对话环境。

## 新鲜验证

```powershell
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 10 `
  --report reports/simulation-report.md `
  --events-json reports/simulation-events.json

python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json

python -m unittest discover -s tests -v

python -m pip install --require-hashes --only-binary=:all: `
  --requirement .github/requirements-dialogue.txt
Push-Location dialogue
python -m unittest discover -s tests -v
Pop-Location

dotnet restore CompanionDesktopPet.sln -r win-x64
$isTestProject = dotnet msbuild `
  tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -nologo -getProperty:IsTestProject
if (($isTestProject | Out-String).Trim() -ne 'true') {
  throw 'The .NET test project is not marked IsTestProject=true; dotnet test would be a no-op.'
}
dotnet test CompanionDesktopPet.sln -c Release --no-restore
```

校验器合格输出必须是 `Validation: 0 hard errors`。扩展运行时当前只允许一条描述原始库存结构的 `surface_inventory_observation` warning；出现任何其他 warning 都阻断发布。模拟必须为零硬约束违规；.NET 门禁必须显示实际执行的非零测试数，不能只检查 `dotnet test` 的退出码。

## 自动化 CI/CD

`.github/workflows/ci-cd.yml` 会在每个 PR 和推送到 `main` 时执行完整语料 Python 测试、对话服务测试、语料契约、模拟证据和 .NET Release 门禁。对话测试使用 `.github/requirements-dialogue.txt` 里按 Python 3.11 / Windows x64 钉住哈希的二进制轮子，工作目录是 `dialogue/`。它不下载语音模型，也不连接真实大模型；图测试使用脚本替身。手动运行 `workflow_dispatch` 会在门禁通过后生成可下载的 Windows 单 EXE artifact，但不会创建公开 Release，也不会打出 v1.6.0 那种带语音运行时的安装包。

正式单 EXE 发布只接受位于 `origin/main` 上、形如 `v1.1.0` 或 `v1.1.0-rc.1` 的全新 annotated tag。流水线从 tag 派生程序集版本，例如 `v1.7.0` 必须生成 `ProductVersion=1.7.0+<40 位提交 SHA>`；随后验证单 EXE、隔离 WPF smoke、法律文件和 SHA-256，再由 GitHub-hosted runner 使用仓库内置 `GITHUB_TOKEN` 创建新的 GitHub Release。这条 Release 的资产仍是单 EXE、ZIP 和法律文件。带语音运行时的安装包超过 GitHub 单附件 2GB 限制，继续由本机 Inno Setup 分段上传；`packaging/JiayiDesktopPet.iss` 和 `scripts/Assemble-VoiceRuntime.ps1` 里的源目录是打包装机上的路径，CI 不执行这两步。Release 标题、发布亮点、下载说明、完整性验证和构建来源均使用中文；只有许可证要求逐字保留的 `Required Notice` 继续使用官方英文原文。

在干净且已推送的 `main` 上可用下面的入口发布。若本机访问 GitHub 需要代理，只让这一次 tag push 经过代理；真正的 EXE/ZIP 上传由云端流水线完成：

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
git tag -a v1.1.0 -m "佳怡桌宠 v1.1.0"
git -c http.proxy=http://127.0.0.1:7890 push origin v1.1.0
```

代理端口不同则替换 `7890`。发布入口要求本次 push 是新建且非强推的 annotated tag；普通 push 也不能移动远端已有 tag。已有 GitHub Release 的八项资产保持不可变：同一原始运行的失败重试只有在候选资产逐字节完全相同时才作为无操作成功，任何名称或哈希差异都会失败，流水线不会删除、覆盖或编辑旧资产。不要删除后重建版本 tag；如需在 GitHub 侧禁止这种历史复用，应同时配置受保护 tag/ruleset，新版本始终使用新 tag。

## 干净发布与隔离烟测

先解析仓库根目录，并只清理已确认位于仓库内的 `publish/` 与 `outputs/verify/`；不要按模糊进程名结束程序，也不要递归删除整个 `outputs/CompanionDesktopPet/`。完整的计数、可复现生成、哈希记录和清理顺序见 [发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。

```powershell
$repoRoot = [IO.Path]::GetFullPath((git rev-parse --show-toplevel))
$publishDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'publish'))
$verifyDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs\verify'))
$rootPrefix = $repoRoot.TrimEnd('\') + '\'
foreach ($path in @($publishDir, $verifyDir)) {
  if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Cleanup target escaped repository root: $path"
  }
  Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
}
dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj `
  -c Release -r win-x64 --self-contained true --no-restore -o $publishDir
Copy-Item -LiteralPath (Join-Path $publishDir 'CompanionDesktopPet.exe') `
  -Destination (Join-Path $repoRoot 'outputs\CompanionDesktopPet\佳怡桌宠.exe') -Force
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 `
  -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe `
  -PublishExePath (Join-Path $publishDir 'CompanionDesktopPet.exe')
```

验证器要求 `publish/` 只包含 `CompanionDesktopPet.exe`，最终交付目录只包含 `佳怡桌宠.exe` 和明确允许的 `使用说明.txt`；额外 EXE、目录及 DLL/PDB/JSON/TXT 等 sidecar 都会使验证失败。它还会核对 publish 与交付 EXE 的 SHA-256。身份彩蛋只有在精确列入 editorial manifest 后才可进入运行时语料；安全边界由应用启动时对 `PersonaCorpus` exact editorial manifest 的自校验、Python validator 和程序集测试共同承担，不再使用无法区分已批准内容与泄露的 EXE 原始字节 marker 扫描。随后验证器把 EXE 单独复制到 `outputs/verify/`，以 `--smoke-test` 启动并只跟踪本次 PID；只有应用在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束才算成功。超时后的强制终止仅用于清理且仍判失败，非零退出同样失败。

## 数据、隐私与限制

- `src/CompanionDesktopPet/Assets/persona-corpus.tsv` 与 `data/source/persona-corpus.original.tsv` 是不可变审计证据，不原地覆盖。
- legacy 内容只进入 archive/review/PII review；运行时只接受通过安全规则、authorship manifest 与逐行 ledger 校验的 `curated_authored` 行。空的 surface manifest 是“零 legacy runtime surface”的可验证证据。
- 身份彩蛋只有精确列入 editorial manifest、且 ID、来源、允许的身份 marker、文本 SHA-256、分类、冷却和每日上限全部匹配时才可进入 `PersonaCorpus`；宽泛 marker 命中或 EXE 字节扫描不是批准。应用启动自校验该 exact manifest，Python validator 和程序集测试共同阻止未审批身份或隐私内容进入运行时。
- IDE 前台、连续活跃和空闲返回仍是未采集的未来信号，默认未知。全屏是当前唯一已采集的窗口上下文，只按本文开头公开的 HWND/可见性/样式、DWM 几何与显示器边界判断；失败保持原始 `unknown`，不读取标题、进程、输入、剪贴板、用户文件、像素或网络数据。
- 对话开关默认关闭。打开并填好接口之后，用户写下的句子会离开本机，发往该接口；接口密钥只放在被 gitignore 的 `config/llm.runtime.json`，不进入安装包。语音合成、自动台词和点击回复仍然留在本机。对话草稿先经过审计，不通过就改写一次，仍然不通过就改说本地短句，不把模型原文送去朗读。
- 自动检查不能替代人物授权、虚构身份、关系边界和再分发权利的人工审批。

## 许可

本仓库采用分层许可：可分离的技术代码按 [PolyForm Noncommercial License 1.0.0](LICENSE.md) 提供，可用于非商业学习、研究、实验、修改与按条款分发。由于该许可限制商业用途，本项目属于 **source-available（源码可见）**，不是 OSI 定义的开源软件。仓库使用 `LICENSE.md` 让 GitHub 正常渲染许可正文；Release 包中仍以标准文件名 `LICENSE` 携带同一份逐字节一致的原文。

桌宠形象、图标、姓名与昵称、人格、口吻、背景、关系设定、全部语料、语义分组、剧情/决策树、行为森林和编辑性编排均不随技术代码授权，原则上保留全部权利。官方 Release 仅额外允许非商业的私下运行，不授权抽取、复用、转载、改编、训练/微调模型、制作数据集或衍生角色。完整边界见 [LICENSE-SCOPE.md](LICENSE-SCOPE.md)、[ASSET_AND_PERSONA_RIGHTS.md](ASSET_AND_PERSONA_RIGHTS.md) 与 [NOTICE](NOTICE)。

## v1.7.0 增补

2026-10-05。上面各节保持原样。annotated tag `v1.7.0` 指向 `eae7ebd`，Windows 单 EXE 仍由该 tag 的流水线发布；带语音运行时的安装包仍是 v1.6.0。运行库仍是 82,132 条、1,723 个语义场景，SHA-256 仍为 `339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece`。

这次增加的是 Android 浮窗，包名 `com.jiayi.companionpet`，显示版本 1.7.0，version code 2。工程在 `src/CompanionDesktopPet.Android/`，不加入 Windows 解决方案。`android/jiayi-agent/` 用 Chaquopy 把 `dialogue/persona_dialogue` 打进 APK，对话按桌面 agent 的顺序在手机上完成：关系设定、记忆压缩、检索、审计和设置。本机 `persona.json` 里的「她是对方的女朋友」在打包时带上；说「做我女朋友」仍走本地回合，回复「好，我做你女朋友。」回车和发送按钮都会送出这一句。浮窗平时不占键盘，只有点进输入框才取焦点，点外面就放开。气泡底部留出行距，最后一行不再被裁掉。

语音不再探测空闲端口，也不再把非模拟器上保存的局域网或 `10.0.2.2` 地址留着。手机固定把文本发到 `http://43.138.138.200:8765`，由这台电脑的显卡合成后经 frp 转出。模拟器仍可使用 `10.0.2.2`。对话不再要求平板去连本机 `8766`。

本地调试签名 APK 为 116,374,976 字节，SHA-256 为 `9fc1b9218c68ed2cc7842693ff5ca8845d7f65009e7f1f7a501f1d1392968bdd`。仓库忽略 `outputs/`、`voice/gpu-host.log`、`voice/gpu-out/`，以及 Android 的 `build/`、`.gradle/` 和 `local.properties`。
