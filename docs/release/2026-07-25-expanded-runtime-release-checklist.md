# v1.4.0 Hybrid Runtime 发布与清理清单

日期：2026-07-30
状态：`v1.4.0` 语料、100-seed 模拟与发布文档已完成；单 EXE、标签、GitHub Release、代理回下载复验与最终工作树清理按本清单后续步骤执行并登记

本文的当前门禁是 30,000 条 authored 与 v1.2.1 的 52,132 条 legacy 合并后的 82,132 条 hybrid runtime。v1.0.0、v1.1.0、v1.2.1、v1.3.0 的证据继续作为历史记录保留，但不得当作 v1.4.0 的测试、二进制或哈希证据。release tag 必须指向实际产出 EXE 的 source commit。

## 1. 精确验收常量

| 项目 | 必须精确等于 | 定义 |
| --- | ---: | --- |
| authored runtime | 30,000 | `source_kind=curated_authored` 的全部启用行 |
| legacy curated runtime | 806 | v1.2.1 清单绑定的非 surface 启用行 |
| legacy runtime surfaces | 51,326 | `source_kind=legacy_surface_variant` 的启用行 |
| combined runtime | 82,132 | authored 与 legacy 的稳定 ID 并集 |
| semantic scenes | 1,723 | runtime 中唯一 `semantic_group` 数 |
| authorship ledger | 30,000 | 每条 authored source 的 hash-bound lineage |
| immutable source | 75,375 | 原始 TSV 无表头物理数据行数 |
| archive dispositions | 75,375 | expanded build 的 archive 数据记录数 |

当前文件已重新计数为 30,000 authored runtime、0 legacy surfaces、1,190 个唯一 `semantic_group`、30,000 条 ledger、75,375 条 source 与 75,375 条 archive。任何差异都阻止发布。

## 2. 运行时选择与暴露门禁

- 调度必须 scene-first：先按 `semantic_group` 过滤、评分并选择场景，再在所选场景内选变体。变体数量不得增加场景权重，点击兜底也不得先全局挑行再倒推场景。
- Easter egg 播放率必须为 8%–12%；seasoning 播放率必须为 0.5%–1.5%；dry-sharp 播放率必须为 0%–4%。它们是模拟播放率，不是库存行占比。
- seasoning 使用 contract 的 NFKC/casefold 与 token-boundary 规则，identity markers 明确排除在 seasoning 之外；最近 20 次最多暴露 1 次 seasoning。
- dry-sharp 的场景资格由稳定 scene hash 决定；contract 中 0.6%–0.8% 的 dry-sharp scene inventory 与 0%–4% 的 playback acceptance 是两个不同门禁。
- `warm_friend` 最近 20 次最多 2 次；`nickname_easter_egg` 最近 100 次最多 1 次。关系画像配额必须在评分前过滤。

## 3. 身份与隐私边界

- authored 身份彩蛋只允许来自 b083–b092，且必须匹配受控 category、editorial role、关系画像、marker 和 source-reference 语法。
- `config/persona-authorship-manifest.json` 绑定 100 批的文本/元数据摘要；`persona-authorship-ledger.tsv` 逐行绑定 batch、variant、摘要、review status、关系画像与根哈希。
- `persona-surface-manifest.tsv` 必须为 0 数据行；任何 legacy runtime surface 或额外 manifest 记录都是硬失败。
- 问句/回复钩子、未批准身份、非身份 PII、不可用上下文、面向用户的当前状态断言、控制字符、过度命令式文本和规范化重复不得进入运行时。
- archive/source/review 只提供审计证据，不自动构成运行时许可。不得用宽泛 marker 扫描或 EXE 原始字节搜索代替 manifest 审批。
- 自动台词四个精确随机间隔窗口为：本地时间 `06:00–17:59:59` 使用 `5–15` 分钟，`18:00–22:59:59` 使用 `10–20` 分钟，`23:00–05:59:59` 使用 `30–60` 分钟；明确全屏时覆盖时段并使用 `60–120` 分钟。上下界均可取到。
- 全屏探测只读取台前 HWND 的有效性、可见/最小化状态与样式，DWM cloaked 状态和扩展边框几何，以及相交显示器的完整边界；失败或采样期间 HWND 变化时，原始观测保持 `unknown`，不伪造为非全屏。它不读取窗口标题、进程名称/内容、输入、剪贴板、用户文件、屏幕像素或网络数据。
- 桌宠继续完全离线。正常运行时，角色偏好、冷却历史和剧情状态保存在 `%LOCALAPPDATA%\CompanionDesktopPet`；只有用户主动启用开机自启动时，才会另在当前用户 Run 注册表项保存桌宠自身 EXE 路径；`--smoke-test` 使用并清理系统临时目录中的隔离状态。自动规则不能替代人物授权、虚构身份、关系边界和再分发权利的人工批准。

