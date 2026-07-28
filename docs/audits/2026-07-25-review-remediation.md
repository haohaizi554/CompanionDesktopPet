# 2026-07-25 全面审查与修复记录

2026-07-26 Task 7 对原始 P0/P1/P2 审查附件重新逐行取证。审计基线为 `44cb7aa75513445e9802c11871fb56922eafb266`；不沿用旧审查的行号、严重度或“已修复”统计。每项都核对当前生产源码和现有覆盖测试，完整门禁的实际输出登记在发布清单与 Task 7 implementer report。

## 结论口径

- **Fixed**：当前生产源码已经实现修复，且存在直接覆盖该行为的自动化测试。缺任一项都不得标记 Fixed。
- **Rejected**：审查的技术前提不适用于当前可达生产路径；保留原 claim 并说明为什么不应按建议修改。
- **Open debt**：风险或维护成本仍真实存在，但当前没有在本任务越界修改生产/测试代码。
- **Unverified**：只有文档或搜索证据，没有同时得到生产源码与覆盖测试的证明。

## P0：发布阻断级原始条目

| 原始 claim | 结论 | 当前 source 证据 | covering test 证据 |
| --- | --- | --- | --- |
| `DialogueService` 只在锁内取 `_agent`，`Respond()` 在锁外并发执行 | **Fixed** | `Services/DialogueService.cs` 的 `GetReply`、`CreateSnapshot`、`NextStoryDueAt` 均在同一 `_sync` 内访问 agent；`Respond()` 也在锁内。 | `DialogueWarmupTests.GetReply_SerializesConcurrentCallsIntoTheMutableAgent` 并发调用后要求 `MaximumConcurrentCalls == 1`。 |
| `OfflineCompanionAgent` 和 `SceneHistory` 没有自己的锁，必然并发损坏 | **Rejected** | agent 只由 `DialogueService` 持有并在上述所有权锁内调用；`SceneHistory.Entries` 暴露只读 facade，`Restore` 先物化输入，避免 self-restore/别名破坏。给每个内部类型重复加锁会制造新的锁顺序。 | 同一并发序列化测试覆盖 agent 所有权；`SceneEngineTests.History_RestoreFromItsOwnEntriesPreservesEntriesAndIndexes` 与 `History_EntriesExposeAReadOnlyFacade` 覆盖 history 边界。 |
| `CharacterState.ActiveStories` 是 public `List`，外部可修改 agent 内态 | **Rejected** | `CharacterState` 是持久化 DTO；`CharacterState.Clone` 与 `AgentMemorySnapshot.DetachedCopy` 在 agent/持久化边界复制列表，`StoryProgress` 为不可变 record。 | `OfflineCompanionAgentTests.CreateSnapshot_ReturnsDetachedCharacterStateAndStoryCollection` 修改返回快照后再次取快照，证明内部状态未被穿透。 |
| `CompanionEventPump` 无锁且多个回调会并发修改 `_pending` | **Rejected** | `MainWindow` 由 `DispatcherTimer` 同步调用 `ProcessEventTimerTick`；`RestoreFromSecondInstance` 在非 Dispatcher 线程时先 `Dispatcher.Invoke`。当前没有第二个生产调用者或跨 await 的 `Poll`。 | `CompanionEventPumpTests` 覆盖同一轮多逻辑事件、story 去重和 idle 边界；`WindowShellTests.MainWindow_HiddenAndClosedQueuedTicksDoNotObserveOrRearm` 覆盖 Dispatcher 生命周期。测试名中的 Concurrent 指逻辑事件同轮出现，不是多线程。 |
| `PersonaCorpus → SceneCatalog → StoryArcCatalog` 静态初始化失败会毒化类型，无法 fallback | **Fixed** | `SceneCatalog.LoadPersonaScenes` 捕获非致命加载/契约异常并返回 `FallbackDialogueCatalog`；`StoryArcCatalog.Build` 在 fallback 场景不足时返回空集合；目录使用 `Lazy<T>`。 | `SceneCatalogSafetyTests.LoadPersonaScenes_PrimaryFailureReturnsFallbackWithoutPoisoningTheType`、`LoadPersonaScenes_CoverageFailureRecordsDegradedFallbackWithoutValidatingFallback`、`StoryArcBuild_InsufficientFallbackScenesDisablesStoriesInsteadOfThrowing`。 |
| `SettingsService`/`AgentMemoryService` 固定 `.tmp` 名导致并发覆盖 | **Fixed** | 两者统一调用 `AtomicJsonFile.WriteAsync`；它按规范化目标路径使用进程内 semaphore，并以 `Path.GetRandomFileName()` 创建独立临时文件，覆盖式原子移动且只清理本次临时文件。 | `SettingsServiceTests.ConcurrentSaves_UseIndependentTemporaryFilesAndLeaveOneCompleteDocument`、`AgentMemoryServiceTests.ConcurrentSaves_UseIndependentTemporaryFilesAndLeaveOneCompleteDocument`；`AtomicJsonFileTests.CleanupFailure_ReleasesDestinationGateAndPreservesPrimaryFailure`。 |
| 52,132 / 51,326 / 533 只写在文档，代码只检查 50k–60k | **Fixed** | `PersonaContract.g.cs` 生成 `ExpandedRuntimeRows=52132`、`LegacySurfaceRows=51326`、`SemanticSceneCount=533`；`PersonaCorpus.Build` 精确拒绝 runtime/surface 偏差。 | `PersonaCorpusTests.Corpus_LoadsTheCuratedEnabledV2Inventory`、`test_contract.PersonaContractFileTests.test_release_inventory_uses_exact_published_counts`、generator `--check` 测试及 validator 精确规模门禁。 |
| 哈希表已填写，同时文档仍称“待复核/占位”（原 P0） | **Unverified** | `README.md:24,65`、`README-persona-corpus.md:259-261` 与 `docs/release/2026-07-25-expanded-runtime-release-checklist.md:156-183,227-262` 现区分 `v1.0.0` 历史实证、当前 source gate 和 `v1.1.0` 待 tag 产物；Task 7 再次删除含混的“最终门禁已通过”泛称。 | 仓库没有解析这些 Markdown 状态与哈希表的自动化文档合同，因此不能标 Fixed。 |
| fallback 文档声称 scene-first，测试/实现却全局先选最旧行 | **Fixed** | `SceneScheduler.SelectReusableClickFallback` 先在场景层评分/选择，再只在所选场景内应用 line recency。 | `SceneEngineTests.ClickFallback_SelectsTheSceneBeforeApplyingLineRecencyWithinThatScene` 与 `SafeFeedback_SelectsTheSceneBeforeApplyingLineRecencyWithinThatScene`。 |

