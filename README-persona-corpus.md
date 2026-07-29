# Persona Corpus v2 维护与验证说明

本文说明佳怡桌宠离线角色语料的来源、20 列契约、确定性流水线、运行时选择规则与发布边界。所有命令均从仓库根目录运行；流水线只使用 Python 标准库，不调用网络、模型 API 或数据库。

> 版本口径：v1.3.0 当前运行时精确为 30,000 条 `curated_authored`、0 条 legacy surface、1,190 个唯一 `semantic_group`。100 个 authored 批次、authorship manifest、逐行 ledger、21 列 runtime schema 与关系画像配额共同构成发布契约；806/51,326/52,132/533 是 v1.2.1 历史基线。

## 1. 为什么 75,375 条源数据行不是 75,375 条独立内容

不可变源证据位于 `src/CompanionDesktopPet/Assets/persona-corpus.tsv`，共 75,375 条无表头物理数据行。基线审计发现大量共享开头、主题句和结尾：例如多个技术分类各有 3,900 行，相同长结尾各重复 3,600 次，且有 455,894 对有界近重复候选。这些行主要是前缀 × 主题 × 后缀的笛卡尔组合，不等于 75,375 个独立写作意图。

authored runtime 不在构建时或运行时拼接片段。`data/authored/v1/b001-b100.tsv` 每批 300 条，每条都是可独立播放的完整句子；builder 一对一生成 30,000 条 `curated_authored`。原始 75,375 条 legacy 数据逐一保存在 archive、review 和来源映射中，但不再物化为运行时 surface。

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

### 2.1 `source_line` 与 lineage epoch

`source_line` 是冻结 source SHA 下从 1 开始计数的物理数据行号；源文件没有表头，因此 `source_line=1` 就是第 1 条源数据。行号只有与对应的冻结 source SHA（即 lineage epoch）一起才构成完整身份，不能脱离该 SHA 单独解释。

任一 authored TSV 字节变化都必须重建 `persona-authorship-manifest.json`、30,000 行 ledger、runtime TSV、模拟和审计报告。variant ID 是稳定身份；批次文本摘要、元数据摘要和 manifest 根哈希必须同步变化。legacy source SHA 继续冻结，只用于 archive/review 的审计 lineage。

### 2.2 发布拓扑与当前集成状态

| 层 | 最终发布常量 | 本审计基线状态 |
| --- | ---: | --- |
| immutable source | 75,375 条无表头物理数据行 | 已验证，双副本 SHA-256 相同 |
| archive | 75,375 条 disposition 记录 | 已集成，当前计数精确匹配 |
| authored runtime | 30,000 条 | 当前发布运行时，`source_kind=curated_authored` |
| legacy runtime surfaces | 0 条 | `persona-surface-manifest.tsv` 仅保留表头 |
| semantic scenes | 1,190 个 | 已按唯一 `semantic_group` 聚合 |
| authorship ledger | 30,000 条 | 逐行绑定 batch、variant、摘要和根哈希 |

这里的 30,000、0、1,190 和 30,000 ledger 是当前精确发布验收值；任一计数不符都阻止发布。发布哈希必须从目标提交上的隔离重建与模拟重放取得。

## 3. v2 的 21 个字段

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
| 17 | `relationship_profile` | `neutral`、`warm_friend`、`playful_friend` 或 `nickname_easter_egg` |
| 18 | `text` | 不含制表符/真实换行的完整独立句子 |
| 19 | `source_kind` | 当前运行时必须为 `curated_authored` |
| 20 | `source_reference` | `catalog:authored-v1:<batch>;variant:<variant_id>` |
| 21 | `rewrite_reason` | authored editorial role |

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
  --authored-dir data/authored/v1 `
  --authorship-manifest config/persona-authorship-manifest.json `
  --output data/optimized/persona-corpus-v2.tsv `
  --seed 20260722 `
  --pii-policy review