## 4. 异步预热与 fallback

- 窗口先显示；记忆、30,000 条语料和 1,190-scene catalog 在后台预热，不能阻塞 Dispatcher/UI 线程。
- 预热期间 startup/click 必须立即返回固定、本地、短小的 fallback；自动事件可以静默，不得伪造完整语料已就绪。
- 瞬态失败按 1、5、30 秒退避重试；取消正常结束，结构、格式、contract 或 privacy 错误视为永久失败并保持 fallback。
- smoke readiness 必须等到完整 runtime 已就绪且真正的非-fallback 启动回复完成渲染。fallback 来源、超时、强杀或非零退出都判失败。

## 5. 隔离可复现重建

以下命令只在 `outputs/verify/corpus-repro/` 生成临时副本，不原位覆盖 canonical 数据。删除前先证明目标位于仓库根目录内。

```powershell
$repoRoot = [IO.Path]::GetFullPath((git rev-parse --show-toplevel))
$rootPrefix = $repoRoot.TrimEnd('\') + '\'
$reproDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs\verify\corpus-repro'))
if (-not $reproDir.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Repro directory escaped repository root: $reproDir"
}
Remove-Item -LiteralPath $reproDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $reproDir | Out-Null

python tools/build_corpus_v2.py `
  --input data/source/persona-corpus.original.tsv `
  --mappings data/intermediate/source-line-map.tsv `
  --authored-dir data/authored/v1 `
  --authorship-manifest config/persona-authorship-manifest.json `
  --output (Join-Path $reproDir 'persona-corpus-v2.tsv') `
  --report-output (Join-Path $reproDir 'pii-review.tsv') `
  --seed 20260722 `
  --pii-policy review

$pairs = @(
  @('data\optimized\persona-corpus-v2.tsv', 'persona-corpus-v2.tsv'),
  @('data\optimized\persona-corpus-archive.tsv', 'persona-corpus-archive.tsv'),
  @('data\optimized\persona-corpus-review.tsv', 'persona-corpus-review.tsv'),
  @('reports\pii-review.tsv', 'pii-review.tsv'),
  @('data\optimized\persona-surface-manifest.tsv', 'persona-surface-manifest.tsv'),
  @('data\optimized\persona-authorship-ledger.tsv', 'persona-authorship-ledger.tsv')
)
foreach ($pair in $pairs) {
  $canonical = Join-Path $repoRoot $pair[0]
  $rebuilt = Join-Path $reproDir $pair[1]
  $canonicalHash = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash
  $rebuiltHash = (Get-FileHash -LiteralPath $rebuilt -Algorithm SHA256).Hash
  if ($canonicalHash -ne $rebuiltHash) {
    throw "Non-reproducible artifact: $($pair[0])"
  }
}
```

在同一最终提交上运行精确计数：

```powershell
@'
from pathlib import Path
import csv

root = Path('.')
with (root / 'data/optimized/persona-corpus-v2.tsv').open(
    encoding='utf-8-sig', newline=''
) as stream:
    runtime = list(csv.DictReader(stream, delimiter='\t'))
with (root / 'data/optimized/persona-corpus-archive.tsv').open(
    encoding='utf-8-sig', newline=''
) as stream:
    archive = list(csv.DictReader(stream, delimiter='\t'))

authored = sum(row['source_kind'] == 'curated_authored' for row in runtime)
surfaces = sum(row['source_kind'] == 'legacy_surface_variant' for row in runtime)
legacy_curated = len(runtime) - authored - surfaces
scenes = len({row['semantic_group'] for row in runtime})
ledger = list(csv.DictReader((root / 'data/optimized/persona-authorship-ledger.tsv').open(encoding='utf-8', newline=''), delimiter='\t'))
source_lines = sum(
    1 for _ in (root / 'data/source/persona-corpus.original.tsv').open(
        encoding='utf-8-sig', newline=''
    )
)

assert authored == 30_000, authored
assert legacy_curated == 806, legacy_curated
assert surfaces == 51_326, surfaces
assert len(runtime) == 82_132, len(runtime)
assert scenes == 1_723, scenes
assert len(ledger) == 30_000, len(ledger)
assert source_lines == 75_375, source_lines
assert len(archive) == 75_375, len(archive)
print('PASS: 30,000 authored; 806 legacy curated; 51,326 legacy surfaces; 82,132 runtime; 1,723 scenes')
'@ | python -
```

## 6. 测试、模拟与发布

必须从最终语料重新生成 simulation events/report；旧 contract 下的报告不可复用。

```powershell
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 100 `
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

`simulation-events.json` 必须为每次尝试记录精确的 `seed/day_index/slot_index`，并绑定 corpus、scheduler 与 subseed derivation。校验器必须重新执行选择器并比对精确 `selected_id`。v1.3.0 的发布结果必须为 `0 hard errors / 0 warnings`。