## P1：C# Services

| 原始 claim | 结论 | 当前 source 证据 | covering test 证据 |
| --- | --- | --- | --- |
| 取消首轮 warmup 后，新 token 永远复用已取消 `_run` | **Fixed** | `DialogueWarmupCoordinator.StartAsync` 在已完成且 outcome 为 `Cancelled` 时调用 `StartNewRunLocked`；显式 retry 同样允许 cancelled 新轮次。 | `DialogueWarmupCoordinatorTests.StartAsync_AfterCancelledRunStartsFreshRunWithNewToken`；退避/单飞另由 `StartAsync_TransientFailuresUseExactBoundedBackoffAndOneSharedRun` 覆盖。 |
| `TemporalDialogueService.GetContextualLines` 每次全扫 52k | **Fixed** | `ContextIndex` 为 `Lazy<IReadOnlyDictionary<ContextBucket,...>>`，`BuildContextIndex` 一次构建不可变 bucket。 | `TemporalDialogueServiceTests.GetContextualLines_ReusesAnImmutableBucketForEquivalentContexts` 与 `GetContextualLines_AfterWarmupHasBoundedSteadyStateAllocations`。 |
| `AgentMemoryService.IsValid` 每次重建三张目录字典 | **Fixed** | `CatalogIndex` 是 `ExecutionAndPublication` 的 `Lazy<RuntimeCatalogIndex>`，校验和 reconcile 复用同一索引。 | `AgentMemoryServiceTests.IsValid_AfterWarmupHasBoundedSteadyStateAllocations` 与 `ReconcileForRuntime_AfterWarmupDoesNotRebuildCatalogIndexes`。 |
| `PetActionCoordinator.Pause()` 覆盖 Dragging/Landing，残留 `_returnToPaused` | **Fixed** | `Pause` 在拖动/落地时只设置返回目标，`Resume` 可撤销目标；`Complete` 只接受可完成瞬态。 | `PetActionCoordinatorTests.PauseDuringDrag_PreservesDragAndReturnsToPausedAfterLanding`、`PauseDuringLanding_PreservesLandingAndReturnsToPausedAfterCompletion`、`Complete_RejectsStatesThatAreNotCompletableActions` 等 9 个针对边界的回归。 |
| 故事与普通场景复用 line ID 导致普通播放提前消耗故事冷却 | **Fixed** | `StoryArcCatalog.ReservedPersonaSemanticGroups` 从 story 节点导出保留组；普通候选排除这些组，同时保留 canonical line ID/来源和跨入口配额语义。 | `SceneEngineTests.StoryArcs_ReserveTheirSourcePersonaScenesFromOrdinaryCandidates` 与 `StoryArcs_UseOnlyEnabledV2Lines`。 |
| `NotifyIcon` 可能在线程错误处构造，事件失效 | **Fixed** | `TrayIconService` 在创建任何 WinForms shell 对象前校验目标 Dispatcher 线程，回调统一 marshal 到该 Dispatcher。 | `TrayIconServiceTests.Constructor_RejectsNonDispatcherThreadBeforeCreatingNativeShell`、`NotifyIconCallbackRaisedOnAWorker_RunsTheCommandOnTheDispatcher`。 |
| `MainWindow` 有 8 个、6–14 参数的构造重载，应改 Builder/Options | **Fixed** | 当前协作者集中于 `UI/MainWindowDependencies.cs`；主构造接收一个 dependencies 对象，仅保留两个小型兼容入口。 | `AppLifecycleTests` 和 `WindowShellTests` 多条生命周期测试直接用 `new MainWindow(new MainWindowDependencies(...))` 注入协作者并覆盖真实 WPF 行为。 |

