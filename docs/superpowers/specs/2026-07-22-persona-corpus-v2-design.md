# Persona Corpus v2 设计

日期：2026-07-22  
状态：已由用户提供的重构规范批准

## 目标

把当前桌宠使用的 75,375 行组合展开语料迁移为一个完全离线、不可读心、不要求用户回应、可追溯且可验证的单向角色播报系统。目标不是保留原行数，而是产出约 800～1,200 条可独立播放的高质量 v2 语料，并提供纯 Python 标准库的审计、构建、选择、模拟和验证工具。

## 仓库适配决策

- 仓库没有根目录 `persona-corpus.tsv`；现有权威原库位于 `src/CompanionDesktopPet/Assets/persona-corpus.tsv`。此文件作为不可变输入，复制到 `data/source/persona-corpus.original.tsv`，两者记录并复核 SHA-256。
- 不修改或删除原库。所有中间产物、v2、归档、复核清单和报告使用新目录。
- Python 代码只使用 3.11 标准库，构建与运行均不联网、不调用模型。
- WPF 桌宠最终嵌入 `data/optimized/persona-corpus-v2.tsv`，只消费 `enabled=true` 文案；旧库保留用于审计与追溯，不再作为运行语料。
- 当前 C# 状态/场景/记忆系统继续负责角色连续性；v2 元数据成为内容选择约束的权威来源，至少保证问题、虚假上下文、冷却和分组约束不被旧随机选择绕过。

## 方案选择

### 方案 A：原地清洗

直接改写现有 75,375 行。优点是路径不变；缺点是违反原文件保护、不可追溯且仍容易保留组合噪声，因此不采用。

### 方案 B：为原库补元数据

保留全部行并逐行分类。优点是数据量不下降；缺点是把机械组合误当有效表达，运行体验不会根治，因此不采用。

### 方案 C：不可变迁移到 v2（采用）

保留并审计原库，恢复 prefix/core/suffix 结构；从主题中少量、完整地改写独立句子，问题与风险内容分别归档或复核。通过 v2 schema、离线 selector、模拟器与验证器把体验规则变成可执行门禁。

## 数据架构

### Source

- `src/CompanionDesktopPet/Assets/persona-corpus.tsv`：权威原库，只读。
- `data/source/persona-corpus.original.tsv`：字节级副本。

### Intermediate

- `extracted-prefixes.tsv`
- `extracted-topics.tsv`
- `extracted-suffixes.tsv`
- `source-line-map.tsv`

抽取器通过同分类频次、共同前后缀、组合重复次数和置信度恢复结构。EasterEgg 默认 standalone；低置信度内容不强拆。

### Optimized

- `persona-corpus-v2.tsv`：带固定 20 列表头的运行语料。
- `persona-corpus-archive.tsv`：每条未启用原文和原因。
- `persona-corpus-review.tsv`：需要人工判断的风险内容。

每条 v2 文案是完整独立文本，不在构建时或运行时拼接 prefix/core/suffix。ID 由稳定的主题/变体标识生成；每条记录包含语义组、输出模式、触发、上下文、冷却、权重、来源和改写原因。

## 内容策略

- enabled 文案不含中英文问号，`requires_reply=false`。
- 不断言未知的用户状态；技术内容改为角色自己的经验或通用观察。
- ProactiveChat 原文全部归档，仅其少量非问句改写可进入 v2。
- 技术主题通常保留 1～2 个完整变体，生活/爱好/回忆主题保留 3～5 个情境不同的变体。
- self_talk 与 ambient 是主输出；user_direct 只保留无需回应、不过界的表达。
- 疑似真实姓名、地点、收入和工作经历默认进入 PII review，未经人工确认不启用。
- 口癖只作为少量角色辨识度，不由生成器机械添加。

## Python 组件

- `models.py`：v2 行、上下文、历史条目和选择结果数据模型。
- `loader.py`：严格 TSV 读取、枚举和列校验，错误包含行号。
- `context.py`：当前 MVP 的 trigger 与 required_context 匹配。
- `history.py`：历史 JSON 往返、单条/语义/每日/频率统计。
- `selector.py`：按触发、上下文、冷却、每日上限、近期重复、打扰预算、分组配额和权重依序过滤与评分；无合法候选返回 `None`。

## 调度不变量

- 相邻主动文本至少 8 分钟；普通每小时最多 2 条，深夜最多 1 条。
- 不连续 technical、daily_care 或 emotional_reflection。
- 最近 5 次 technical 不超过 2，最近 10 次 user_direct 不超过 2，最近 50 次 EasterEgg 不超过 1。
- 冷却内不重复 ID 或 semantic_group；required_context 未满足时不得播放。
- 配置权重和为 1.0，technical 介于 0.10～0.20，character_life 最高，easter_egg 不超过 0.02。

## 构建和审计

1. 复制并哈希原库。
2. `audit_corpus.py` 产生 before 指标、代表例子和行号。
3. `extract_corpus_structure.py` 生成中间结构和来源映射。
4. `build_corpus_v2.py` 固定种子生成 v2/archive/review；内容来自显式完整句库和可追溯的主题级改写，不做全排列。
5. `validate_corpus_v2.py` 对 schema、枚举、重复、问题、虚假上下文、PII、冷却、长度、口癖和调度配置做硬校验。
6. `simulate_persona.py` 以至少 10 个固定 seed 模拟 30 天，并验证全部调度不变量。

## 测试策略

- Python 使用 `unittest`，覆盖附件列出的 22 类行为与边界。
- 验证器作为独立非零退出门禁。
- 构建重复执行后比较 v2 SHA-256，证明固定 seed 可复现。
- 模拟报告逐 seed 记录异常；任何硬约束异常导致验证失败。
- .NET 测试增加 v2 资源、只启用安全文案、无问号、无运行时拼接和 C# 选择约束测试。
- 发布后检查目录只有单个 EXE、无旁置 DLL，并做进程启动烟测。

## 报告与人工边界

生成 before、after、rewrite、manual-review、simulation 和 PII 报告。自动化不能判断“雷琳玥”、湖南/广东、工资与打零工经历是否为完全虚构且获准公开，因此默认禁用并列为人工确认项；这不是静默删除。

## 完成条件

只有在原库哈希复核不变、Python 全测通过、验证器成功、30 天 10-seed 模拟无硬约束违规、.NET 全测通过、C# 嵌入 v2、单 EXE 重新发布并通过无 DLL 启动烟测后，任务才算完成。
