# Persona Corpus v2 维护与验证说明

本文说明佳怡桌宠离线角色语料的来源、20 列契约、确定性流水线、运行时选择规则与发布边界。所有命令均从仓库根目录运行；流水线只使用 Python 标准库，不调用网络、模型 API 或数据库。

> 版本口径：2026-07-22 文档中的“约 800 条”和 Easter egg `<=2%` 是历史初版目标，已被 2026-07-24 persona contract 与 expanded-runtime 方案取代。当前审计基线可验证 curated core 为 806 条；最终发布常量为 52,132 条运行时文案、51,326 条安全 legacy surfaces 与 533 个语义场景。扩展文件和最终哈希在本基线尚未集成，下文将其明确标作待校验，不能据此宣称现有文件已经达标。

## 1. 为什么 75,375 行不是 75,375 条独立内容

不可变源证据位于 `src/CompanionDesktopPet/Assets/persona-corpus.tsv`，共 75,375 个物理行（含表头）。基线审计发现大量共享开头、主题句和结尾：例如多个技术分类各有 3,900 行，相同长结尾各重复 3,600 次，且有 455,894 对有界近重复候选。这些行主要是前缀 × 主题 × 后缀的笛卡尔组合，不等于 75,375 个独立写作意图。

curated core 不在构建时或运行时重新拼接片段，而是提供 806 条能独立播放的完整句子。expanded runtime 在 core 之外只加入 51,326 条通过安全筛选、保持原文并有精确 lineage/manifest 的 legacy surfaces，因此最终启用总数必须是 `806 + 51,326 = 52,132`；原始 75,375 个证据位置仍逐一保存在 archive、review 和来源映射中，不因进入运行时而被改写或删除。

## 2. 不可变源与 SHA-256 门禁

真实源路径是 `src/CompanionDesktopPet/Assets/persona-corpus.tsv`，字节副本是 `data/source/persona-corpus.original.tsv`。两者不得改写，当前共同 SHA-256 为：

```text
3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534
```

验证命令：

```powershell
$source = Get-FileHash src/CompanionDesktopPet/Assets/persona-corpus.tsv -Algorithm SHA256
$copy = Get-FileHash data/source/persona-corpus.original.tsv -Algorithm SHA256
if ($source.Hash -ne $copy.Hash) { throw 'Immutable source hash mismatch.' }
```

### 2.1 发布拓扑与当前校验状态

| 层 | 最终发布常量 | 本审计基线状态 |
| --- | ---: | --- |
| immutable source | 75,375 个物理行（含表头） | 已验证，双副本 SHA-256 相同 |
| archive | 75,375 条 disposition 记录 | expanded build 集成后重算 |
| curated core | 806 条 | 已验证 |
| safe legacy surfaces | 51,326 条 | 待集成 `persona-surface-manifest.tsv` 后验证 |
| expanded runtime | 52,132 条 | 待集成并验证，必须等于 core + surfaces |
| semantic scenes | 533 个 | 待集成并按唯一 `semantic_group` 重算 |

