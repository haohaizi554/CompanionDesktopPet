# 2026-07-25 全面审查与修复记录

本文逐项复核外部审查清单。结论按当前 `main` 代码和可执行路径判断，不把旧行号、测试名或历史实现直接当成现状。

## 结论口径

- **已修复（本轮）**：复现了当前缺口，先建立失败回归，再修改生产代码。
- **此前已修复**：审查描述对应旧版本；当前代码、测试或生成契约已经覆盖。
- **不成立**：当前生产调用链不存在所述风险，盲目加锁或改变行为反而会扩大状态空间。
- **接受的工程债**：确有可维护性成本，但不构成当前发布阻断。

## P0 复核

| 审查项 | 结论 | 当前证据/处理 |
| --- | --- | --- |
| `DialogueService` 只在锁内取 agent、下游状态并发裸奔 | 不成立 | `GetReply` 在 `_sync` 内完成 `agent.Respond`；快照和剧情到期读取也在同一所有权边界。事件泵、动画回调和二实例恢复都回到 WPF Dispatcher。没有在可变状态内重复叠锁。 |
| 语料/场景静态初始化失败会让服务命名空间永久不可用 | 此前已修复 | `PersonaCorpus` 与场景目录使用 `Lazy<T>`；`SceneCatalog.LoadPersonaScenes` 捕获非致命加载/契约异常并回退到内置本地目录，失败原因可诊断。故事目录在 fallback 不足时返回空集合。 |
| 设置和记忆固定 `.tmp` 互相覆盖 | 此前已修复 | 两者统一使用 `AtomicJsonFile`：随机临时名、按规范化目标路径的进程内 semaphore、覆盖式原子移动、仅清理本次临时文件，并有并发写回归。 |
| 52,132 / 51,326 / 533 只在文档脚本里检查 | 此前已修复 | 共享 persona contract 生成 C# 精确常量；程序集加载、.NET 测试、Python validator 与 CI 均要求精确计数。范围常量不替代精确发布常量。 |
| 哈希登记与“待复核”互相矛盾 | 此前已修复 | 当前 README 和发布清单区分历史已发布证据、tag 构建提交与其后的文档/资产登记提交，不再把具体哈希称作占位。 |
| fallback 文档说 scene-first，测试却全局先选行 | 此前已修复 | 当前 fallback 先选择语义场景，再在场景内选变体；旧的全局 LRU 测试和实现已经移除。 |

## C# 服务与桌面运行时

| 审查项 | 结论 | 当前证据/处理 |
| --- | --- | --- |
| 取消首轮 warmup 后永远复用已取消 Task | **已修复（本轮）** | 已完成且结果为 `Cancelled` 时，`StartAsync`/显式重试都会用新 token 建立新 run；回归验证旧、新 Task 不同并最终进入 Ready。 |
| `TemporalDialogueService` 每次扫描完整语料 | 此前已修复 | 构建期按时间桶建立索引，运行时不再全表过滤。 |
| `AgentMemoryService.IsValid` 热路径反复建三张字典 | 此前已修复 | 目录索引使用线程安全 Lazy 缓存。 |
| Pause 覆盖 Dragging/Landing | **已修复（本轮）** | 暂停请求只记录落地后的目标状态，不破坏拖动/落地瞬态；Resume 可在瞬态中撤销返回 Paused。 |
| 故事节点与普通场景共享 line ID，互相消耗冷却 | **已修复（本轮）** | 保留原始 ID/来源审计，不复制伪造台词；故事来源 `semantic_group` 从普通候选中保留给 story arc，避免普通播放提前消费故事冷却。 |
| `NotifyIcon` 可在错误线程构造 | **已修复（本轮）** | 在创建任何 WinForms shell 对象前检查目标 Dispatcher 线程；错误线程以明确异常拒绝。 |
| `MainWindow` 构造函数爆炸 | 此前已修复 | 当前只有少量入口，协作者集中在 `MainWindowDependencies` options 对象；没有再引入 Builder 层。 |

## Python、配置与模拟

