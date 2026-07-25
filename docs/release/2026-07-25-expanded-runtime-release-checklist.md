# Expanded Runtime 发布与清理清单

日期：2026-07-25
状态：Phase 4A 语料/仿真证据与 Phase 4B 单 EXE 发布证据均已完成

本文是已集成 52,132 条 expanded runtime 的发布门禁。它不授权修改不可变 source；当前计数、可复现重建、模拟与单 EXE 隔离烟测证据均已重新核对。Phase 4B 交付物已从固定且已推送的干净提交构建；artifact commit 与最终 `main` SHA 以远端 Git 结果为准。

## 1. 精确验收常量

| 项目 | 必须精确等于 | 定义 |
| --- | ---: | --- |
| curated core | 806 | 非 `legacy_surface_variant` 的启用行 |
| safe legacy surfaces | 51,326 | `source_kind=legacy_surface_variant` 且存在精确 surface-manifest 绑定的启用行 |
| expanded runtime | 52,132 | core + safe surfaces |
| semantic scenes | 533 | expanded runtime 中唯一 `semantic_group` 数 |
| immutable source | 75,375 | 原始 TSV 无表头物理数据行数 |
| archive dispositions | 75,375 | expanded build 的 archive 数据记录数 |

当前文件已重新计数为 806 core、51,326 safe legacy surfaces、52,132 runtime、533 个唯一 `semantic_group`、75,375 条 source 与 75,375 条 archive。它们既是现物统计也是精确发布常量；任何后续差异都阻止发布。

## 2. 运行时选择与暴露门禁

- 调度必须 scene-first：先按 `semantic_group` 过滤、评分并选择场景，再在所选场景内选变体。变体数量不得增加场景权重，点击兜底也不得先全局挑行再倒推场景。
- Easter egg 播放率必须为 8%–12%；seasoning 播放率必须为 3%–6%；dry-sharp 播放率必须为 2%–4%。它们是模拟播放率，不是库存行占比。
- seasoning 使用 contract 的 NFKC/casefold 与 token-boundary 规则，identity markers 明确排除在 seasoning 之外；最近 20 次最多暴露 1 次 seasoning。
- dry-sharp 的场景资格由稳定 scene hash 决定；它不得用于 care、emotional、Easter egg、late-night、holiday 或 anniversary 场景。contract 中 4%–6% 的 dry-sharp scene inventory 与 2%–4% 的 playback acceptance 是两个不同门禁。
- surface 变体须在场景内遵守近期 opener/ending/template 冲突约束；只有该场景的所有合格 surface 都冲突时才允许进入明确测试过的 fallback。

## 3. 身份与隐私边界

- `config/persona-editorial-manifest.json` 是身份彩蛋的 exact allowlist。批准必须同时匹配稳定 ID、variant、来源、允许的 identity marker、文本 SHA-256、category/group、cooldown、`max_per_day` 与 weight。
- `data/optimized/persona-surface-manifest.tsv` 是安全 legacy surface 的逐行证据，必须绑定 line ID、variant、source line、category/group/topic、source reference、原始文本摘要和 source 摘要；缺失、额外或重复记录全部是硬失败。
- `source_line` 是冻结 source SHA 下从 1 开始的物理数据行号。source 的任何字节变化、插入、删除或重排都开启新的 lineage epoch，必须重新生成、复核和审批全部派生产物；冻结 SHA 不变时不得重编号当前 51,326 个 surface ID。
- 问句/回复钩子、未批准身份、非身份 PII、不可用上下文、面向用户的当前状态断言、控制字符、过度命令式文本和规范化重复不得进入运行时。
- archive/source/review 只提供审计证据，不自动构成运行时许可。不得用宽泛 marker 扫描或 EXE 原始字节搜索代替 manifest 审批。
- 桌宠继续完全离线，不读取输入内容、剪贴板、文件名或窗口标题；自动规则不能替代人物授权、虚构身份、关系边界和再分发权利的人工批准。

## 4. 异步预热与 fallback

