# 全屏感知与可见自动气泡频率设计

**日期：** 2026-07-25

**状态：** 已确认，待实施

**目标分支：** `main`

## 1. 目标

把桌宠的自动说话频率改成用户实际能看到的气泡节奏，并接通真实的 Windows 前台全屏状态：

| 当前本地时间 / 状态 | 下一条可见 `Automatic` 气泡目标窗口 |
|---|---:|
| 白天 06:00–17:59 | 5–15 分钟 |
| 傍晚与夜晚 18:00–22:59 | 10–20 分钟 |
| 深夜与黎明 23:00–05:59 | 30–60 分钟 |
| 明确检测到前台全屏 | 60–120 分钟，覆盖所有时段 |

这里的窗口不是“后台重试间隔”。在文库已通过校验并完成加载、桌宠可见且没有退出或隐藏到托盘时，计时到期必须能选出并显示一条 `Automatic` 气泡，不能再被旧的每小时预算静默吞掉。

用户主动点击、拖动结束和控制面板操作仍应立即反馈，不受上述自动频率限制。主动交互或其他事件已经显示气泡后，重新计算下一次自动气泡，避免两条气泡紧挨出现。

## 2. 时段唯一来源

不新增第二套小时判断。所有边界复用 `TemporalDialogueService.GetTimePeriod`：

- `LateNight`：23:00–03:59；
- `Dawn`：04:00–05:59；
- `Morning`、`Noon`、`Afternoon`：06:00–17:59；
- `Evening`：18:00–22:59。

`Dawn` 继续产生 `time:dawn` 内容上下文；本设计只把它和深夜映射到相同的 30–60 分钟节奏，不改变语料触发语义。随机上下界均包含在内。

## 3. 可见节奏与打扰预算的职责

当前 `InterruptionBudget` 同时包含 8 分钟最小间隔、滚动一小时最多 2 次、深夜最多 1 次和全屏 2 小时门槛。若只改 `DialogueScheduler`，白天仍大多只能看到每小时 2 条，30–59 分钟的深夜尝试可能被拒绝，全屏最坏会接近 239 分钟才出现下一条气泡。

采用以下分层：

1. `DialogueScheduler` 是 `Automatic` 的唯一频率门；
2. `Automatic` 选择场景时绕过 `InterruptionBudget.CanPlay`；
3. 绕过预算不等于绕过内容质量：上下文匹配、语义组冷却、相邻类别约束、近期比例、行冷却、每日上限和表面多样性继续生效；
4. 若正常选择没有候选，进入仅供 `Automatic` 使用的安全降级选择：仍要求触发与上下文匹配、启用状态、稀有内容配额和非连续同句，优先未使用或最久未使用的 v2 运行时语料；
5. 只有校验通过的运行时语料可参与降级，不引入网络内容，也不把 75,375 行原始 TSV 直接解析进 UI 热路径；
6. 文库未就绪、校验失败、窗口隐藏或应用关闭属于明确的不可用状态，不伪装成已显示；不得用紧循环补偿；
7. `Click`、`DragReleased`、`AnimationPaused`、`AnimationResumed`、`SizeChanged`、`PositionRestored` 作为用户直接动作绕过主动打扰预算；
8. `DayChanged`、`IdleReturned`、`StoryTimerDue`、`ClockTick` 等事件型输出继续使用 persona 打扰预算。成功显示后重置自动计时器；`intentional_silence` 不算可见气泡，也不错误延后已有自动计时。

全屏 2 小时硬门槛不再叠加到 `Automatic`；全屏安静程度由 60–120 分钟节奏直接表达。事件型候选仍可使用单独的打扰预算，配置与文档必须明确其作用域，避免再次把它误解成自动计时器。

## 4. 三态全屏信号

新增可注入接口：

```csharp
internal interface IForegroundFullscreenDetector
{
    bool? Observe(nint excludedWindow);
}
```

语义：

- `true`：稳定的当前前台窗口有效覆盖某一台完整显示器；
- `false`：已可靠确认当前前台窗口不是这种全屏窗口；
- `null`：前台切换竞态、桌宠自身窗口、受保护桌面或 Win32 查询失败，状态未知。

