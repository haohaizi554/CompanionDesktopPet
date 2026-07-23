# Persona Corpus v2 维护与验证说明

本文说明佳怡桌宠离线角色语料的来源、20 列契约、确定性流水线、运行时选择规则与发布边界。所有命令均从仓库根目录运行；流水线只使用 Python 标准库，不调用网络、模型 API 或数据库。

## 1. 为什么 75,375 行不是 75,375 条独立内容

不可变源证据位于 `src/CompanionDesktopPet/Assets/persona-corpus.tsv`，共 75,375 行。基线审计发现大量共享开头、主题句和结尾：例如多个技术分类各有 3,900 行，相同长结尾各重复 3,600 次，且有 455,894 对有界近重复候选。这些行主要是前缀 × 主题 × 后缀的笛卡尔组合，不等于 75,375 个独立写作意图。

v2 不在构建时或运行时重新拼接片段，而是提供 800 条能独立播放的完整句子；原始 75,375 行仍逐行保存在归档、复核和来源映射中。

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

该命令同时更新 v2、archive、review 与 `reports/pii-review.tsv`。同一源、映射、代码和种子必须产生字节一致的 v2。

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

## 9. 选择器验证与调用界面

选择器是库 API，不提供会修改仓库的独立 CLI。完整选择器合同用以下命令验证：

```powershell
python -m unittest tests.test_selector -v
```

运行时入口是 `src.persona_corpus.selector.select_line(corpus, context, history, now, seed, scheduler_config=...)`。`now` 必须带时区；失败、不可信上下文或无合格候选时返回 `None`，且不追加历史。

## 10. 十二阶段选择约束

选择先过滤安全启用行、触发器、必需上下文、ID 冷却、语义冷却、每日上限、相邻/窗口配额、最小间隔、滚动小时预算与夜间预算，再按组缺口、模式缺口、权重和打扰成本评分。加权随机只在最高分带内进行，并使用局部 `random.Random(seed)`，不会污染全局随机状态。

## 11. 上下文与未来信号

当前可信信号来自本地时区日期、周末、时段、季节、应用启动、节日、纪念日、月边界和长静默。`ide_foreground`、`active_minutes`、`idle_return`、`fullscreen` 是未来信号：没有可靠采集时必须为 `None`，不能根据窗口标题、文件名、输入内容或主观推断伪造上下文。只有明确为真/假时才生成对应受控 token。

## 12. 历史与确定性

选择历史保存 `selected_id`、播放时间、分类、分类组、语义组、输出模式、触发器和打扰成本。冷却与预算均从该历史重算；排序稳定且固定种子可复现。历史时间倒退、未知结构或无时区时间会安全失败。

## 13. PII 与人物边界

真实姓名、地区迁徙、收入、打零工经历、亲昵称呼、未经授权的关系或身份暗示默认不进入启用集。自动规则只负责发现与隔离，不能替代人物授权、虚构身份和关系边界的人工判断。

## 14. archive、review 与人工审批

明确不适合独立播报、要求回应或违反边界的源行进入 archive；PII、亲密度、上下文或语气不确定的内容进入 review；`reports/pii-review.tsv` 和 `reports/corpus-manual-review.md` 保存人工复核证据。任何放行都必须可追溯到具体来源与复核理由。

## 15. 扩展规则

新增内容必须是完整句子，并先加入明确的主题/语义组；禁止新增 opener/core/closer 组合数组。填写全部 20 列，保持 ID 稳定、`requires_reply=false`、无问号、无规范化重复、无制表符或真实换行，并给出 `source_reference` 与 `rewrite_reason`。修改后必须重建、校验、模拟并运行 Python/.NET 全套测试。

## 16. WPF 集成

`CompanionDesktopPet.csproj` 只把 `data/optimized/persona-corpus-v2.tsv` 以逻辑名 `CompanionDesktopPet.Assets.persona-corpus-v2.tsv` 嵌入应用。旧的 `src/CompanionDesktopPet/Assets/persona-corpus.tsv` 仅是源证据，不重新嵌入运行时。WPF 按字段名解析精确表头，只加载安全启用行，并把语义冷却、每日上限、组配额和打扰成本交给离线运行时选择器。

## 17. 完整测试门禁

```powershell
python -m unittest discover -s tests -v
dotnet restore CompanionDesktopPet.sln -r win-x64
dotnet test CompanionDesktopPet.sln -c Release --no-restore
```

任何失败都阻止交付。

## 18. 单文件发布与隔离烟测

```powershell
Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj `
  -c Release -r win-x64 --self-contained true --no-restore -o publish
Copy-Item publish/CompanionDesktopPet.exe outputs/CompanionDesktopPet/佳怡桌宠.exe -Force
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 `
  -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe `
  -PublishExePath publish/CompanionDesktopPet.exe
```

验证器要求交付目录只有一个 EXE（可另有 `.txt` 说明），拒绝 DLL/PDB/JSON 等 sidecar，核对交付 EXE 与 publish EXE 的 SHA-256，并扫描最终 EXE 原始字节中的 UTF-8、UTF-16LE 与 UTF-16BE 直接身份标记。地区、收入等通用词可能合法存在于自包含 .NET/ICU 词典中，因此由应用程序集测试和 v2 语料门禁检查，避免扫描整个运行时包时误报。验证器把 EXE 单独复制到 `outputs/verify/` 后以 `--smoke-test` 启动；应用必须在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束。超时、需要强杀或非零退出均失败，强杀只用于清理残留 PID。

## 19. 桌宠交互与隐私

保留左键点击爱心、拖拽时倾斜和松手落地回弹；自然闭眼图层叠加式眨眼不压缩整张人物图。启动后会在本地显示一次“嗨♡”，右键菜单也提供 `打个招呼♡`；这两种打招呼都只使用固定的本地 UI，不从语料生成。仍不提供 wink 或假手挥手，没有旧 `GetGreeting`，也没有语料驱动的 `AnimationCue`/`PlayAmbientGesture`。桌宠完全离线，不读取键盘输入内容、剪贴板、文件名或窗口标题；本机状态写入 `%LOCALAPPDATA%\CompanionDesktopPet`。具体 PII marker 不进入运行时程序集，语料构建/测试门禁和最终 EXE 原始字节扫描负责阻止其进入交付物。

## 20. 限制与发布政策

调度只能使用已实现且可证明的信号，不理解屏幕内容或用户情绪；固定模拟不能证明所有真实时间序列，但会阻止已知硬约束回归。仓库未声明开源许可证；在人物素材、角色内容和再分发权利明确前，不得公开再发布素材、语料或构建产物。

## 确定性重建哈希门禁

发布前先记录 v2 哈希，使用固定种子重建，再比较：

```powershell
$before = (Get-FileHash data/optimized/persona-corpus-v2.tsv -Algorithm SHA256).Hash
python tools/build_corpus_v2.py `
  --input src/CompanionDesktopPet/Assets/persona-corpus.tsv `
  --mappings data/intermediate/source-line-map.tsv `
  --output data/optimized/persona-corpus-v2.tsv `
  --seed 20260722 `
  --pii-policy review
$after = (Get-FileHash data/optimized/persona-corpus-v2.tsv -Algorithm SHA256).Hash
if ($before -ne $after) { throw 'Persona Corpus v2 is not reproducible.' }
```