## P1：Python 流水线与配置

| 原始 claim | 结论 | 当前 source 证据 | covering test 证据 |
| --- | --- | --- | --- |
| import `selector` 立即读默认配置文件 | **Fixed** | `selector.py` 通过 `_default_scheduler_config()` 与 module `__getattr__` 延迟解析 `DEFAULT_SCHEDULER_CONFIG`。 | `test_trigger_contract.SharedTriggerContractTests.test_plain_selector_import_does_not_read_files` 在 monkeypatch `Path.read_text` 为失败后导入模块。 |
| selector/scenarios/validator 三份 `_trigger_matches` 会漂移 | **Fixed** | 三处都导入 `trigger_matching.trigger_matches`；受控时间 token 也集中在同模块。 | `test_trigger_contract.SharedTriggerContractTests.test_all_three_paths_delegate_to_one_table_driven_matcher` 断言三个 callback 是同一函数对象并跑表驱动行为。 |
| normalization/content validation 的 PII marker 双源 | **Fixed** | `privacy.py` 是 marker/pattern 与 stage policy 的唯一规则源；builder、normalization、validation consumer 仅导入。 | `test_privacy_policy.PrivacyPolicyContractTests.test_direct_identifiers_have_the_same_findings_at_every_stage` 与 `test_consumers_do_not_redeclare_pii_marker_or_regex_tables`。 |
| validation hash 用 `repr()` fallback，跨进程不确定 | **Fixed** | `orchestration._stable_validation_node/_stable_validation_sha256` 使用类型化、排序、domain-separated canonical JSON 编码，不使用对象 repr。 | `StableValidationDigestTests` 覆盖映射/集合顺序、未知 key collision、两个 `PYTHONHASHSEED` 子进程的一致 SHA-256。 |
| `assert` 承担运行时输入校验，`python -O` 后 fail-open | **Rejected** | 所指 assert 位于显式类型/形状校验和 early return 之后，或只收窄已经过滤为 `row is not None` 的内部类型；失败路径由 `_Issues`/异常完成，不依赖 assert。 | `test_simulation.SimulationUnitTests.test_invalid_duration_seeds_and_config_fail_closed`、`test_validation` 的 malformed event/runtime limit 用例；Task 7 另以 `python -O` 运行这些负例，结果登记在 implementer report。 |
| scheduler 与 contract 的 weights/targets/runtime limits 双源漂移 | **Fixed** | scheduler 带 `derived_from.path/schema_version/sha256`，由 `generate_persona_scheduler.py` 从 contract 派生。 | `test_config_provenance.ConfigSchemaContractTests.test_generated_scheduler_is_current`，以及 `test_contract` 对三组值和 output-mode 聚合的逐项一致性测试。 |
| `time:dawn [4,6]` 与 late-night `[0,6]` 重叠会同时生成两个 token | **Rejected** | 宽 daypart 分类可包含 dawn，但互斥受控 token 使用 `time:dawn=[4,6)`、`time:late_night=[0,4)∪[23,24)`，由 `canonical_time_context_token` 只返回一个。 | `test_trigger_contract.SharedTriggerContractTests.test_dawn_and_late_night_tokens_are_non_overlapping_at_boundaries` 与 `SceneEngineTests.SceneContext_UsesExactlyOneCanonicalTimeToken`。 |
| identity allowlist ID 含源行号，语料重排后失效 | **Rejected** | 当前 ID 的确保留 source line，但 source SHA 与物理行 epoch 是冻结 provenance；重排不是受支持的无损操作，必须开启新 lineage epoch 并整体重新审批。去掉行号会削弱而不是增强审计。 | `test_build.RealCorpusBuildTests.test_real_lineage_matches_explicit_catalog_source_mapping`、`PersonaCorpusTests.Corpus_IdentityEasterEggsAreExactAndPrivacyScoped` 与 manifest exact-hash 测试。 |
| 四个 config 缺 `$schema` | **Fixed** | contract、scheduler、review allowlist、editorial manifest 均声明本地 Draft 2020-12 schema。 | `test_config_provenance.ConfigSchemaContractTests.test_every_public_config_has_a_parseable_resolving_local_schema` 对四份配置解析、检查 schema 并实际 validate。 |

