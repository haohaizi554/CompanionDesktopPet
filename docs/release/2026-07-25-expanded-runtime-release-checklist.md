# Expanded Runtime 发布与清理清单

日期：2026-07-25
状态：最终哈希待集成提交复核；任何占位未清零都阻止发布

本文是 52,132 条 expanded runtime 的发布门禁。它不授权修改不可变 source，也不把当前分支中尚未集成的扩展文件描述为已完成。

## 1. 精确验收常量

| 项目 | 必须精确等于 | 定义 |
| --- | ---: | --- |
| curated core | 806 | 非 `legacy_surface_variant` 的启用行 |
| safe legacy surfaces | 51,326 | `source_kind=legacy_surface_variant` 且存在精确 surface-manifest 绑定的启用行 |
| expanded runtime | 52,132 | core + safe surfaces |
| semantic scenes | 533 | expanded runtime 中唯一 `semantic_group` 数 |
| immutable source | 75,375 | 原始 TSV 物理行数，含表头 |
| archive dispositions | 75,375 | expanded build 的 archive 数据记录数 |

52,132、51,326 与 533 在本清单创建时仍是待最终集成校验常量。任何并行快照、旧报告或口头统计都不能替代在最终提交上的重新计数。

## 2. 运行时选择与暴露门禁

- 调度必须 scene-first：先按 `semantic_group` 过滤、评分并选择场景，再在所选场景内选变体。变体数量不得增加场景权重，点击兜底也不得先全局挑行再倒推场景。
- Easter egg 播放率必须为 8%–12%；seasoning 播放率必须为 3%–6%；dry-sharp 播放率必须为 2%–4%。它们是模拟播放率，不是库存行占比。
- seasoning 使用 contract 的 NFKC/casefold 与 token-boundary 规则，identity markers 明确排除在 seasoning 之外；最近 20 次最多暴露 1 次 seasoning。
- dry-sharp 的场景资格由稳定 scene hash 决定；它不得用于 care、emotional、Easter egg、late-night、holiday 或 anniversary 场景。contract 中 4%–6% 的 dry-sharp scene inventory 与 2%–4% 的 playback acceptance 是两个不同门禁。
- surface 变体须在场景内遵守近期 opener/ending/template 冲突约束；只有该场景的所有合格 surface 都冲突时才允许进入明确测试过的 fallback。

## 3. 身份与隐私边界

- `config/persona-editorial-manifest.json` 是身份彩蛋的 exact allowlist。批准必须同时匹配稳定 ID、variant、来源、允许的 identity marker、文本 SHA-256、category/group、cooldown、`max_per_day` 与 weight。
- `data/optimized/persona-surface-manifest.tsv` 是安全 legacy surface 的逐行证据，必须绑定 line ID、variant、source line、category/group/topic、source reference、原始文本摘要和 source 摘要；缺失、额外或重复记录全部是硬失败。
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

`simulation-events.json` 必须使用 schema v2，并同时绑定 corpus SHA-256、scheduler config SHA-256、subseed derivation version 与 derivation SHA-256；任一字段漂移都必须产生 `simulation_replay_binding_mismatch` 硬错误。校验器必须为 `0 hard errors`，并且 warning 只能精确等于一条 `surface_inventory_observation`，其他 warning 一律阻断发布。

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

`Verify-Publish.ps1` 必须确认 publish 目录只有 `CompanionDesktopPet.exe`、交付目录只有一个 EXE 和允许的 `使用说明.txt`、两份 EXE 哈希相同，并在 `outputs/verify/` 隔离启动 `--smoke-test`。它只跟踪本次 PID；不要使用 `Stop-Process -Name` 清理无关桌宠进程。

## 7. 发布哈希登记

| 发布对象 | SHA-256 |
| --- | --- |
| immutable source / byte copy | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| expanded runtime v2 | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| archive | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| review | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| PII review | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| surface manifest | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| final `佳怡桌宠.exe` | `<PENDING_FINAL_RELEASE_SHA256>` |

填入哈希后重新运行计数、可复现比较、验证器、simulation、.NET 测试与 publish verifier。记录最终 commit、EXE 大小、哈希、测试数、simulation 比例、smoke PID/退出码和 cleanup 状态。

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

## 9. 当前审计风险（在最终集成提交上逐项关闭）

- 本审计基线尚无 51,326-row surface manifest 或 52,132-row expanded v2；不得把待校验常量写成当前事实。
- 并行构建快照曾出现 528 与 533 scenes 的差异；最终提交必须以唯一 `semantic_group` 重算并精确得到 533。
- 旧 simulation report 仍使用早期 Easter 比例，导致基线 Python 套件出现两项 `easter_egg_ratio_above_limit`；必须从最终语料重生成报告，不能放宽测试。
- 不同集成快照的 .NET test discovery 结果需要在最终提交上重新核对；发布记录必须包含实际执行/通过的非零测试数，不能只把 `dotnet test` 进程退出码 0 当成 .NET 全绿。
- 基线点击深层兜底仍有“全局最旧行优先”的历史路径；最终 runtime 必须用测试证明 fallback 也保持 scene-first。
- 身份 manifest 测试必须断言完整 exact set，不得只接受一个宽范围数量；surface manifest 必须与 runtime 一一对应。
- 旧历史若没有 seasoning/dry 暴露字段，迁移策略必须显式测试，避免升级后的 recent-window 暂时遗忘旧播放。
- 900-click 长时测试接近 512 MiB 预算时要记录峰值；内存压力不能通过减少覆盖或跳过 expanded runtime 来隐藏。
- `outputs/CompanionDesktopPet/佳怡桌宠.exe` 是跟踪文件，可能落后于源码；只有重新 publish、哈希匹配和隔离 smoke 通过后才可提交。