`null` 不能写成 `false`，因此不会错误产生 `not_fullscreen` 内容 token。`MainWindow` 保存最后一次明确的 `true/false` 仅用于计时模式：一次未知观察不会打断已确认的安静模式；应用启动尚无明确观察时，按当前时段安排，避免桌宠首次获得焦点时被错误降到 60–120 分钟。

每次显示决策只采样一次。该次原始观察传入对话上下文，已确认状态用于选择计时窗口，避免前台窗口在一次操作中变化而得到互相矛盾的结果。

## 5. Windows 检测算法

生产实现为 `WindowsForegroundFullscreenDetector`，底层 Win32 调用隔离到可替换的 native adapter。每次最多尝试两轮：

1. `GetForegroundWindow` 为零、等于桌宠自己的 HWND，或两次读取不稳定：返回 `null`；
2. Desktop/Shell 窗口：返回 `false`；
3. 无效、不可见、最小化、child 或 cloaked 窗口：返回 `false`；
4. 查询 cloaked 状态、DWM 可见边界或显示器信息失败：返回 `null`；
5. 用 `MonitorFromWindow(..., MONITOR_DEFAULTTONULL)` 找到相交最大的显示器；无显示器返回 `false`；
6. 使用 `DWMWA_EXTENDED_FRAME_BOUNDS` 与 `MONITORINFO.rcMonitor` 比较四条边；每边误差不超过 1 个原生像素时返回 `true`，否则返回 `false`；
7. 最后再次读取前台 HWND；若已经变化则重试一次，仍不稳定返回 `null`。

不回退到受 DPI 虚拟化且包含不可见 resize border 的 `GetWindowRect`，不把 WPF DIP 与 native screen coordinates 混算，不缓存 `HMONITOR` 或显示器矩形。这样支持副屏负坐标、竖屏、RDP 动态分辨率和显示器热插拔。

普通最大化窗口通常只覆盖 `rcWork`，应为 `false`；自动隐藏任务栏时若窗口几何完整覆盖 `rcMonitor`，按减少打扰的产品目标视为 `true`；跨屏铺展但不精确匹配任一单屏时为 `false`。

检测器不得读取窗口标题、进程名、文件名、输入内容、剪贴板或屏幕像素。

## 6. 轮询与状态转换

采用现有 30 秒 `EventTimer` 加关键路径即时采样，不引入 `SetWinEventHook`：

- `Window_Loaded`：首次采样并武装计时器；
- 每次 30 秒事件轮询：检查明确的全屏状态或时段是否变化；
- `AutomaticTimer_Tick`：显示前再次采样；
- 从托盘恢复：重新采样后武装；
- 隐藏到托盘、关闭窗口：停止采样与计时。

若已武装计时器对应的模式与最新明确模式不同，例如进入/退出全屏，或在非全屏模式下跨过 18:00、23:00、06:00 边界，则旧计时器只静默作废并按新窗口重排，本次不说话。全屏期间跨时段仍保持同一个 60–120 分钟模式，不做无意义重排。状态转换不发送 `CompanionEvent.FullscreenChanged`，不修改 `LastReply`，也不额外显示气泡。

选择轮询是因为前台 HWND 不变的 F11 切换不会可靠产生 foreground hook 事件；为位置变化再加全局 hook 会引入拖动/缩放事件洪水，最终仍需要轮询。

## 7. 调用链

同一个观察快照沿下面的路径传递：

```text
MainWindow
  -> DialogueScheduler.NextDelay(localTime, effectiveFullscreen)
  -> DialogueService.GetReply(trigger, localTime, random, observedFullscreen)
  -> ICompanionDialogueAgent.Respond(..., observedFullscreen)
  -> OfflineCompanionAgent
  -> SceneContext.IsFullscreen
```

`false` 才添加 `not_fullscreen`；`true` 和 `null` 都不伪造该 token。`MainWindowDependencies` 提供 detector 与可测试的 scheduler/clock 缝，生产默认实例由应用创建。

