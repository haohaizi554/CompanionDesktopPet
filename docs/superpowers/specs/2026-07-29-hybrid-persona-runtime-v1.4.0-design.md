# v1.4.0 Hybrid Persona Runtime Design

日期：2026-07-29
状态：设计已确认，待实施计划
目标版本：`v1.4.0`

## 1. 目标

v1.4.0 将 v1.3.0 的 30,000 条静态 authored 语料与 v1.2.1 已审计的 52,132 条运行时语料合并为一个可确定性重建、可追溯、离线、单 EXE 的混合运行时。合并后 authored 保持主要人格来源，legacy 内容作为扩展库贡献约三成实际播放，不能因库存更大而支配调度。

精确发布库存为：

- `curated_authored=30,000`
- `legacy_surface_variant=51,326`
- legacy curated core `=806`
- runtime total `=82,132`
- unique normalized text `=82,132`
- unique stable ID `=82,132`
- semantic scenes `=1,723`
- authorship ledger `=30,000`
- surface manifest `=51,326`
- immutable legacy source dispositions `=75,375`

## 2. 研究结论

冻结源 `persona-corpus.tsv` 的 75,375 条物理数据行不是 75,375 条可直接播放的新内容。当前 archive 分类为：58,690 条 `cartesian_duplicate`、8,580 条 `requires_user_reply`、4,160 条 `overly_commanding`、1,574 条 `fake_context`、1,248 条 `privacy_risk`、551 条 `low_information`、338 条 `manual_review` 和 234 条 `unsafe_emotional_claim`；当前 authored-only 构建对全部源行标记 `can_recover=false`。

v1.2.1 已通过独立的安全筛选和 manifest，从冻结源确定性物化 51,326 条 legacy surface，并与 806 条旧 curated 内容组成 52,132 条运行时。该运行时与 v1.3.0 的 30,000 条 authored 在规范化文本和稳定 ID 上均无重合；两套场景命名空间也无重合，因此集合并集精确为 82,132 条、1,723 scenes。

旧运行时不能原样拼接。旧集有 22/533 个 dry-sharp scenes（约 4.13%），新集有 8/1,190 个（约 0.67%）；原样合并会得到 30/1,723（约 1.74%），违反当前 0.6%–0.8% 场景库存门禁。当前 contract 还把运行时上限固定为 60,000，并要求 legacy surface 为 0；这些契约必须显式版本化。

## 3. 非目标

- 不把剩余约 24,000 条未进入 v1.2.1 runtime 的高风险或低质量源行直接启用。
- 不从 v1.2.1 Release 二进制或历史 TSV 复制运行时数据。
- 不放宽问句、回复钩子、PII、假上下文、强迫/依赖、身份或控制字符规则。
- 不引入联网模型、数据库、遥测、热更新、TTS、Live2D 或新的用户数据采集。
- 不让行数、surface 数量或场景数量直接决定播放概率。

## 4. 构建架构

### 4.1 输入

一次 release build 同时加载：

1. `data/authored/v1/b001-b100.tsv` 与 authorship manifest；
2. 冻结的 75,375 行 legacy source 与 source-line mapping；
3. 806 条旧 curated catalog；
4. legacy surface 安全筛选策略，以及 v1.2.1 的精确 manifest 作为重建比较基线；
5. authored 与 legacy 两个版本化身份审批集合；
6. v1.4.0 persona contract 与 scheduler contract。

历史 tag 只用于回归比较，不是构建输入。当前仓库必须包含重建所需的所有版本化证据。

### 4.2 Builder profile

builder 增加显式 `hybrid` runtime profile，禁止通过 `authored is None` 等隐式分支决定发布拓扑。profile 行为为：

1. 一对一物化 30,000 authored rows；
2. 物化 806 curated catalog rows；
3. 从冻结 archive basis 重新运行 legacy safety filter；
4. 精确物化 51,326 legacy surfaces；
5. 合并后检查 ID、规范化文本、source reference 和 scene signature；
6. 执行全局 dry-sharp 再标定；
7. 生成新的 surface manifest，并与 v1.2.1 比较基线逐行核对稳定 ID、来源与文本哈希；
8. 以稳定 seed/ID 排序并生成全部派生产物。

`authored-only` 与 `legacy-only` profile 仅保留为回归测试入口，不得被 v1.4.0 release workflow 使用。CLI 和 CI 必须显式传入 `--runtime-profile hybrid`。