- 窗口先显示；记忆、52k 语料和 533-scene catalog 在后台预热，不能阻塞 Dispatcher/UI 线程。
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
  --output (Join-Path $reproDir 'persona-corpus-v2.tsv') `
  --report-output (Join-Path $reproDir 'pii-review.tsv') `
  --seed 20260722 `
  --pii-policy review

$pairs = @(
  @('data\optimized\persona-corpus-v2.tsv', 'persona-corpus-v2.tsv'),
  @('data\optimized\persona-corpus-archive.tsv', 'persona-corpus-archive.tsv'),
  @('data\optimized\persona-corpus-review.tsv', 'persona-corpus-review.tsv'),
  @('reports\pii-review.tsv', 'pii-review.tsv'),
  @('data\optimized\persona-surface-manifest.tsv', 'persona-surface-manifest.tsv')
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

surfaces = sum(row['source_kind'] == 'legacy_surface_variant' for row in runtime)
core = len(runtime) - surfaces
scenes = len({row['semantic_group'] for row in runtime})
source_lines = sum(
    1 for _ in (root / 'data/source/persona-corpus.original.tsv').open(
        encoding='utf-8-sig', newline=''
    )
)

assert core == 806, core
assert surfaces == 51_326, surfaces
assert len(runtime) == 52_132, len(runtime)
assert scenes == 533, scenes
assert source_lines == 75_375, source_lines
assert len(archive) == 75_375, len(archive)
print('PASS: corpus counts 806 + 51,326 = 52,132; 533 scenes; 75,375 source/archive')
'@ | python -
```

## 6. 测试、模拟与发布

必须从最终语料重新生成 simulation events/report；旧 contract 下的报告不可复用。

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

`simulation-events.json` 必须使用 schema v3，并为每次尝试记录精确的 `seed/day_index/slot_index`，同时绑定 corpus SHA-256、scheduler config SHA-256、subseed derivation version 与 derivation SHA-256。校验器必须按规范时间、上下文、subseed 和逐 seed 历史重新执行选择器并比对精确 `selected_id`；任一绑定字段、坐标、上下文、顺序或选择结果漂移都必须产生硬错误。校验器必须为 `0 hard errors`，并且 warning 只能精确等于一条 `surface_inventory_observation`，其他 warning 一律阻断发布。

### 6.1 Phase 4A 新鲜验证记录