## P1：UI、可访问性与生命周期

| 原始 claim | 结论 | 当前 source 证据 | covering test 证据 |
| --- | --- | --- | --- |
| 主窗口没有 Automation 名称或 live-region | **Fixed** | `MainWindow.xaml` 为 window、`CharacterStage`、speech、control menu 和命令设置 Automation name/help/live 属性；新台词更新 name 并触发 live-region changed。 | `WindowShellTests.MainWindow_ExposesAccessibleNamesAndLiveSpeechStatus` 与 `MainWindow_NewVisibleSpeechRaisesTheLiveRegionAnnouncementHook`。 |
| `Popup Focusable="False"` 阻断键盘和读屏 | **Rejected** | 该 Popup 是非交互只读语音 live-region，不应抢走人物/菜单焦点；可交互入口是可聚焦的 `CharacterStage` 和标准 `MenuItem`。把 popup 改为可聚焦会破坏键盘焦点链。 | 上述 live-region 测试，以及 `MainWindow_KeyboardControlMenuRestoresFocusAfterPopupCloses`、`MainWindow_KawaiiContextMenu_PreservesShellAndSubmenuBehavior`。 |
| 硬编码颜色不响应 Windows 高对比度 | **Fixed** | XAML 消费 `DynamicResource`；`PetThemeManager` 监听高对比度并切换到 `SystemColors` palette、禁用阴影，Dispose 时解绑。 | `PetThemeManagerTests.HighContrastChanges_ApplySystemPaletteAndRestoreNormalTheme`、`Dispose_UnsubscribesFromFutureThemeChanges`，以及菜单 DynamicResource 的 WPF 测试。 |
| `PetTheme.xaml` 的隐式菜单样式污染未来 TextBox 菜单 | **Fixed** | 三个 kawaii style 都有显式 key，只在 `ControlMenu.Resources` 局部设为隐式样式。 | `WindowShellTests.PetTheme_KeepsControlStylesKeyedUntilTheWindowScopesThem` 与 `MainWindow_KawaiiContextMenu_PreservesShellAndSubmenuBehavior`。 |
| `AnimationController` 无 IDisposable，隐藏托盘后永久 clock 继续 tick | **Fixed** | controller 实现 `IDisposable`、跟踪/移除 clocks；MainWindow 隐藏时 `Suspend`，关闭时 `Dispose`。 | `AnimationControllerTests.RestartAndDisposeKeepAConstantClockBudgetAndDetachAnimations` 与 `WindowShellTests.MainWindow_TrayHideSuspendsPresentationSchedulersAndQueuesHiddenSpeech`。 |
| `BubbleCountdownController` 的 long/bool 会在 32 位或跨线程撕裂/看不到关闭 | **Rejected** | 交付目标固定 `win-x64`；controller 不拥有 timer/task/native handle，所有生产调用与拥有它的 `DispatcherTimer` 都在单一 WPF Dispatcher，关闭是终态。对无跨线程入口的状态机增加锁/volatile 不解决可达问题。 | `BubbleCountdownControllerTests.HideAndCloseCannotBeRevivedByLeaveOrShow`、`SuspendFreezesTheDeadlineUntilResume`，以及 WindowShell 的隐藏/关闭 timer 生命周期回归。 |