这里的 52,132、51,326 和 533 是最终发布的精确验收值，不是范围；任一计数不符都阻止发布。它们也不是本审计基线当前 `persona-corpus-v2.tsv` 的文件统计。最终哈希必须在集成提交上通过可复现重建得到，见本文末尾和 [发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。

## 3. v2 的 20 个字段

字段顺序是协议的一部分，不能增删或换序。

| # | 字段 | 含义 |
| ---: | --- | --- |
| 1 | `id` | 稳定且唯一的语料 ID |
| 2 | `category` | 编辑分类 |
| 3 | `category_group` | 调度组，如 `technical`、`character_life` |
| 4 | `topic_id` | 可追溯的主题 ID |
| 5 | `semantic_group` | 语义冷却和相邻约束使用的分组 |
| 6 | `output_mode` | `self_talk`、`ambient`、`user_direct` 或 `system_observe` |
| 7 | `trigger` | 允许播放的事件/时间触发器 |
| 8 | `required_context` | 必须由上下文证明的受控 token；无要求时为 `none` |
| 9 | `tone` | 受控语气枚举 |
| 10 | `interrupt_cost` | 0–5 的打扰成本 |
| 11 | `cooldown_hours` | 同 ID 冷却小时数 |
| 12 | `semantic_cooldown_hours` | 同语义组冷却小时数 |
| 13 | `max_per_day` | 每个本地自然日的单行上限 |
| 14 | `weight` | 通过硬过滤后的相对选择权重 |
| 15 | `requires_reply` | 是否要求用户回应；启用行必须为 `false` |
| 16 | `enabled` | 是否能进入运行时；桌宠只加载 `true` |
| 17 | `text` | 不含制表符/真实换行的完整独立句子 |
| 18 | `source_kind` | 原始、改写或策划创作等来源类型 |
| 19 | `source_reference` | 原始行、主题和变体的可审计引用 |
| 20 | `rewrite_reason` | 改写或新增原因 |

## 4. 基线审计

```powershell
python tools/audit_corpus.py `
  --input src/CompanionDesktopPet/Assets/persona-corpus.tsv `
  --output reports/corpus-audit-before.md
```

审计采用稀有字符 3-gram 候选桶，不对 75,375 行做全量两两比较。

## 5. 结构抽取

```powershell
python tools/extract_corpus_structure.py `
  --input src/CompanionDesktopPet/Assets/persona-corpus.tsv `
  --output-dir data/intermediate
```

输出前缀、主题、后缀和 `source-line-map.tsv`，只用于审计与迁移，不用于运行时拼句。

## 6. 确定性构建

```powershell
python tools/build_corpus_v2.py `
  --input src/CompanionDesktopPet/Assets/persona-corpus.tsv `
  --mappings data/intermediate/source-line-map.tsv `
  --output data/optimized/persona-corpus-v2.tsv `
  --seed 20260722 `
  --pii-policy review
```

expanded builder 同时生成 v2、archive、review、`reports/pii-review.tsv` 与 `data/optimized/persona-surface-manifest.tsv`。同一不可变源、映射、contract、editorial manifest、构建代码和种子必须产生全部五个字节一致的输出；发布验证应在隔离临时目录重建后逐一比较哈希，不能只在原位覆盖 v2 再比较单个文件。

## 7. 严格校验

```powershell
python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json
```

合格输出是 `Validation: 0 hard errors, 0 warnings`。白名单只允许精确匹配并附理由，不能用宽泛正则掩盖新问题。

## 8. 30 天、多种子模拟

```powershell
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 10 `
  --report reports/simulation-report.md
```

`--seeds 10` 精确定义为种子 `0..9`。模拟必须为零硬约束违规，事件 JSON 和 Markdown 报告在相同输入下字节一致。

最终 expanded runtime 的播放暴露率门禁是：Easter egg 8%–12%、seasoning 3%–6%、dry-sharp 2%–4%。三者都是模拟播放次数占比，不是 TSV 行数占比；身份 marker 不算 seasoning。dry-sharp 另有按场景计算的 inventory 规则，不能拿行变体占比替代。任何旧报告中的 `<=2%` Easter 结论均已过期，必须用最终语料和当前 contract 重新生成报告。

## 9. 选择器验证与调用界面

选择器是库 API，不提供会修改仓库的独立 CLI。完整选择器合同用以下命令验证：

```powershell
python -m unittest tests.test_selector -v
```

运行时入口是 `src.persona_corpus.selector.select_line(corpus, context, history, now, seed, scheduler_config=...)`。`now` 必须带时区；失败、不可信上下文或无合格候选时返回 `None`，且不追加历史。

## 10. 十二阶段选择约束

最终运行时采用 scene-first：先按 `semantic_group` 聚合场景，过滤触发器、必需上下文、语义冷却、每日上限、相邻/窗口配额、最小间隔、滚动小时预算与夜间预算，再按组缺口、模式缺口、场景权重和打扰成本选择场景；只有场景确定后，才在该场景内选择通过 ID 冷却与 surface 暴露约束的变体。场景权重不得随变体数量增长，fallback 也不得先在全库挑行再倒推场景。加权随机只在最高分带内进行，并使用局部 `random.Random(seed)`，不会污染全局随机状态。

## 11. 上下文与未来信号

当前可信信号来自本地时区日期、周末、时段、季节、应用启动、节日、纪念日、月边界和长静默。`ide_foreground`、`active_minutes`、`idle_return`、`fullscreen` 是未来信号：没有可靠采集时必须为 `None`，不能根据窗口标题、文件名、输入内容或主观推断伪造上下文。只有明确为真/假时才生成对应受控 token。

## 12. 历史与确定性

选择历史保存 `selected_id`、播放时间、分类、分类组、语义组、输出模式、触发器和打扰成本。冷却与预算均从该历史重算；排序稳定且固定种子可复现。历史时间倒退、未知结构或无时区时间会安全失败。

## 13. PII 与人物边界

真实姓名、地区迁徙、收入、打零工经历、亲昵称呼、未经授权的关系或身份暗示默认不进入启用集。身份彩蛋是唯一例外：必须在 `config/persona-editorial-manifest.json` 中按稳定 ID 精确批准，并同时绑定来源、允许的 identity marker、文本 SHA-256、分类/分组、冷却、每日上限与权重；宽泛 marker 命中、EXE 原始字节扫描或“看起来像彩蛋”都不构成批准。自动规则只负责发现、精确核对与隔离，不能替代人物授权、虚构身份和关系边界的人工判断。

## 14. archive、review 与人工审批

明确不适合独立播报、要求回应或违反边界的源行进入 archive；PII、亲密度、上下文或语气不确定的内容进入 review；`reports/pii-review.tsv` 和 `reports/corpus-manual-review.md` 保存人工复核证据。安全 legacy surface 还必须在 `persona-surface-manifest.tsv` 中精确绑定 line ID、variant、源行、category/group/topic、source reference、原始文本摘要和源摘要。任何放行都必须可追溯到具体来源与复核理由；archive/source 的存在本身不等于运行时批准。

## 15. 扩展规则

新增内容必须是完整句子，并先加入明确的主题/语义组；禁止新增 opener/core/closer 组合数组。填写全部 20 列，保持 ID 稳定、`requires_reply=false`、无问号、无规范化重复、无制表符或真实换行，并给出 `source_reference` 与 `rewrite_reason`。修改后必须重建、校验、模拟并运行 Python/.NET 全套测试。

## 16. WPF 集成

`CompanionDesktopPet.csproj` 只把最终 `data/optimized/persona-corpus-v2.tsv` 以逻辑名 `CompanionDesktopPet.Assets.persona-corpus-v2.tsv` 嵌入应用。旧的 `src/CompanionDesktopPet/Assets/persona-corpus.tsv` 仅是不可变源证据，不重新嵌入运行时。WPF 按字段名解析精确表头，只加载 806 core 与 manifest 批准的安全 surfaces，并把场景/变体选择、语义冷却、每日上限、组配额、seasoning/dry 暴露和打扰成本交给离线运行时选择器。

## 17. 完整测试门禁

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

任何失败都阻止交付；.NET 门禁还必须记录实际执行的非零测试数，禁止把 VSTest 跳过但退出 0 当作通过。

此外，最终门禁必须断言 806 core、51,326 surfaces、52,132 runtime rows、533 scenes 和 75,375 archive dispositions；模拟报告必须从最终输入重新生成，不能复用旧 contract 下的结果。

## 18. 单文件发布与隔离烟测

递归清理前必须把目标解析为仓库内的绝对路径。只清理 `publish/`、`outputs/verify/` 和明确的旧交付 EXE；不要递归删除整个交付目录，也不要按进程名批量终止桌宠。以下为简版，包含可复现语料、计数、哈希记录和发布后清理的完整顺序见 [发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。

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

验证器要求交付目录只有一个 EXE（可另有 `.txt` 说明），拒绝 DLL/PDB/JSON 等 sidecar，并核对交付 EXE 与 publish EXE 的 SHA-256。身份彩蛋只有在精确列入 editorial manifest 后才可进入运行时语料；安全边界由应用启动时对 `PersonaCorpus` exact editorial manifest 的自校验、Python validator 和程序集测试共同承担，不再使用无法区分已批准内容与泄露的 EXE 原始字节 marker 扫描。验证器把 EXE 单独复制到 `outputs/verify/` 后以 `--smoke-test` 启动；应用必须在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束。超时、需要强杀或非零退出均失败，强杀只用于清理残留 PID。

## 19. 桌宠交互与隐私

保留左键点击爱心、拖拽时倾斜和松手落地回弹；自然闭眼图层叠加式眨眼不压缩整张人物图。启动后会在本地显示一次“嗨♡”，右键菜单也提供 `打个招呼♡`；这两种打招呼都只使用固定的本地 UI，不从语料生成。`暂停动画` 只暂停并复位待机动作、自动眨眼和问候，点击爱心、拖动/落地和台词仍可使用。仍不提供 wink 或假手挥手，没有旧 `GetGreeting`，也没有语料驱动的 `AnimationCue`/`PlayAmbientGesture`。窗口先显示，记忆、语料和场景在后台异步预热；预热期间启动/点击使用不读取个人信息的固定本地 fallback，自动触发保持静默，完整烟测必须等到真实语料回复。桌宠完全离线，不读取键盘输入内容、剪贴板、文件名或窗口标题；本机状态写入 `%LOCALAPPDATA%\CompanionDesktopPet`。身份彩蛋仅可按精确 editorial manifest 审批进入 `PersonaCorpus`；应用启动自校验该 exact manifest，并由 Python validator 与程序集测试阻止未审批内容进入运行时。

## 20. 限制与发布政策

调度只能使用已实现且可证明的信号，不理解屏幕内容或用户情绪；固定模拟不能证明所有真实时间序列，但会阻止已知硬约束回归。仓库未声明开源许可证；在人物素材、角色内容和再分发权利明确前，不得公开再发布素材、语料或构建产物。

## 确定性重建哈希门禁

发布前必须在仓库内的隔离临时目录以固定 seed `20260722` 重建，并逐字节比较 v2、archive、review、PII review 与 surface manifest；不要原位覆盖 canonical 数据来“证明”可复现。完整命令见 [发布与清理清单](docs/release/2026-07-25-expanded-runtime-release-checklist.md)。

| 发布对象 | SHA-256 |
| --- | --- |
| `src/CompanionDesktopPet/Assets/persona-corpus.tsv` | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| `data/source/persona-corpus.original.tsv` | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| expanded `data/optimized/persona-corpus-v2.tsv` | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| `data/optimized/persona-corpus-archive.tsv` | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| `data/optimized/persona-corpus-review.tsv` | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| `reports/pii-review.tsv` | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| `data/optimized/persona-surface-manifest.tsv` | `<PENDING_FINAL_INTEGRATION_SHA256>` |
| `outputs/CompanionDesktopPet/佳怡桌宠.exe` | `<PENDING_FINAL_RELEASE_SHA256>` |

任何 `<PENDING_...>` 占位仍存在时都不得发布。