### 6.1 `v1.0.0` 历史验证记录（不得作为 v1.1.0 的新鲜证据）

- 两项 generator `--check` 均通过；隔离重建的 v2、archive、review、PII review 与 surface manifest 五份产物逐字节匹配 canonical。
- 精确计数：806 core + 51,326 surfaces = 52,132 runtime；533 scenes；75,375 source 数据行；75,375 archive；51,326 surface-manifest 记录。
- 30 天 × 10 seeds：1,500 attempts / 1,500 outputs；Easter egg 9.87%、seasoning 4.93%、dry-sharp 4.00%；natural、adversarial 与 combined hard violations 均为零。
- Validator：`0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。
- `v1.0.0` 标签流水线实际执行并通过 Python 311/311；.NET Release 测试项目门禁为 `IsTestProject=true`，实际发现并通过 392/392。当前源码已新增测试，因此 v1.1.0 必须登记最终标签流水线实际发现的新数量。
- Release 回归还顺带发现并消除了节日候选断言对 52,132 行重复全表扫描的二次复杂度；修复后完整 Release 套件在 33 秒内完成。

### 6.2 Phase 5 精确回放与运行时比例门禁

- validator-facing events 已升级为 schema v3；1,500 条事件按 `seed/day_index/slot_index` 完整排序，校验器会重新构造规范时间、上下文、subseed、逐 seed 历史与精确 `selected_id`。缺失、额外、乱序、上下文/时间篡改和同场景 surface 偷换均为硬错误。
- 四季、04:00–05:59 dawn、`ide_foreground/idle_return/fullscreen` 的 `null/false/true`，以及 `active_minutes` 的 `null/89/90/91` 均有硬覆盖门禁。
- canonical replay 上限为 3,000 次；声明规模或原始事件数超限、raw/parsed 数量不完整时均在 selector 前拒绝。相同完整输入只复用容量为 2 的规范答案流缓存，每次事件仍逐条重新比对。
- C# 运行时通过真实 `OfflineCompanionAgent → SceneScheduler → PersonaCorpus` 路径验证：10 seeds × 30 days × 4 slots = 1,200 outputs，Easter egg 为 120/1,200（10.00%），每 seed 均为 10.00%。接受区间由共享 contract 生成，不在测试中另行硬编码。
- `v1.0.0` 发布前主代理复验：Python `311/311`（140.4 秒）；.NET Release `392/392`、0 跳过；validator 为 `0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。这些数值是历史证据，不预判 v1.1.0 的最终结果。

### 6.3 `v1.1.0` pre-tag source gate（2026-07-29）

以下是在 `main` 基线 `301823e2f38c33e7c1c917af110ca70e5c2ef703` 加当时四份文档改动上重新执行的 source/test 证据；它是标签前门禁，目标 tag 的最终 EXE/Release 证据见第 7.2 节：

- simulation：命令退出 `0`，实际用时 `53.8s`；`30 days × 10 seeds`，`1,500/1,500 outputs`，`0 hard violations`。报告 SHA-256 为 `b66e5c9ba704ff3d050fb7d41f4cb6fa553acfbb1790010a3129c3f6cbcafcb9`，events SHA-256 为 `017e1bf3c20559bd046a1d86c0f0a3788220d0262f82792e9288651c81f42d80`；editorial evidence 为 `rewrites=50, disabled=20, tone=20, fake_context=20, manual=4513`。
- validator：命令退出 `0`，实际用时 `61.4s`；`0 hard errors / 1 warning`，唯一 warning 精确为 `surface_inventory_observation`（51,326 surface rows 的 inventory observation）。
- Python unittest：实际发现并通过 `368/368`，`0 failures / 0 errors`；unittest 报告用时 `544.823s`，命令总用时 `548s`。
- .NET：`dotnet --version` 为 `9.0.301`；restore、`IsTestProject=true` 查询与完整 Release suite 的组合命令退出 `0`、总用时 `58s`；完整 Release suite 实际发现并通过 `600/600`，`0 failed / 0 skipped`，测试报告用时 `38s`。输出没有编译 warning；结束后仅打印一条非门禁的 SDK workload update 可用通知。
- simulation 两份 tracked report 经新鲜重跑后与当前 canonical 字节一致，因此 `git status` 没有 report diff；上面的 hash 来自本轮工具输出与 `Get-FileHash` 复核，不是从历史数字复制。

这些 source gates 在标签前没有预填 `v1.1.0` 的 EXE 字节数、ProductVersion、Authenticode、SmokePID、Release URL 或资产哈希；第 7.2 节只登记后续 annotated tag 流水线与代理回下载实际产生的值。

### 6.4 后续版本的自动发布入口