```

authored builder 同时生成 v2、archive、review、`reports/pii-review.tsv`、空 surface manifest 与 30,000 行 authorship ledger。相同 authored batches、manifest、contract、构建代码和种子必须产生全部六个字节一致的输出。

## 7. 严格校验

```powershell
python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json
```

v1.3.0 合格输出必须精确为 `Validation: 0 hard errors, 0 warnings`。白名单只允许精确匹配并附理由，不能用宽泛正则掩盖新问题。

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

最终 authored runtime 的播放暴露率门禁是：Easter egg 8%–12%、seasoning 0.5%–1.5%、dry-sharp 0%–4%。三者都是模拟播放次数占比，不是 TSV 行数占比；dry-sharp 另有 0.6%–0.8% 的场景库存门禁。

## 9. 选择器验证与调用界面

选择器是库 API，不提供会修改仓库的独立 CLI。完整选择器合同用以下命令验证：

```powershell
python -m unittest tests.test_selector -v
```

运行时入口是 `src.persona_corpus.selector.select_line(corpus, context, history, now, seed, scheduler_config=...)`。`now` 必须带时区；失败、不可信上下文或无合格候选时返回 `None`，且不追加历史。

## 10. 十二阶段选择约束

最终运行时采用 scene-first：先按 `semantic_group` 聚合场景，过滤触发器、必需上下文、语义冷却、每日上限、相邻/窗口配额、最小间隔、滚动小时预算与夜间预算，再按组缺口、模式缺口、场景权重和打扰成本选择场景；只有场景确定后，才在该场景内选择通过 ID 冷却与 surface 暴露约束的变体。场景权重不得随变体数量增长，fallback 也不得先在全库挑行再倒推场景。加权随机只在最高分带内进行，并使用局部 `random.Random(seed)`，不会污染全局随机状态。

## 11. 上下文与未来信号

当前可信信号来自本地时区日期、周末、时段、季节、应用启动、节日、纪念日、月边界和长静默。`ide_foreground`、`active_minutes` 与 `idle_return` 仍是未来信号：没有可靠采集时必须为 `None`。`fullscreen` 已由桌面运行时采集；自动台词的四个精确间隔窗口为：本地时间 `06:00–17:59:59` 使用 `5–15` 分钟，`18:00–22:59:59` 使用 `10–20` 分钟，`23:00–05:59:59` 使用 `30–60` 分钟，明确全屏时覆盖时段并使用 `60–120` 分钟；上下界均可取到。

全屏探测只读取台前 HWND 的有效性、可见/最小化状态和样式，DWM cloaked 状态与扩展边框几何，以及相交显示器的完整边界。它不读取窗口标题、进程名称或内容、键鼠输入、剪贴板、用户文件、屏幕像素或网络数据。探测失败或台前 HWND 在采样期间变化时，原始信号保持 `None`，不会伪造受控 token；运行时的有效安静模式只保留最近一次明确观测，直到后续明确结果更新。

## 12. 历史与确定性

选择历史保存 `selected_id`、播放时间、分类、分类组、语义组、输出模式、触发器和打扰成本。冷却与预算均从该历史重算；排序稳定且固定种子可复现。历史时间倒退、未知结构或无时区时间会安全失败。

## 13. PII 与人物边界

真实姓名、地区迁徙、收入、打零工经历、亲昵称呼、未经授权的关系或身份暗示默认不进入启用集。身份彩蛋是唯一例外：必须在 `config/persona-editorial-manifest.json` 中按稳定 ID 精确批准，并同时绑定来源、允许的 identity marker、文本 SHA-256、分类/分组、冷却、每日上限与权重；宽泛 marker 命中、EXE 原始字节扫描或“看起来像彩蛋”都不构成批准。自动规则只负责发现、精确核对与隔离，不能替代人物授权、虚构身份和关系边界的人工判断。

## 14. archive、review 与人工审批

明确不适合独立播报、要求回应或违反边界的源行进入 archive；PII、亲密度、上下文或语气不确定的内容进入 review；`reports/pii-review.tsv` 和 `reports/corpus-manual-review.md` 保存人工复核证据。安全 legacy surface 还必须在 `persona-surface-manifest.tsv` 中精确绑定 line ID、variant、源行、category/group/topic、source reference、原始文本摘要和源摘要。任何放行都必须可追溯到具体来源与复核理由；archive/source 的存在本身不等于运行时批准。

## 15. 扩展规则

新增内容必须是完整句子，并先加入明确的主题/语义组；禁止新增 opener/core/closer 组合数组。填写全部 20 列，保持 ID 稳定、`requires_reply=false`、无问号、无规范化重复、无制表符或真实换行，并给出 `source_reference` 与 `rewrite_reason`。修改后必须重建、校验、模拟并运行 Python/.NET 全套测试。

## 16. WPF 集成

`CompanionDesktopPet.csproj` 只把当前 `data/optimized/persona-corpus-v2.tsv` 以逻辑名 `CompanionDesktopPet.Assets.persona-corpus-v2.tsv` 嵌入应用。WPF 严格解析 21 列表头，加载 30,000 条 authored 行，并把场景/变体选择、语义冷却、每日上限、组配额、关系画像配额、seasoning/dry 暴露和打扰成本交给离线运行时选择器。

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

此外，最终门禁必须断言 30,000 authored runtime rows、0 legacy surfaces、1,190 scenes、30,000 ledger 和 75,375 archive dispositions；模拟报告必须从最终输入重新生成，不能复用旧 contract 下的结果。

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

验证器要求交付目录只有一个自包含 EXE（可另有 `.txt` 说明），拒绝旁置或外部应用 DLL、PDB、JSON 等 sidecar，并核对交付 EXE 与 publish EXE 的 SHA-256。这里的“单 EXE”是交付边界，不表示 Windows 进程绝不加载 DLL；应用仍会使用操作系统提供的系统 DLL 与系统组件。身份彩蛋只有在精确列入 editorial manifest 后才可进入运行时语料；安全边界由应用启动时对 `PersonaCorpus` exact editorial manifest 的自校验、Python validator 和程序集测试共同承担，不再使用无法区分已批准内容与泄露的 EXE 原始字节 marker 扫描。验证器把 EXE 单独复制到 `outputs/verify/` 后以 `--smoke-test` 启动；应用必须在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束。超时、需要强杀或非零退出均失败，强杀只用于清理残留 PID。

## 19. 桌宠交互与隐私

保留左键点击爱心、拖拽时倾斜和松手落地回弹；自然闭眼图层叠加式眨眼不压缩整张人物图。启动后会在本地显示一次“嗨♡”，右键菜单也提供 `打个招呼♡`；这两种打招呼都只使用固定的本地 UI，不从语料生成。`暂停动画` 只暂停并复位待机动作、自动眨眼和问候，点击爱心、拖动/落地和台词仍可使用。仍不提供 wink 或假手挥手，没有旧 `GetGreeting`，也没有语料驱动的 `AnimationCue`/`PlayAmbientGesture`。窗口先显示，记忆、语料和场景在后台异步预热；预热期间启动/点击使用不读取个人信息的固定本地 fallback，自动触发保持静默，完整烟测必须等到真实语料回复。桌宠完全离线；除上一节公开的只读几何全屏探测外，不读取键盘输入内容、剪贴板、窗口标题、进程名称/内容、用户文件名、用户目录内容或屏幕像素，也不使用网络。正常运行时，角色偏好、冷却历史和剧情状态保存在 `%LOCALAPPDATA%\CompanionDesktopPet`；只有用户主动启用开机自启动时，才会另在当前用户 Run 注册表项保存桌宠自身 EXE 路径；`--smoke-test` 使用并清理系统临时目录中的隔离状态。身份彩蛋仅可按精确 editorial manifest 审批进入 `PersonaCorpus`；应用启动自校验该 exact manifest，并由 Python validator 与程序集测试阻止未审批内容进入运行时。

## 20. 限制与发布政策

调度只能使用已实现且可证明的信号，不理解屏幕内容或用户情绪；固定模拟不能证明所有真实时间序列，但会阻止已知硬约束回归。可分离的技术代码按 PolyForm Noncommercial 1.0.0 提供，属于 source-available 而不是 OSI 开源；人物形象、角色身份、人格口吻、语料、语义树/森林及编辑性编排不随代码授权。完整边界见 [LICENSE-SCOPE.md](LICENSE-SCOPE.md) 与 [ASSET_AND_PERSONA_RIGHTS.md](ASSET_AND_PERSONA_RIGHTS.md)。

## 确定性重建哈希门禁

发布前必须在隔离临时目录重建并逐字节比较 v2、archive、review、PII review、空 surface manifest 与 authorship ledger；不要原位覆盖 canonical 数据来“证明”可复现。

| 发布对象 | SHA-256 |
| --- | --- |
| `src/CompanionDesktopPet/Assets/persona-corpus.tsv` | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| `data/source/persona-corpus.original.tsv` | `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` |
| expanded `data/optimized/persona-corpus-v2.tsv` | `1d887627e6b4a8f303a0151cea8b99726d176b9953a396782e88cde69de5633c` |
| `data/optimized/persona-corpus-archive.tsv` | `a9e78adefbeff30e44f88e7eae1a953a8e76c499f29d58d1ec334eb6f75e03bc` |
| `data/optimized/persona-corpus-review.tsv` | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| `reports/pii-review.tsv` | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |
| `data/optimized/persona-surface-manifest.tsv` | `a2353ed6480ec1e75c40add4ae36ed7884724ca88a64b25f8b1c9282f975037c` |
| `config/persona-authorship-manifest.json` | `4742a984077ce044adf8f059ee46dd36bfd7f4a0248c3422060bd366027cc4b6` |
| `data/optimized/persona-authorship-ledger.tsv` | `31305579ebf55d2d49c3227d7c7664b16e89abd0e9aab3cbbfa11ae3e0cace8d` |
| `reports/simulation-report.md` | `c55beb9155042acf326deef7a6b57f85913704d35dd9c02c4ccc5036df10eb3e` |
| `reports/simulation-events.json` | `4e4b66d7eeac6dd01dd70e117e97b67a75f52ff2cf1c21f6fa34920e0e105493` |
| 当前 `v1.2.1` 的 `outputs/CompanionDesktopPet/佳怡桌宠.exe` | `7d5343c01e1ed89ef15e3d9595f6c9fb1ec24f8275db15628a5b541ad5c1ff03` |
| 历史 `v1.0.0` 的 `outputs/CompanionDesktopPet/佳怡桌宠.exe` | `b79bf57a94d63387b6d8db288e53f64b06af32a3aa4881e7c069634839442a82` |

当前 EXE 是 `v1.2.1` 实证：annotated tag 指向提交 `421b54a349062ab540b27bfe6f9a97ba7df5b6f2`，使用 .NET SDK `9.0.301` 构建，`ProductVersion=1.2.1+421b54a349062ab540b27bfe6f9a97ba7df5b6f2`，大小为 `80,454,500` 字节。云端 publish、delivery、isolated、直接 Release 资产、ZIP 内 EXE、经 `127.0.0.1:7890` 回下载后的仓库交付副本与任务交付副本 SHA-256 全部等于上表值；云端 smoke PID `8556` 与最终本地复核 smoke PID `46072` 均自行以退出码 0 结束。历史 v1.0.0/v1.1.0 的独立证据仍保留在发布清单中。

发布表中任何哈希占位都必须先清零；v1.2.1 的 EXE、资产与校验和来自目标标签流水线和代理回下载实证，没有预填或沿用旧值。完整 8 项资产哈希、ZIP 清单、签名状态和 CI 链接见发布与清理清单。