## P2：测试、发布脚本与文档

| 原始 claim | 结论 | 当前 source 证据 | covering test 证据 |
| --- | --- | --- | --- |
| `WindowShellTests` 大量反射私有成员/构造签名，重构脆弱（原 P2） | **Open debt** | `tests/CompanionDesktopPet.Tests/WindowShellTests.cs:475-3847` 当前仍有 `GetPrivateField` 31 处、`SetPrivateField` 3 处、`InvokePrivate`/`InvokePrivateAsync` 30 处（含 helper 定义）；部分新 cadence/lifecycle seam 已改为 internal snapshot/processor，但未清零。 | 完整 .NET suite 只能证明现状可执行，不能消除反射耦合；不标 Fixed。 |
| 900-click 性能预算与功能断言混在单元测试（原 P2） | **Open debt** | `OfflineCompanionAgentTests.cs:329-411` 的 `Respond_RepeatedClicksRemainResponsiveAndDoNotSuppressAutomatic` 已有 `Trait(Category=Performance)`，但仍在一个方法中同时断言无静默、配额、p95 `<50ms` 与 256 MiB。 | Trait 可筛选，完整 suite 也会运行；结构性混合仍存在，故不标 Fixed。 |
| `Task.Delay(800)`/`Elapsed <100ms` 墙钟依赖会在慢 CI 抖动（原 P2） | **Open debt（部分缓解）** | 原 `Task.Delay(800)` 动画等待已删除，延迟/倒计时主要使用注入时钟；但 `DialogueWarmupTests.cs:34-38` 与 `WindowShellTests.cs:754-758` 仍各有一个 `<100ms` 同步 fallback 阈值。 | monotonic/manual clock 测试覆盖大部分生命周期；两处真实耗时阈值仍在完整 suite 中，故不标 Fixed。 |
| `PetActionCoordinatorTests` 只有两个 happy path | **Fixed** | 状态机显式拒绝未知 ambient 与非法 Complete，并保留 pause/drag/landing 目标。 | 当前测试覆盖 ambient 互斥、拖动/落地 pause、瞬态 resume、重复/重启拖动、非法 Complete 与未知 enum，远超原两个用例。 |
| simulation test 硬编码 `3265 + 1248` manual review 数量 | **Fixed** | `simulation.summarize_editorial_outcomes` 从实际 review/PII collections 求和。 | `test_simulation` 读取 tracked review 与 PII TSV 的实际数据行、要求非空并与 `summary.manual_review_items` 比较。 |
| `Verify-Publish.Contract.ps1` 用正则匹配脚本文本 | **Fixed** | smoke 生命周期提取到可调用的 `Verify-Publish.Core.psm1::Invoke-PublishSmokeTest`；入口脚本调用模块。 | `tests/Verify-Publish.Contract.ps1` 实际运行 helper，覆盖参数、隐藏窗口、input-idle 假成功、默认预算、非零退出、超时 PID 清理与同名无关进程。 |
| README 的“已验证”与“待校验”自相矛盾（原 P2；与上方 P0 哈希项独立） | **Unverified** | `README.md:50-65` 将泛称改为明确的 `v1.0.0` 历史实证，并声明 `v1.1.0` 必须重新取证；哈希表也按版本分节。 | 没有 Markdown 状态合同测试，不能仅靠文案审阅标 Fixed。 |
| README 宣称 `--smoke-test`，代码可能不支持 | **Fixed** | `App.OnStartup` 精确识别 `--smoke-test`；发布 helper 精确以该唯一参数启动。 | `tests/Verify-Publish.Contract.ps1` 检查收到的参数；真实 WPF smoke/CI 发布路径执行同一入口。 |
| 隐私文案声称不读标题/输入/用户文件，但 `Environment.ProcessPath` 存在张力 | **Fixed（边界已澄清）** | README 明确“用户文件”与桌宠自身 EXE 路径/本地状态边界。新增全屏探测的 native contract 只含 HWND 有效性/可见性/样式、DWM cloaked/frame geometry 和 monitor bounds；没有 title/process/input/clipboard/file/pixel/network API。 | `WindowsForegroundFullscreenDetectorTests` 覆盖有效、不可见、child、cloaked、几何、多显示器及所有 query failure 保持 `null`；`DialogueSchedulerTests` 覆盖全屏只改变 cadence。 |