- PR 与 `main` push 只运行质量门禁；`workflow_dispatch` 额外生成 30 天保留的 Windows artifact，但不公开发版。
- GitHub Release 只由形如 `v1.1.0` / `v1.1.0-rc.1`、在本次事件中新建且非强推的 annotated tag 触发。tag 必须精确指向 `origin/main` 中的提交；轻量 tag、非严格语义版本 tag、旁支提交和强制移动均在打包前拒绝。已有 Release 的八项资产不可变：同一原始运行的失败重试仅在八项候选资产逐字节完全一致时无操作成功，任何清单或哈希差异都会失败，不删除、覆盖或编辑旧资产。GitHub push payload 无法证明被删除 tag 的全部历史，因此还应通过仓库 tag ruleset 阻止删除后重建。
- 根目录提交的 `global.json` 精确锁定 .NET SDK `9.0.301` 并使用 `rollForward=disable`；`setup-dotnet` 从该文件安装 SDK，实际 `dotnet --version`、action 输出与提交版本必须三者精确一致，避免 runner 预装的更高 SDK 静默接管构建，也避免同一 tag 日后 rerun 漂到更新 patch。
- 程序集版本直接从 tag 去掉前导 `v` 后派生；发布门禁要求 `ProductVersion=<tag version>+<GITHUB_SHA>` 精确相等。因此 `v1.1.0` 不允许继续产出显示为 `1.0.0` 的 EXE。
- 需要本机代理时，仅用 `git -c http.proxy=http://127.0.0.1:7890 push origin <tag>` 推送小型 tag；质量门禁、EXE/ZIP 构建及 GitHub Release 资产上传均由 GitHub-hosted runner 使用短期 `GITHUB_TOKEN` 完成，不依赖本机 `gh` keyring token，也不复制一套本地上传逻辑。
- GitHub Release 标题和说明固定为 `zh-CN`：发布亮点、下载与运行、完整性验证、离线/单 EXE/签名说明、构建来源和许可边界均使用中文。禁止 `--generate-notes` 注入英文自动说明；仅逐字保留法律文件要求的英文 `Required Notice`。
- 当前没有热更新或自动更新通道。打包门禁必须对最终 delivery EXE 执行 `Get-AuthenticodeSignature`，并在尚未配置证书时精确要求 `Status=NotSigned`、Signer/TimeStamper 证书均为空，再把该状态写入 staging 证据与发布说明；未来接入证书后必须显式把策略升级为 `Status=Valid` 和固定主体/指纹，不能静默接受任意签名。

测试全绿后，只删除已验证位于仓库内的 scratch/publish 目录，并只覆盖明确的交付 EXE：

```powershell
$repoRoot = [IO.Path]::GetFullPath((git rev-parse --show-toplevel))
$rootPrefix = $repoRoot.TrimEnd('\') + '\'
$publishDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'publish'))
$verifyDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs\verify'))
$deliveryDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs\CompanionDesktopPet'))
$deliveryExe = Join-Path $deliveryDir '佳怡桌宠.exe'

foreach ($path in @($publishDir, $verifyDir, $deliveryDir)) {
  if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release path escaped repository root: $path"
  }
}
Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $verifyDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $deliveryExe -Force -ErrorAction SilentlyContinue

dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj `
  -c Release -r win-x64 --self-contained true --no-restore -o $publishDir
Copy-Item -LiteralPath (Join-Path $publishDir 'CompanionDesktopPet.exe') `
  -Destination $deliveryExe -Force

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 `
  -ExePath $deliveryExe `
  -PublishExePath (Join-Path $publishDir 'CompanionDesktopPet.exe')
```

`Verify-Publish.ps1` 必须使用 `-Force` 枚举目录，确认 publish 目录只有 `CompanionDesktopPet.exe`、交付目录只有一个 EXE 和允许的 `使用说明.txt`，并拒绝隐藏/系统 sidecar 与目录；两份 EXE 哈希必须相同，再在 `outputs/verify/` 隔离启动 `--smoke-test`。默认 30 秒总预算覆盖应用内部最多 15 秒语料 warmup 与两次各 2 秒动作探针；成功输出必须登记 `SmokePID`、`ExitCode=0` 及 publish/delivery/isolated 三份哈希。这里的单 EXE 指不依赖旁置/外部应用 DLL、JSON 或 PDB；内嵌原生组件仍可能由 .NET 单文件机制解压到系统临时缓存，Windows 系统 DLL 与系统组件也不在此承诺范围。脚本只跟踪本次 PID；不要使用 `Stop-Process -Name` 清理无关桌宠进程。

## 7. 发布哈希登记

