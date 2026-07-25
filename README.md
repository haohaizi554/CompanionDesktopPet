# 佳怡桌宠（CompanionDesktopPet）

一个完全离线的 Windows x64 WPF 桌宠，以及配套的可审计中文角色语料系统。她不读取输入内容、剪贴板、窗口标题，也不枚举或读取用户文件名、用户目录内容，不依赖网络、数据库或在线模型。正常运行时，角色偏好、冷却历史和剧情状态保存在 `%LOCALAPPDATA%\CompanionDesktopPet`；只有用户主动启用开机自启动时，才会另在当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 项保存桌宠自身的 EXE 路径。`--smoke-test` 发布验证使用系统临时目录中的独立状态，并在退出时清理。

> 当前集成基线包含 52,132 条运行时文案：806 条 curated core 加 51,326 条由 manifest 精确批准的安全 legacy surfaces；按唯一 `semantic_group` 聚合后为 533 个语义场景。WPF 离线运行时、异步语料预热、本地 fallback、自包含单文件发布与隔离烟测也已接入。75,375 条无表头源物理数据行仍作为不可变审计证据，不会整体进入运行时。

左键点击会显示爱心、按点击位置向相反方向轻轻倾斜，并给出一句回复。点击回复有长期运行兜底：即使冷却历史逐渐累积，或从旧版容易陷入静默的本地记忆恢复，后续点击也不会永久失声。桌宠保留自然单次/偶发双次眨眼，启动后会显示一次本地“嗨♡”，也可从右键面板选择 `打个招呼♡`；这些都是纯本地 UI 动作，不由语料驱动。

窗口先显示，再在后台异步加载记忆、语料和场景。完整语料尚未就绪时，启动与点击立即使用短小的内置本地 fallback，不阻塞 UI；自动播报可以保持安静。预热成功后切换到完整场景，瞬态失败按 1、5、30 秒退避重试，结构或隐私契约错误则保持 fallback 并阻止发布烟测把它误判为“完整语料已就绪”。

## 最终交付

```text
outputs/CompanionDesktopPet/佳怡桌宠.exe
outputs/CompanionDesktopPet/使用说明.txt
```

`佳怡桌宠.exe` 是 `win-x64` 自包含单文件应用，运行时不需要另行安装 .NET，也不依赖旁置或外部应用 DLL、JSON、PDB 等运行时 sidecar。“自包含单 EXE”不表示进程绝不加载 DLL；作为 Windows 桌面应用，它仍会正常使用操作系统提供的系统 DLL 与系统组件。