| 审查项 | 结论 | 当前证据/处理 |
| --- | --- | --- |
| import `selector` 就读取默认配置 | 此前已修复 | 默认配置通过 lazy getter / module `__getattr__` 延迟解析。 |
| trigger matching 三份实现漂移 | 此前已修复 | 选择器、场景与 validator 共用 `trigger_matching.py`，测试禁止消费者重新声明。 |
| PII 标记双源 | 此前已修复 | `privacy.py` 是权威规则源；消费者不得复制身份/PII 词表。 |
| `repr()` fallback hash 不可复现 | 此前已修复 | 未知结构使用 canonical deterministic hash。 |
| `python -O` 会让输入校验 fail-open | 不成立 | 审查所指路径在 assert 前已有显式结构/约束校验；optimized-mode 负例会验证畸形输入仍被拒绝。其余 assert 只做经过验证后的类型收窄或测试内部不变量。 |
| contract/scheduler 重复字段双源 | 此前已修复 | scheduler 标注 derivation 来源并由生成/一致性门禁绑定；发布检查拒绝漂移。 |
| dawn 与 late-night 重叠 | 不成立 | 受控 time token 使用 dawn `[4,6)`、late-night `[0,4)` 和 `[23,24)`；较宽的 daypart 仅用于 trigger 分类，不会同时生成两个互斥 token。 |
| allowlist ID 含旧行号会随重排失效 | 此前已修复 | 身份采用 exact editorial manifest；legacy lineage 绑定冻结 source SHA 和物理行 epoch，重排必须整体重新审批。 |
| config 无 `$schema` | 此前已修复 | 四个配置均声明 schema，并在测试/validator 中加载。 |
| 模拟不覆盖预算、nullable signals、四季和 dawn | 此前已修复 | natural + adversarial 场景覆盖夜间/滚动小时/最小间隔、四季、04:00–05:59、四类 nullable 信号和边界值；回放绑定 corpus/config/derivation hashes。 |

## UI、可访问性与生命周期

| 审查项 | 结论 | 当前证据/处理 |
| --- | --- | --- |
| 无可访问性名称、菜单无法键盘使用 | 此前已修复 | 主窗口、人物、气泡 live region 与控制面板已有 Automation 属性；Popup 容器不取焦点，MenuItem 子项仍进入标准键盘导航。 |
| 硬编码主题不响应 Windows 高对比度 | **已修复（本轮）** | `PetThemeManager` 监听系统高对比度变化；气泡、文字、边框、菜单、选中/禁用态与阴影通过 DynamicResource 切换为系统 palette，退出时解绑。菜单同时处于 checked/highlighted 状态时改用系统 `HighlightText`，避免勾号与高亮背景同色消失。 |
| 隐式 MenuItem/ContextMenu 样式污染未来控件 | 此前已修复 | 卡哇伊样式使用显式 key，并只在桌宠 `ContextMenu.Resources` 内局部应用。 |
| 永久动画在托盘隐藏后继续 tick，控制器不释放 | 此前已修复 | `AnimationController` 实现 `IDisposable`，跟踪并移除 clocks；隐藏/暂停会 Suspend，关闭会 Dispose。 |
| 气泡倒计时跨线程字段可撕裂/关闭后复活 | 此前已修复 | 倒计时使用 `TimeProvider` 和单一 Dispatcher 所有权；悬停 suspend/resume、关闭与过期竞态均有回归。 |

## 测试与发布工程