| 发布对象 | SHA-256 |
| --- | --- |
| immutable source / byte copy | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| expanded runtime v2 | `339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece` |
| archive | `b7d9a5f2fd6f4750ea2b688206f77bf45a2b59ca12c09f36281c72efc620721d` |
| review | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| PII review | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |
| surface manifest | `bcf9c97be0e4b1d7b7db11fcb46f44de17ef0ade6cb2e79d69f8af69bdbc637d` |
| authorship manifest | `4742a984077ce044adf8f059ee46dd36bfd7f4a0248c3422060bd366027cc4b6` |
| authorship ledger | `31305579ebf55d2d49c3227d7c7664b16e89abd0e9aab3cbbfa11ae3e0cace8d` |
| persona contract | `8ed829357d2692f044185614244a59d00fec3909e1a5f41e78fcd47958ad356c` |
| scheduler raw bytes | `d90826d9ffba57f83e0e3a04bd38f8f27e80b5fe95e31737bbbd4e4959de0ebb` |
| scheduler semantic binding | `eedc8979fb239a915789af4ff62d55b31a2aeabde3f196dabd7355e73f666f2a` |
| editorial manifest | `ce03fcbe4bb4de0f61ab81e29075ed80eb30bfe921bb1499e5514a1a3c5ad7b5` |
| subseed derivation v2 | `e5f6d36ffb5d4936bccca24cb9c7177a63e02d937118342916bd5eea0a83640d` |
| simulation report | `2fceb9aecd6817dedf4c3690938c0c1656c97eeff4af8558f300791091597952` |
| validator-facing simulation events | `84c05704368bc5a18946342e2f6d5cef113ebc345aa9ac7f3b8037f45b6a8fd2` |
| current `v1.3.0` `佳怡桌宠.exe` | `9d20f5a546d10c65ac5b65558dbcc722f96ceef5e63fb89484dcb69bc420d5e6` |
| current `v1.2.1` `佳怡桌宠.exe` | `7d5343c01e1ed89ef15e3d9595f6c9fb1ec24f8275db15628a5b541ad5c1ff03` |
| historical `v1.0.0` `佳怡桌宠.exe` | `b79bf57a94d63387b6d8db288e53f64b06af32a3aa4881e7c069634839442a82` |

### 7.1 `v1.0.0` 正式发布实证

- built-from source commit：`ad5aa867a06d84d64fc4399cb4d258becce1b8ab`；带注释标签 `v1.0.0` 精确指向该提交，构建前 tracked worktree 干净且该 SHA 已推送到远端。
- 工具链：GitHub-hosted Windows runner 实际使用 .NET SDK `10.0.301`；EXE `ProductVersion=1.0.0+ad5aa867a06d84d64fc4399cb4d258becce1b8ab`，可从二进制反查 built-from。
- 最终 EXE：`80,299,750` 字节；云端 publish、delivery、isolated 与经代理回传后重新下载的本地副本 SHA-256 均为 `b79bf57a94d63387b6d8db288e53f64b06af32a3aa4881e7c069634839442a82`。
- publish 清单精确为一个 `CompanionDesktopPet.exe`；delivery 清单精确为 `佳怡桌宠.exe` 与允许的 `使用说明.txt`，无应用 DLL/JSON/PDB 或额外目录。
- 隔离 smoke：云端 `SmokePID=2280`、最终本地复核 `SmokePID=13700`，两次均 `ExitCode=0`，进程自行退出且未按进程名清理；完整发布验证器契约测试通过，包含隐藏 publish DLL、隐藏 delivery JSON、额外 EXE、嵌套依赖、强制终止与非零退出拒绝用例。
- 标签门禁：Python `311/311`、.NET Release `392/392`，均为 0 失败；validator 为 `0 hard errors` 和唯一允许的 `surface_inventory_observation` warning。
- GitHub Release：[v1.0.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.0.0) 精确保留 8 个外层资产；`SHA256SUMS.txt` SHA-256 为 `ab88e85b41c23e0fb1ca980581a7b84cd3766b592e80bcead1c506b930ba4d04`，并精确覆盖其余 7 项。GitHub 会清洗非 ASCII 附件名，故外层使用 `Jiayi-Desktop-Pet.exe`、`Jiayi-Desktop-Pet-README-zh-CN.txt`、`Jiayi-Desktop-Pet-win-x64.zip`，ZIP 内仍精确保留两个中文交付名与四个法律文件。最终 8 项已经本机 `http://127.0.0.1:7890` 代理回传/刷新并重新下载复核。
- 证据登记后已清理 `publish/`、`outputs/verify/`、`outputs/verify-contract-test/` 与 `outputs/verify-contract-helpers/`；交付目录保留。
- Authenticode 状态为 `NotSigned`。这不改变单文件与离线门禁，但 GitHub/网络下载可能触发 SmartScreen 或安全软件信誉提示；未配置代码签名证书前不得宣称“下载后无安全提示”。

### 7.2 `v1.1.0` 最终登记证据（2026-07-29）