### 4.3 输出

构建输出包括：

- 82,132 行 `persona-corpus-v2.tsv`
- 75,375 行 archive
- review 与 PII review
- 51,326 行 surface manifest
- 30,000 行 authorship ledger
- 混合库存报告、模拟 events/report 和哈希登记

同一输入连续两次隔离构建时，上述输出必须逐字节一致。

## 5. 契约模型

`release_inventory` 拆分为明确字段：

- `authored_runtime_rows=30000`
- `legacy_curated_rows=806`
- `legacy_surface_rows=51326`
- `expanded_runtime_rows=82132`
- `semantic_scene_count=1723`

`inventory.expanded_runtime` 上限提高到至少 90,000，下限不得被用来区分 authored-only 与 hybrid。生成的 C# 常量分别暴露五个精确值；`ExpandedRuntimeRows` 不再错误地等同于 authored count。schema、Python loader、generator、C# parser、validator、reports 和 tests 必须引用同一 contract，不得重复硬编码另一套数字。

## 6. 来源层级

`source_tier` 是从 `source_kind` 确定性派生的场景属性，不增加 TSV 列：

- `curated_authored` -> `authored`
- 其余四类旧 curated source kind 与 `legacy_surface_variant` -> `legacy`

同一 `semantic_group` 的全部 variants 必须具有同一 source tier；混层 scene 是硬错误。`SceneDefinition`、Python scene model 和历史项暴露受控 tier，序列化快照记录 tier。

v1.3.0 历史快照没有该字段。迁移时缺失 tier 默认 `authored`，因为 v1.3.0 实际只包含 authored；未知非空 tier、结构损坏或不一致历史安全失败。

## 7. 来源层级播放策略

来源配额在所有现有硬过滤之后、场景最终评分之前执行。它不替代 trigger、required context、ID/semantic cooldown、每日上限、关系画像、静默、小时/夜间预算或安全过滤。

最近 100 次历史是来源窗口。目标 legacy 比例为 30%，发布接受区间为 25%–35%。算法在 Python 与 C# 使用同一整数规则：

- 历史少于 20 次时不启用硬上下界，只按距 30% 目标的整数 deficit 给来源 tier 加分；
- 历史达到 20 次后，对每个候选计算“加入候选并移除窗口外最旧项”后的 hypothetical ratio；
- 若 legacy 低于 25% 且存在安全合格 legacy scene，优先 legacy；
- 若加入 legacy 会超过 35% 且存在安全合格 authored scene，屏蔽 legacy；
- 在 25%–35% 内只使用小幅 deficit score，不覆盖原有 category/mode/scene 评分；
- 若目标 tier 没有安全合格 scene，可退回另一 tier，宁可短暂偏离来源比例，也不能绕过硬安全规则或无故静默。

release simulation 要求总 legacy 播放率 25%–35%，每个 seed 为 20%–40%，并记录所有 tier fallback。自然场景必须保持 1,500/1,500 outputs 与零 hard violations。

## 8. 关系画像与身份边界

所有 legacy rows 迁移为 `relationship_profile=neutral`，不消耗 authored 的 warm/nickname 配额。authored 继续执行：

- warm_friend 最近 20 次最多 2 次；
- nickname_easter_egg 最近 100 次最多 1 次。

身份审批分为两个版本化 exact sets：

- authored v1 identity rules，绑定 authored variant、batch、文本哈希和 marker；
- legacy v1.2.1 identity rules，绑定旧稳定 ID、source line/reference、文本哈希和 marker。

构建时将两套规则合并为无冲突的 exact runtime set。重复 ID、重复文本、marker 集不一致、来源哈希漂移、规则缺失、额外身份行或宽泛 marker 命中全部是硬错误。非身份 PII 继续禁用。

## 9. Dry-sharp 与 seasoning 再标定

authored 源中明确标记的 8 个 dry-sharp scenes 保持不变，以维持 authorship metadata 一一绑定。legacy tier 的历史 dry-sharp 先降为基础 `dry`，再从符合 category、trigger、context 和 tone 条件的 legacy dry scenes 中按稳定 scene hash 排序，补足全局目标场景数。

对 1,723 scenes，目标 0.7% 对应 12 个 dry-sharp scenes；允许区间 0.6%–0.8% 对应 11–13 个。实现选择精确 12 个，不依赖浮点阈值碰运气。任何 authored dry-sharp 数超过全局上限或合格 legacy scenes 不足以补齐目标都阻止构建。