| 审查项 | 结论 | 当前证据/处理 |
| --- | --- | --- |
| WindowShell 高价值用例依赖反射和真实 `Task.Delay` | **已优化（本轮）** | 环境调度通过内部运行时快照/显式处理入口观测，动画协作者有生产合理接口；关键问候、暂停、拖动/落地测试使用受控时间和完成回调。剩余反射只在尚未形成稳定观察契约的低风险边界。 |
| 性能预算混在普通测试中 | 此前已修复 | 性能用例带 `Trait(Category=Performance)`，可单独筛选，同时完整 Release 门禁仍会执行。 |
| PetAction 只有两个 happy-path 测试 | **已扩充（本轮）** | 覆盖暂停中的拖动/落地、恢复、重复 BeginDrag、错误完成状态等转换。 |
| manual review 数量硬编码 `3265 + 1248` | **已修复（本轮）** | 测试独立读取 review/PII TSV 数据行得到期望值，并要求结果非空。 |
| 发布 contract 用正则匹配脚本源码 | **已修复（本轮）** | smoke 进程生命周期提取为可调用模块；契约实际运行 helper，验证参数、隐藏窗口、input-idle 不能冒充成功、默认预算、非零退出、超时 PID 清理和同名无关进程不被终止。 |
| CI 可能由 runner 预装更高 .NET SDK 接管 | **已修复（本轮）** | 根 `global.json` 精确锁定 SDK，CI 从该文件安装并核对实际版本；tag 派生 `ProductVersion=<semver>+<commit>`。 |
| tag 规则会接受前导零等非法 SemVer | **已修复（本轮）** | 发布模块集中解析严格 SemVer：拒绝 core 数字前导零、纯数字 prerelease 前导零、build metadata、缺失前导 `v` 和不完整标签；质量门禁直接执行其行为合同。 |
| `Compress-Archive` 让同一提交重跑产生不同 ZIP | **已修复（本轮）** | 发布模块使用固定 DOS wall-clock、ordinal UTF-8 条目顺序、store 模式、CRC-32 与零 extra/comment/external metadata 写出确定性 ZIP；跨时区合同验证时间戳、条目、内容与两次 SHA-256 完全一致，并拒绝危险/重复叶名称、覆盖既有目标和失败残片。 |
| 只重跑 Release job 时找不到上一轮 package artifact | **已修复（本轮）** | package job 把实际 artifact 名称作为 job output；下游 release job 消费该输出，不再用自己重跑后变化的 `run_attempt` 重算名称。 |
| 已发布 tag 可被移动并用 `--clobber` 覆盖资产 | **已修复（本轮）** | 流水线要求 tag push 在本次事件中新建且非强推；已有 Release 必须具有精确八项资产，且每项下载后 SHA-256 与候选逐字节相同才允许原运行失败后的无操作式重跑。任何差异都会失败，不再删除、覆盖或编辑已有资产。被删除 tag 的历史复用仍应由 GitHub tag ruleset 阻止，不能从单次 push payload 反推全部历史。 |
| smoke 默认超时测试允许 30–120 秒漂移 | **已修复（本轮）** | 可调用策略与行为合同都要求默认值精确等于 30 秒；显式值仍受 1–120 秒参数范围约束。 |
| 根目录 `LICENSE` 中的 Markdown 被 GitHub 当纯文本展示 | **已修复（本轮）** | 仓库源文件改为 GitHub 可渲染的 `LICENSE.md`，官方原文 SHA-256 保持不变；CI 明确拒绝重新出现无扩展名源文件，并在 ZIP/Release 外层映射回惯用资产名 `LICENSE`。 |
| GitHub Release 自动说明可能混入英文 | **已修复（本轮）** | 流水线移除 `--generate-notes`，使用带稳定 `zh-CN` 标记的中文标题和六段中文发布说明，并在发布后回读标题、段落、tag、SHA、版本与 ProductVersion；仅法律要求的 `Required Notice` 保留英文原文。 |
| 离线 EXE 是否存在热更新、签名状态是否只是文档声明 | **已修复（本轮）** | 项目明确不含热更新、自动更新或联网下载代码机制。发布门禁对最终 delivery EXE 实际执行 `Get-AuthenticodeSignature`；当前策略精确要求 `NotSigned` 且 Signer/TimeStamper 证书均为空，并把结果写入证据与中文 Release。未来签名时必须显式切换到 `Valid` 和固定证书身份策略。 |

## 许可边界

仓库继续使用分层许可：可分离 Technical Code 按 PolyForm Noncommercial 1.0.0 提供，属于 **source-available noncommercial**，不是 OSI 开源。形象、图标、姓名/昵称、身份、人格、口吻、背景、关系设定、全部语料、语义树/森林、剧情和编辑性编排明确排除并保留全部权利；官方 Release 只额外允许非商业私下运行，不能抽取、转载、改编、训练或制作衍生角色。

需要注意：PolyForm Noncommercial 对 Technical Code 允许的范围比字面“只准技术学习”更宽，包含其他非商业用途以及按条款修改/分发。不能用 scope 文件暗中改写标准许可证。若未来要求代码本身也严格限于技术学习，应由权利人采用经过法律审定的自定义 source-available 许可，而不是把当前许可证误称为 OSI 开源。

## 最终验证

最终发布提交必须重新执行：生成契约检查、完整 Python 测试、语料 validator、完整 .NET Release 测试、发布 verifier contract、干净 self-contained single-file publish、隔离 `--smoke-test`、资产 SHA-256 覆盖和 tag/Release 回读。实际提交、测试数量、EXE 字节数与哈希登记在对应版本的发布清单和 GitHub Release 中。