当前公开交付仍是历史 Release [v1.0.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.0.0)：EXE 从提交 `ad5aa867a06d84d64fc4399cb4d258becce1b8ab` 使用 .NET SDK `10.0.301` 构建，`ProductVersion=1.0.0+ad5aa867a06d84d64fc4399cb4d258becce1b8ab`，大小为 `80,299,750` 字节，SHA-256 为 `b79bf57a94d63387b6d8db288e53f64b06af32a3aa4881e7c069634839442a82`。publish、交付与隔离烟测副本的哈希一致，真实 WPF smoke 自行以退出码 0 结束。GitHub 会清洗非 ASCII 附件名，因此外层资产使用 `Jiayi-Desktop-Pet.exe`、`Jiayi-Desktop-Pet-README-zh-CN.txt` 和 `Jiayi-Desktop-Pet-win-x64.zip`；ZIP 内仍保留 `佳怡桌宠.exe` 与 `使用说明.txt`。完整证据见[发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。该 v1.0.0 EXE 未做 Authenticode 代码签名，从网络下载时可能出现 Windows SmartScreen/安全软件信誉提示。当前源码中的后续修复只有在新的版本标签完成全部门禁、生成新哈希并回读 GitHub Release 后，才属于新的公开交付。

## 体验与操作

- 左键单击人物：显示爱心、按点击位置向相反方向轻轻倾斜，并按当前场景说一句话。
- 按住左键拖动：移动桌宠，移动时按方向倾斜，松手后回弹。
- 气泡与人物之间保持 30 DIP 的视觉距离；鼠标停在人物或气泡上时，只暂停当前气泡剩余的消失倒计时，移开后从剩余时间继续。
- 右键人物：打开卡哇伊风格控制面板，可说句话、`打个招呼♡`、暂停/继续动画、调整大小、切换置顶、设置开机自启动、恢复位置、藏到托盘或退出。
- 托盘：双击图标切换显示/隐藏；右键菜单可显示/隐藏、说句话、暂停/继续、切换开机自启动或退出。
- 当前源码已支持 Windows 高对比度模式：气泡与控制面板会采用系统颜色和无阴影样式，关闭后恢复卡哇伊主题。此能力不在上面的历史 v1.0.0 EXE 中；必须等 v1.1.0 完成最终构建与 Release 实证后，才可作为公开交付能力宣称。

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
- 当前 curated core 精确为 806 条完整、可独立播放的启用语料。
- 当前运行时精确集成 52,132 条启用语料：806 条 curated core 加 51,326 条通过安全筛选并由 manifest 精确绑定的 legacy surfaces；按唯一 `semantic_group` 聚合后精确为 533 个场景。
- 20 列 v2 元数据、严格校验器、确定性离线选择器与 30 天 × 10 seeds 模拟已经接入。
- 运行时按场景优先：先执行触发器/上下文、语义冷却、每日上限、最小间隔、滚动小时预算、夜间预算与组配额并选择 `semantic_group` 场景，再在场景内选择合格变体；变体多的场景不会因此获得更大权重。
- 发布模拟的播放比例硬门禁是 Easter egg 8%–12%、seasoning 3%–6%、dry-sharp 2%–4%；这些是播放暴露率，不是 TSV 行数占比。
- 点击专用恢复路径通过 8 小时连续会话、900 次连续点击和旧版静默记忆恢复测试；主动播报仍遵守原有静默预算。
- 启动不在 UI 线程同步构建大型目录；预热期间使用固定本地 fallback，且真实 WPF 烟测只接受完整语料产生的启动回复。
- WPF 只嵌入已集成的 v2 运行时资源；75,375 条源物理数据行及其 archive 证据不会整体进入运行时。
- Release 测试、干净 self-contained single-file publish、源/副本 SHA-256、固定种子重建和隔离单 EXE 烟测均已通过最终门禁。

完整语料维护契约、20 字段说明和精确命令见 [README-persona-corpus.md](README-persona-corpus.md)。

## 目录

```text
src/CompanionDesktopPet/       WPF 桌宠
src/persona_corpus/            离线语料流水线、选择器与模拟器
data/source/                   不可变原始语料副本
data/intermediate/             可追溯抽取产物与来源映射
data/optimized/                v2、归档与人工复核 TSV
config/                        调度配置与精确复核白名单
reports/                       审计、改写、人工复核与模拟报告
scripts/                       发布隔离验证脚本
tools/                         语料命令行入口
tests/                         Python、PowerShell 与 .NET 测试
outputs/CompanionDesktopPet/   最终交付
```

## 环境

- Windows x64
- .NET SDK 9.0.301（由根目录 `global.json` 精确锁定）
- Python 3.11 或更高版本（只使用标准库）

运行最终 EXE 不需要 Python 或 .NET SDK；这些工具只用于源码验证与重新构建。

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

`.github/workflows/ci-cd.yml` 会在每个 PR 和推送到 `main` 时执行完整 Python、语料契约、模拟证据和 .NET Release 门禁。手动运行 `workflow_dispatch` 会在门禁通过后生成可下载的 Windows 发布 artifact，但不会创建公开 Release。

正式发布只接受位于 `origin/main` 上、形如 `v1.1.0` 或 `v1.1.0-rc.1` 的全新 annotated tag。流水线从 tag 派生程序集版本，例如 `v1.1.0` 必须生成 `ProductVersion=1.1.0+<40 位提交 SHA>`；随后验证单 EXE、隔离 WPF smoke、法律文件和 SHA-256，再由 GitHub-hosted runner 使用仓库内置 `GITHUB_TOKEN` 创建新的 GitHub Release。这样无需把本机失效的 `gh` keyring 登录用于大文件上传。

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
- 禁用内容进入 archive；不确定内容与 PII 进入 review；只有通过安全规则并由 surface manifest 精确绑定原始行、topic、原文与摘要的 legacy surface 才能进入运行时。
- 身份彩蛋只有精确列入 editorial manifest、且 ID、来源、允许的身份 marker、文本 SHA-256、分类、冷却和每日上限全部匹配时才可进入 `PersonaCorpus`；宽泛 marker 命中或 EXE 字节扫描不是批准。应用启动自校验该 exact manifest，Python validator 和程序集测试共同阻止未审批身份或隐私内容进入运行时。
- IDE 前台、连续活跃、空闲返回和全屏等未来信号默认未知，不猜测用户状态。
- 自动检查不能替代人物授权、虚构身份、关系边界和再分发权利的人工审批。

## 许可

本仓库采用分层许可：可分离的技术代码按 [PolyForm Noncommercial License 1.0.0](LICENSE) 提供，可用于非商业学习、研究、实验、修改与按条款分发。由于该许可限制商业用途，本项目属于 **source-available（源码可见）**，不是 OSI 定义的开源软件。

桌宠形象、图标、姓名与昵称、人格、口吻、背景、关系设定、全部语料、语义分组、剧情/决策树、行为森林和编辑性编排均不随技术代码授权，原则上保留全部权利。官方 Release 仅额外允许非商业的私下运行，不授权抽取、复用、转载、改编、训练/微调模型、制作数据集或衍生角色。完整边界见 [LICENSE-SCOPE.md](LICENSE-SCOPE.md)、[ASSET_AND_PERSONA_RIGHTS.md](ASSET_AND_PERSONA_RIGHTS.md) 与 [NOTICE](NOTICE)。