## 补充复审项（不在原始附件中，保留既有覆盖）

- `SettingsService.LoadAsync` 诊断：**Fixed**。源码对 I/O/JSON/contract 异常保留诊断同时回退默认值；`SettingsServiceTests.Load_MalformedJson_ReportsTheFallbackReasonOnce`、`Load_ContractInvalidValues_ReportTheFallbackReason`、`Load_DiagnosticFailureDoesNotDisableTheDefaultFallback` 覆盖。
- tray best-effort cleanup：**Fixed**。源码逐资源释放、报告非致命异常且不吞 fatal；`TrayIconServiceTests.Dispose_SuppressesQueuedCommandsAndIsIdempotentBestEffort`、`PublishFailure_CleansEveryCreatedNativeResourceBeforeRethrowing` 覆盖。
- 已打开气泡随窗口漂移：**Fixed**。MainWindow 使用同一 SystemAware 虚拟桌面绝对坐标定位 popup；WindowShell 的气泡定位/移动回归覆盖 X/Y 同步、30-DIP 间距与工作区夹取。
- `DialogueForest.TreeWeights` 手写聚合：**Open debt**。当前值与生成 contract 相等，但仍是双写；没有专门防漂移测试，不标 Fixed。
- Release 工程（SDK pin、严格 SemVer、确定性 ZIP、artifact handoff、不可变已有 Release、30 秒 smoke、`LICENSE.md`、中文 Release、Authenticode 状态）：均有对应生产 workflow/module 与 `test_contract.py`、`Release-Packaging.Contract.ps1`、`Verify-Publish.Contract.ps1` 行为合同；其完整历史证据保留在发布清单。Task 7 不重写或冒充目标 tag 的二进制证据。

## 发布结论边界

本审计只证明当前 source/test 对原始 claim 的状态，不把搜索结果、历史测试数字或旧 Release 哈希冒充新鲜证据。Task 7 的 simulation、validator、Python unittest、.NET restore、`IsTestProject`、完整 Release suite 和最终 `main` CI 必须全部以实际输出登记；目标 `v1.1.0` 的 EXE、ProductVersion、签名、隔离 smoke、资产哈希与 Release URL 仍只能由新 annotated tag 流水线产生。