`ShowEventBubble` 返回本次是否真正显示文本。任何真正显示的用户交互或事件气泡都会从同一时间点重排自动计时；静默回复不覆盖当前可见气泡，也不制造高频重试。

## 8. 测试策略

实施必须先写失败测试，再写代码。至少覆盖：

### 8.1 频率边界

- 03:59:59、04:00、05:59:59、06:00；
- 10:59:59、11:00、13:59:59、14:00；
- 17:59:59、18:00、22:59:59、23:00；
- 脚本化随机数分别命中每个区间的最小值和最大值；
- 每个时段下已确认的 `fullscreen=true` 均覆盖为 60–120，明确 `false` 走时段节奏；`null` 不覆盖上一次已确认状态，启动时尚无确认值才走时段节奏。

### 8.2 可见输出契约

- 构造已触发旧的 8 分钟、2/小时、深夜 1/小时和全屏门槛的历史，`Automatic` 仍能选择并显示；
- `Automatic` 降级仍来自启用的 v2 运行时语料，并避免连续同句；
- 事件型输出仍受预算控制，用户直接动作仍有反馈；
- 可见 Click/事件输出后自动计时重排，静默回复不会造成气泡闪烁或紧循环；
- 全屏计时不再与旧 2 小时门槛叠加。

### 8.3 检测器

- NULL 前台、桌宠自身 HWND、连续前台竞态：`null`；
- Desktop/Shell、不可见、最小化、child、cloaked：`false`；
- DWM/显示器查询失败：`null`；
- 完整覆盖、每边 1 px：`true`；任一边超过 1 px：`false`；
- 只覆盖 `rcWork`：`false`；自动隐藏任务栏下完整覆盖：`true`；
- 负坐标副屏、竖屏、跨屏、离屏和动态显示器矩形；
- 所有分类测试使用 fake native adapter，生产 P/Invoke 只做无异常冒烟。

### 8.4 WPF 生命周期与传播

- 每个决策只调用 detector 一次；`true/false/null` 完整传播到 `SceneContext`；
- 进入全屏、退出全屏和跨时段时旧计时器静默重排；
- 桌宠自身获得前台不会把已知全屏降为非全屏；
- 隐藏与关闭后不再探测，从托盘恢复重新探测；
- 转换本身不说话、不改回复版本、不重复保存；
- warmup、并发、托盘、单实例、气泡倒计时与既有窗口测试全部保持通过。

## 9. 文档与隐私

README 和语料说明必须同步更新：

- 明确自动气泡的四档频率；
- 说明只读取前台 HWND 的可见性、窗口样式、DWM 几何和显示器矩形；
- 明确不读取标题、进程内容、键盘、剪贴板、文件或网络；
- 查询失败保留未知状态，不把未知写成非全屏；
- 应用没有热更新，Release EXE 继续按既有策略验证为未签名并用 SHA-256 校验完整性。

## 10. 分阶段交付

1. 设计契约：本文件，独立提交并推送；
2. 频率契约与预算例外：scheduler、agent、纯逻辑测试，独立提交并推送；
3. Win32 detector：native adapter 与确定性测试，独立提交并推送；
4. WPF 接线：状态传播、重排、托盘生命周期与窗口测试，独立提交并推送；
5. 文档与全量验证：README、隐私、审计证据，独立提交并推送；
6. 最终 Release：从最终 `main` 构建、打标签、上传、代理回下载并复核所有资产。

## 11. 验收标准

功能完成必须同时满足：

1. 健康运行时的 `Automatic` 可见输出符合四档目标窗口，旧预算不再静默吞掉它；
2. 全屏检测在正常窗口、最大化、F11/无边框全屏、多显示器和查询失败下符合三态契约；
3. 直接交互保持即时反馈，自动气泡不会紧跟在刚显示的交互/事件气泡之后；
4. 没有新增窗口标题、进程内容、输入或网络采集；
5. 全量 .NET、Python、语料校验、生成器一致性、打包与真实 EXE 冒烟全部通过；
6. 每个实现阶段均有独立提交、远端推送和可追溯验证证据。