- 构建来源：annotated tag `v1.1.0` 精确指向 `dda5350cb2fe102d78a41c5d998eaa4592ded267`，该提交等于当时的 `origin/main`；.NET SDK 为 `9.0.301`，EXE `ProductVersion=1.1.0+dda5350cb2fe102d78a41c5d998eaa4592ded267`。
- CI：[main 最终门禁 run 30398918257](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30398918257) 成功；[v1.1.0 标签 run 30399579242](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30399579242) 的 quality gates、package 与 GitHub Release 三个 job 全部成功。标签门禁实际通过 Python `368/368` 与 .NET Release `600/600`，均为 0 失败；validator 为 `0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。
- 正式 EXE：`80,454,312` 字节，SHA-256 为 `75a074d6c3731e135be99ceb694e0d3fa6ea9a9f9bcc0f52b736eb3c030cb692`；云端 publish/delivery/isolated、直接 Release 资产、ZIP 内 EXE、本地交付与最终 isolated 副本逐字节一致。
- Authenticode：`NotSigned`，SignerCertificate 与 TimeStamperCertificate 均为空；Release 中文正文明确提示 SmartScreen/安全软件可能显示信誉提示。
- smoke：标签流水线 `SmokePID=9924`，通过 `http://127.0.0.1:7890` 回下载后的最终本地复核 `SmokePID=48052`；两次均 `ExitCode=0` 并自行退出。
- GitHub Release：[佳怡桌宠 v1.1.0（Windows x64）](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.1.0) 为非草稿、非预发布版本；标题和正文为中文，包含发布亮点、下载与运行、完整性验证、离线/单 EXE/签名、构建来源、许可范围六个中文章节。只有许可证要求逐字保留的 `Required Notice` 使用英文。
- 外层资产精确为 8 项；`SHA256SUMS.txt` 精确覆盖其余 7 项。代理回下载后的字节数与 SHA-256：

| Release 资产 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `ASSET_AND_PERSONA_RIGHTS.md` | 2,718 | `bc06b35871e87f0951a8866722a1217db544455b9ef196557dc1e10fc2ff1bf9` |
| `Jiayi-Desktop-Pet.exe` | 80,454,312 | `75a074d6c3731e135be99ceb694e0d3fa6ea9a9f9bcc0f52b736eb3c030cb692` |
| `Jiayi-Desktop-Pet-README-zh-CN.txt` | 3,141 | `72301d82aab03f5fd732d2add323cc768715d76043838105a652b15f3c6240d3` |
| `Jiayi-Desktop-Pet-win-x64.zip` | 80,468,572 | `0365707524c0682d731c9f1bb1f7c0d2a380d07a1ceae6a20b4dcd500eb75459` |
| `LICENSE` | 4,563 | `c0ea4a896d2c8c394b29f9427589996db826cd501c512279ff0ed3ef48fabbe5` |
| `LICENSE-SCOPE.md` | 2,949 | `37f7e39235dd7e37724ed4872d52e035f1958f38dc959e1eaeaf33f91f829fc6` |
| `NOTICE` | 235 | `fd7ed21b4c71bbfd505f632e092b9b68d9e7e2c4d1e457f28b50d827b8f08b8b` |
| `SHA256SUMS.txt` | 616 | `8fa76404b2f1961f9897e747d33e1de7a248b1bdf964f43c492edac6559f3280` |

- ZIP 无子目录，精确平铺 `佳怡桌宠.exe`、`使用说明.txt`、`LICENSE`、`LICENSE-SCOPE.md`、`ASSET_AND_PERSONA_RIGHTS.md`、`NOTICE`；每项与对应外层资产哈希一致。

### 7.3 `v1.2.1` 最终登记证据（2026-07-29）

- 构建来源：annotated tag `v1.2.1` 精确指向 `421b54a349062ab540b27bfe6f9a97ba7df5b6f2`，该提交等于当时的 `origin/main`；.NET SDK 为 `9.0.301`，EXE `ProductVersion=1.2.1+421b54a349062ab540b27bfe6f9a97ba7df5b6f2`。
- CI：[main 最终门禁 run 30418888675](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30418888675) 成功；[v1.2.1 标签 run 30419229928](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30419229928) 的 quality gates、package 与 GitHub Release 三个 job 全部成功。标签门禁实际通过 Python `372/372` 与 .NET Release `637/637`，均为 0 失败、0 跳过；validator 为 `0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。
- 正式 EXE：`80,454,500` 字节，SHA-256 为 `7d5343c01e1ed89ef15e3d9595f6c9fb1ec24f8275db15628a5b541ad5c1ff03`；云端 publish/delivery/isolated、直接 Release 资产、ZIP 内 EXE、仓库交付副本、任务交付副本与最终 isolated 副本逐字节一致。
- Authenticode：`NotSigned`，SignerCertificate 与 TimeStamperCertificate 均为空；Release 中文正文明确提示 SmartScreen/安全软件可能显示信誉提示。
- smoke：标签流水线 `SmokePID=8556`，通过 `http://127.0.0.1:7890` 回下载后的最终本地复核 `SmokePID=46072`；两次均 `ExitCode=0` 并自行退出。
- GitHub Release：[v1.2.1](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.2.1) 为非草稿、非预发布版本；标题精确为版本号 `v1.2.1`，正文使用中文逐项登记 12 项实际代码/测试变更、保持不变的契约边界、真实门禁结果、下载方式和构建来源，只有许可证要求逐字保留的 `Required Notice` 使用英文。
- 外层资产精确为 8 项；`SHA256SUMS.txt` 精确覆盖其余 7 项。代理回下载后的字节数与 SHA-256：