- 两项 generator `--check` 均通过；隔离重建的 v2、archive、review、PII review 与 surface manifest 五份产物逐字节匹配 canonical。
- 精确计数：806 core + 51,326 surfaces = 52,132 runtime；533 scenes；75,375 source 数据行；75,375 archive；51,326 surface-manifest 记录。
- 30 天 × 10 seeds：1,500 attempts / 1,500 outputs；Easter egg 9.87%、seasoning 4.93%、dry-sharp 4.00%；natural、adversarial 与 combined hard violations 均为零。
- Validator：`0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。
- Python：实际执行并通过 300/300；.NET Release：测试项目门禁为 `IsTestProject=true`，实际执行并通过 389/389。
- Release 回归还顺带发现并消除了节日候选断言对 52,132 行重复全表扫描的二次复杂度；修复后完整 Release 套件在 33 秒内完成。

### 6.2 Phase 5 精确回放与运行时比例门禁

- validator-facing events 已升级为 schema v3；1,500 条事件按 `seed/day_index/slot_index` 完整排序，校验器会重新构造规范时间、上下文、subseed、逐 seed 历史与精确 `selected_id`。缺失、额外、乱序、上下文/时间篡改和同场景 surface 偷换均为硬错误。
- 四季、04:00–05:59 dawn、`ide_foreground/idle_return/fullscreen` 的 `null/false/true`，以及 `active_minutes` 的 `null/89/90/91` 均有硬覆盖门禁。
- canonical replay 上限为 3,000 次；声明规模或原始事件数超限、raw/parsed 数量不完整时均在 selector 前拒绝。相同完整输入只复用容量为 2 的规范答案流缓存，每次事件仍逐条重新比对。
- C# 运行时通过真实 `OfflineCompanionAgent → SceneScheduler → PersonaCorpus` 路径验证：10 seeds × 30 days × 4 slots = 1,200 outputs，Easter egg 为 120/1,200（10.00%），每 seed 均为 10.00%。接受区间由共享 contract 生成，不在测试中另行硬编码。
- 主代理最终复验：Python `311/311`（140.4 秒）；.NET Release `392/392`、0 跳过；validator 为 `0 hard errors / 1 warning`，唯一 warning 为 `surface_inventory_observation`。

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
| expanded runtime v2 | `3335d72e695528892ddec92076f0f02abacf58fff02ed6bd0aadf67d1cf0cc40` |
| archive | `b7d9a5f2fd6f4750ea2b688206f77bf45a2b59ca12c09f36281c72efc620721d` |
| review | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| PII review | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |
| surface manifest | `bcf9c97be0e4b1d7b7db11fcb46f44de17ef0ade6cb2e79d69f8af69bdbc637d` |
| persona contract | `8bb42a5f14a3e180b1a8b9c72e12dceb0f1701798f98d346c370d792392eb7dd` |
| scheduler raw bytes | `7bbf3f1d5b6dc5c51a8758ebd3bc05f4a0e3d8d1f97c53e77ed240b3650e1a40` |
| scheduler semantic binding | `4eaa40cd28d58aaa9dcecaaded539f25ceb39b35a4fc1cd9012d422cd414b462` |
| editorial manifest | `ce03fcbe4bb4de0f61ab81e29075ed80eb30bfe921bb1499e5514a1a3c5ad7b5` |
| subseed derivation v2 | `e5f6d36ffb5d4936bccca24cb9c7177a63e02d937118342916bd5eea0a83640d` |
| simulation report | `09d67f3b69fb97f871337fc6e2a6b5a4a4c9897c680af3551796091764e090e2` |
| validator-facing simulation events | `5fddf3a0c05705da9ff97f7a1b339b664ee8dbcf1e81318e09267e815bc1d9da` |
| final `佳怡桌宠.exe` | `cc69d4b555ac438641f805cbe3d51cf8b7d04627d1eda0837dbd089a3bdc6d4e` |

### 7.1 Phase 4B 发布实证

- built-from source commit：`dbc587243f209f785fc57de4da507229a41e0509`；构建前 tracked worktree 干净且该 SHA 已推送到远端。
- 工具链：.NET SDK `9.0.301`；EXE `ProductVersion=1.0.0+dbc587243f209f785fc57de4da507229a41e0509`，可从二进制反查 built-from。
- 最终 EXE：`80,429,415` 字节；publish、delivery、isolated 三份 SHA-256 均为 `cc69d4b555ac438641f805cbe3d51cf8b7d04627d1eda0837dbd089a3bdc6d4e`。
- publish 清单精确为一个 `CompanionDesktopPet.exe`；delivery 清单精确为 `佳怡桌宠.exe` 与允许的 `使用说明.txt`，无应用 DLL/JSON/PDB 或额外目录。
- 最终隔离 smoke：`SmokePID=43416`，`ExitCode=0`，进程自行退出且未按进程名清理；完整发布验证器契约测试通过，包含隐藏 publish DLL、隐藏 delivery JSON 与隐藏目录拒绝用例。
- Phase 4B 提交前全量复核：Python `300/300`、.NET Release `389/389`，均为 0 失败、0 跳过。
- 证据登记后已清理 `publish/`、`outputs/verify/`、`outputs/verify-contract-test/` 与 `outputs/verify-contract-helpers/`；交付目录保留。
- Authenticode 状态为 `NotSigned`。这不改变单文件与离线门禁，但 GitHub/网络下载可能触发 SmartScreen 或安全软件信誉提示；未配置代码签名证书前不得宣称“下载后无安全提示”。

版本化文件不能可靠记录“包含自身的最终提交 SHA”，因为写入该 SHA 会再次改变提交。本节只记录实际产生 EXE 的 built-from source commit；Phase 4B artifact commit 与最终 `main` SHA 以 Git 远端结果为准。

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

## 9. 已关闭审计项与发布结论

- 已关闭：surface manifest 为 51,326 行，expanded v2 为 52,132 行，唯一 `semantic_group` 精确为 533；五份隔离重建产物与 canonical SHA-256 全部一致。
- 已关闭：simulation 已用当前 scheduler semantic binding 重放，1,500/1,500 attempts 有输出；Easter egg 9.87%、seasoning 4.93%、dry-sharp 4.00%，natural/adversarial/combined hard violations 全为零，dawn 与四季、nullable signals 均覆盖。
- 已关闭：scene-first fallback、identity exact set、surface/runtime 一一绑定、旧 seasoning/dry 历史迁移均有自动化测试；900-click retained-memory 门槛已收紧为 256 MiB，不通过缩减 runtime 规避。
- 已关闭：Phase 4A 的 Python 300/300 与 .NET Release 389/389 均为实际非零执行结果，不是仅凭进程退出码推断。
- 已关闭：`outputs/CompanionDesktopPet/佳怡桌宠.exe` 已从干净且已推送的 `dbc5872` 重建；built-from、SDK、ProductVersion、字节数与 SHA-256 均已登记，publish/delivery/isolated 三份哈希一致，隔离 `--smoke-test` 自行以退出码 0 结束，Phase 4B 完成。