seasoning 播放接受区间保持 0.5%–1.5%，recent-20 maximum 保持 1。legacy inventory 仍为 observation-only，但来源权重、场景选择和 recent gate 必须让实际播放通过门禁；不得为了合并而放宽当前人格质量标准。

## 10. 错误处理与可观测证据

以下情况直接失败，不生成可发布 EXE：

- 五项 release inventory 任一不匹配；
- stable ID、规范化文本、source reference 或 manifest 重复/漂移；
- scene 内 tier、tone 或调度 metadata 不一致；
- identity exact set 不闭合；
- dry-sharp 无法精确分配 12 scenes；
- validator 有 hard error 或 warning；
- simulation 任一 hard violation、输出缺失或 tier 比例越界；
- Python/C# 选择器对固定输入、history 和 seed 产生不同 tier/scene 决策；
- embedded resource 数量、哈希或运行时解析与 canonical TSV 不一致。

报告必须分别展示 authored、legacy curated、legacy surface、tier playback、tier fallback、关系画像、dry-sharp、seasoning、Easter egg 和来源追溯，不得把 75,375 source 宣称为 75,375 条启用语料。

## 11. 测试策略

所有行为变更遵循 TDD：先加入最小失败测试并记录预期失败，再实现最少代码通过。

测试层次：

1. contract/schema/generator：五项库存、90k 容量、source-tier 规则；
2. builder：精确 82,132、两类 manifest、零重复、两次重建一致；
3. safety/lineage：旧筛选集合精确 51,326，禁止剩余 archive 行进入 runtime；
4. Python selector：warm-up、25% 下限、35% 上限、100 窗口滚动、tier fallback；
5. C# runtime：parser、scene tier consistency、history migration、与 Python 等价；
6. dry-sharp：保留 8 authored scenes、legacy 再标定后全局精确 12；
7. simulation：30 days x 10 seeds、1,500/1,500、tier 比例和既有暴露门禁；
8. performance：异步预热不阻塞 UI，82,132 行加载、900-click retained memory、启动预算不回退；
9. full gates：Python 全量、.NET Release 全量、generator `--check`、CI evidence contracts、validator、单 EXE WPF smoke。

## 12. 发布与文档

发布使用新 annotated tag `v1.4.0`，不移动 `v1.3.0` 或更早标签。GitHub Release 标题严格为 `v1.4.0`。正文使用具体中文内容，至少列出：

- 82,132 的精确组成；
- 1,723 scenes、30,000 ledger、51,326 surface manifest；
- authored 优先与 legacy 25%–35% 播放策略；
- dry-sharp、seasoning、关系画像和身份边界；
- 实际 Python/.NET/validator/simulation 结果；
- EXE/ZIP 字节数、SHA-256、ProductVersion 和 source commit；
- 离线、单 EXE、NotSigned 和 SmartScreen 提示。

GitHub CLI 与 git 网络操作使用 `http://127.0.0.1:7890`。tag workflow 生成并上传八项资产；发布后通过同一代理回下载，逐项复核 SHA256SUMS、ZIP 内部清单和 EXE smoke。README、persona corpus 文档、发布清单、审计报告与任务面板同步更新。

最终步骤执行 `git worktree prune` 并核对工作树注册表与同级目录，只保留当前仓库；删除本轮明确的 release/download/evidence scratch，保留正式输出。

## 13. 验收标准

v1.4.0 只有同时满足以下条件才算完成：

1. canonical runtime 精确 82,132 行、1,723 scenes，全部唯一且可追溯；
2. authored/legacy curated/legacy surface 精确为 30,000/806/51,326；
3. validator 为 `0 hard errors / 0 warnings`；
4. simulation 为 1,500/1,500 outputs、0 hard violations，legacy aggregate 25%–35%；
5. 所有旧安全边界、关系画像和暴露门禁通过；
6. Python、.NET、CI、performance、publish verifier 和真实 WPF smoke 全部通过；
7. Release 标题只有 `v1.4.0`，中文正文包含实际结果而非通用描述；
8. 八项资产、哈希、官方 EXE 回下载副本和仓库/任务输出副本一致；
9. `origin/main`、tag source commit、文档证据和任务面板关系清晰；
10. 最终只剩一个干净工作树。