| Release 资产 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `ASSET_AND_PERSONA_RIGHTS.md` | 2,718 | `bc06b35871e87f0951a8866722a1217db544455b9ef196557dc1e10fc2ff1bf9` |
| `Jiayi-Desktop-Pet.exe` | 80,454,500 | `7d5343c01e1ed89ef15e3d9595f6c9fb1ec24f8275db15628a5b541ad5c1ff03` |
| `Jiayi-Desktop-Pet-README-zh-CN.txt` | 3,141 | `8e2e6c212a0d83229d5444c7affc06a1259b479955b4d29d9f9736af6c78540f` |
| `Jiayi-Desktop-Pet-win-x64.zip` | 80,468,760 | `5b13fb624fdf9bd12ef3eeca7be8316ffd8a480baac03a7e844b7f545a386ab5` |
| `LICENSE` | 4,563 | `c0ea4a896d2c8c394b29f9427589996db826cd501c512279ff0ed3ef48fabbe5` |
| `LICENSE-SCOPE.md` | 2,949 | `37f7e39235dd7e37724ed4872d52e035f1958f38dc959e1eaeaf33f91f829fc6` |
| `NOTICE` | 235 | `fd7ed21b4c71bbfd505f632e092b9b68d9e7e2c4d1e457f28b50d827b8f08b8b` |
| `SHA256SUMS.txt` | 616 | `7f3c2782286a72cb7cbfabd21a2386065590ba4b61367e7f6aad4720220fcec4` |

- ZIP 无子目录，精确平铺 `佳怡桌宠.exe`、`使用说明.txt`、`LICENSE`、`LICENSE-SCOPE.md`、`ASSET_AND_PERSONA_RIGHTS.md`、`NOTICE`；包内 EXE 与外层 Release EXE 的 SHA-256 均为 `7d5343c01e1ed89ef15e3d9595f6c9fb1ec24f8275db15628a5b541ad5c1ff03`。
- v1.2.0 初始标签 run 30418294065 在 Python 3.11 因 `sum(tone_ratio)==1.0` 的精确浮点比较失败；生产结果为 `0.9999999999999999`，业务值正确。v1.2.1 将该测试改为近似比较，并先在 main run 30418888675 完整通过后再创建新标签；未移动或复用失败的 v1.2.0 标签。

### 7.4 `v1.3.0` 最终登记证据（2026-07-29）

- 构建来源：annotated tag `v1.3.0` 精确指向 `20fcbe2051e4d1c0f382a59aab5f30b22b8462f5`，该提交等于当时的 `origin/main`；.NET SDK 为 `9.0.301`，EXE `ProductVersion=1.3.0+20fcbe2051e4d1c0f382a59aab5f30b22b8462f5`。
- CI：[main run 30450632805](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30450632805) 与 [v1.3.0 tag run 30450700771](https://github.com/haohaizi554/CompanionDesktopPet/actions/runs/30450700771) 均成功；标签流水线通过 Python `375/375`、.NET Release `641/641`、模拟 `1,500/1,500` 且 `0 hard violations`、validator `0 hard errors / 0 warnings`。
- 正式 EXE：`79,112,877` 字节，SHA-256 为 `9d20f5a546d10c65ac5b65558dbcc722f96ceef5e63fb89484dcb69bc420d5e6`；云端 publish/delivery/isolated、直接 Release 资产、ZIP 内 EXE、仓库交付副本、任务交付副本与最终 isolated 副本逐字节一致。
- Authenticode：`NotSigned`，SignerCertificate 与 TimeStamperCertificate 均为空；Release 中文正文明确提示 SmartScreen/安全软件可能显示信誉提示。
- smoke：标签流水线 `SmokePID=1952`，通过 `http://127.0.0.1:7890` 回下载后的最终本地复核 `SmokePID=32112`；两次均 `ExitCode=0` 并自行退出。
- GitHub Release：[v1.3.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.3.0) 为非草稿、非预发布版本；标题精确为版本号 `v1.3.0`，正文用中文逐项说明 30,000 条 authored runtime、0 legacy、1,190 scenes、关系画像精确分布、暴露配额、测试/模拟结果、下载和构建来源。
- 外层资产精确为 8 项；`SHA256SUMS.txt` 精确覆盖其余 7 项。代理回下载后的字节数与 SHA-256：

| Release 资产 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `ASSET_AND_PERSONA_RIGHTS.md` | 2,718 | `bc06b35871e87f0951a8866722a1217db544455b9ef196557dc1e10fc2ff1bf9` |
| `Jiayi-Desktop-Pet.exe` | 79,112,877 | `9d20f5a546d10c65ac5b65558dbcc722f96ceef5e63fb89484dcb69bc420d5e6` |
| `Jiayi-Desktop-Pet-README-zh-CN.txt` | 3,141 | `3fb55291c448a8d031c44326159a8093d6ce950904b43fed52d9a6f9f32a8abe` |
| `Jiayi-Desktop-Pet-win-x64.zip` | 79,127,137 | `1ea1799d4e2cdfb13703ad6b815557a83c704da2cc1835f67280be7ed8f7907e` |
| `LICENSE` | 4,563 | `c0ea4a896d2c8c394b29f9427589996db826cd501c512279ff0ed3ef48fabbe5` |
| `LICENSE-SCOPE.md` | 2,949 | `37f7e39235dd7e37724ed4872d52e035f1958f38dc959e1eaeaf33f91f829fc6` |
| `NOTICE` | 235 | `fd7ed21b4c71bbfd505f632e092b9b68d9e7e2c4d1e457f28b50d827b8f08b8b` |
| `SHA256SUMS.txt` | 616 | `e61bd9658d14536929bf8a480c3dac71bc96c22017036564c3c0f70cb1be6be3` |

- ZIP 无子目录，精确平铺 `佳怡桌宠.exe`、`使用说明.txt`、`LICENSE`、`LICENSE-SCOPE.md`、`ASSET_AND_PERSONA_RIGHTS.md`、`NOTICE`；包内 EXE 与外层 Release EXE 的 SHA-256 均为 `9d20f5a546d10c65ac5b65558dbcc722f96ceef5e63fb89484dcb69bc420d5e6`。

版本化文件不能可靠记录“包含自身的最终提交 SHA”，因为写入该 SHA 会再次改变提交。本节只记录实际产生 EXE 的 built-from source commit；artifact/docs commit 与最终 `main` SHA 以 Git 远端结果为准。

## 8. 发布后清理

完成哈希登记和 verifier 后，可删除精确的 scratch 目录；保留最终交付目录，不清理不可变 source/archive/review/report：

```powershell
foreach ($path in @($publishDir, $verifyDir)) {
  if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Cleanup target escaped repository root: $path"
  }
  Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
}
git status --short
```

最终 `git status --short` 只允许预期的源代码、数据、报告、文档和交付 EXE 变化；`bin/`、`obj/`、`publish/`、`outputs/verify/`、`__pycache__/`、临时报告、额外 EXE 或 sidecar 都是清理/审阅信号，不能盲目纳入提交。

2026-07-29 最终清理已执行 `git worktree prune`；`git worktree list --porcelain` 与 `D:\desktop` 目录核对均只剩 `D:\desktop\CompanionDesktopPet`，仓库状态干净，无额外 `CompanionDesktopPet-*` 工作树目录。

## 9. 已关闭审计项与发布结论

- 已关闭：30,000 authored runtime、0 legacy surface、1,190 scenes、30,000 ledger 与 75,375 archive 已精确验证并登记 SHA-256。
- 已关闭：simulation 为 1,500/1,500 outputs、0 hard violations；联合 validator 为 0 hard errors / 0 warnings。
- 已关闭：scene-first、关系画像配额、authored identity exact policy、runtime/ledger 一一绑定均有 Python 与 C# 自动化测试。
- 已关闭（v1.0.0 历史基线）：标签流水线的 Python 311/311 与 .NET Release 392/392 均为实际非零执行结果，不是仅凭进程退出码推断；CI 依赖闭包固定版本、wheel SHA-256 与完整传递依赖。
- 已关闭（v1.2.1 历史 Release）：built-from、SDK、ProductVersion、字节数、SHA-256、签名状态、两个 smoke、ZIP 清单与 8 项资产均保留在第 7.3 节。
- 已关闭（v1.3.0 当前 Release）：`outputs/CompanionDesktopPet/佳怡桌宠.exe` 已替换为从不可变 Release 代理回下载并复核的正式 EXE；built-from、SDK、ProductVersion、字节数、SHA-256、签名状态、两个 smoke、ZIP 清单、8 项资产与具体中文 Release 均已在第 7.4 节登记。
