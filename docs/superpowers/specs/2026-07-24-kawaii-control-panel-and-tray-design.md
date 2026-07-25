# 卡哇伊控制面板、气泡交互与托盘设计

**日期：** 2026-07-24
**状态：** 待用户书面确认后实施
**分支：** `feat/cute-companion-desktop-pet`

## 1. 目标

在不改变离线语料、隐私边界和单文件交付约束的前提下，完成以下桌面交互升级：

1. 将人物右键菜单改成已确认的 A+B「奶油樱花糖」风格；
2. 将气泡尾尖与人物顶部的视觉间距固定为已确认的 30 DIP；
3. 让点击爱心时的短促倾斜左右交替，不再永远向右；
4. 鼠标停在人物或气泡上时暂停气泡消失倒计时，移开后从剩余时间继续；
5. 在人物右键菜单和托盘菜单中提供当前用户级「开机自启动」开关；
6. 增加常驻系统托盘，可显示/隐藏桌宠、说句话、暂停/继续、切换自启动和退出。

保留现有点击爱心、拖动方向倾斜、落地回弹、大小、置顶、位置恢复、对话森林、记忆与发布门禁。

## 2. 硬约束与非目标

- 仍是 Windows x64、完全离线、自包含单 EXE；不增加 NuGet 托盘包、外置 DLL、JSON sidecar 或后台辅助进程。
- 不读取输入内容、剪贴板、窗口标题或网络状态，也不枚举或读取用户文件名、用户目录内容；自启动只使用桌宠自身 EXE 路径。
- 不把自启动状态写进 `PetSettings/settings.json`。注册表是唯一真相源，避免旧五字段设置因严格反序列化而整体回退。
- 不修改其他程序的启动项，不申请管理员权限，不使用 HKLM、计划任务或启动文件夹脚本。
- 托盘图标始终存在，不提供“关闭托盘图标”开关；否则桌宠隐藏后可能失去恢复入口。
- 本轮不扩展单实例协议。桌宠隐藏时再次双击 EXE，第二实例仍会按现有互斥锁规则静默退出；恢复入口是托盘。

## 3. 方案比较与选型

### 3.1 右键菜单

- 纯奶油实体卡片：最稳定，但梦幻感偏弱；
- 重度系统毛玻璃：更通透，但不同 Windows 环境下表现和可读性有差异；
- **选定：奶油实体底 + 轻樱花渐变 + 白色内高光 + 柔粉阴影。**

选定方案继承 A 的清楚轮廓和稳定底色，同时保留 B 的透明感与柔光，不依赖系统模糊 API。

### 3.2 托盘

- 第三方 WPF NotifyIcon 包：封装方便，但会增加依赖和发布风险；
- 自建 Win32 Shell_NotifyIcon 封装：控制完整，但原生互操作和生命周期代码过重；
- **选定：框架内置 `System.Windows.Forms.NotifyIcon + ContextMenuStrip`。**

项目已经启用 `UseWindowsForms=true`，并内嵌 `Assets/pet.ico`，因此选定方案不增加第三方依赖或外置文件。

### 3.3 自启动

- 设置 JSON：容易形成系统状态与配置文件双真源，并破坏旧配置兼容；
- 启动文件夹快捷方式/脚本：会创建额外文件，移动 EXE 后容易残留；
- **选定：`HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` 的单个 REG_SZ 值。**

该方案无需管理员权限，状态可直接由 Windows 真相源读取，也不会改变单 EXE 交付形态。

## 4. 「奶油樱花糖」菜单表面

主题资源集中在 `Themes/PetTheme.xaml`，生产 XAML 不散落颜色常量：

- 表面：从近不透明奶油白 `#FAFFFDF7` 过渡到浅樱花粉 `#E8FFE0EA`；
- 描边：半透明玫瑰粉 `#B8E56F91`，2 DIP；
- 外框：24 DIP 圆角、11 DIP 内距、270 DIP 最小宽度；
- 阴影：低透明柔粉阴影，模板外围保留安全边距，不能被 Popup 裁切；
- 条目：最小高度 35 DIP、14 DIP 圆角、上下 2 DIP 间距；
- 悬停：白粉高光胶囊和细内描边；
- 分隔线：透明—玫瑰—透明三段渐变；
- 文字：现有可可色与微软雅黑 UI，禁用项降低透明度但仍可读。

`MenuItem` 自定义模板必须保留原生行为：

- `IsHighlighted`、`IsEnabled`、`IsChecked`；
- `Role=SubmenuHeader` 与右箭头；
- 名为 `PART_Popup` 的子菜单 Popup；
- 键盘方向导航与勾选状态。

菜单行为继续由 WPF 原生 `ContextMenu/MenuItem` 管理，主题只负责表面。

## 5. 气泡与人物的 30 DIP 布局

`SpeechBubble` 和 `CharacterStage` 放入同一个底部对齐纵向 `StackPanel`：

```text
[SpeechBubble]
      30 DIP
[CharacterStage]
---------------- window bottom
```

- `SpeechBubble.Margin="12,0,12,30"`；
- 删除 `CharacterStage` 当前只在底部对齐时失效的顶部 Margin；
- 窗口高度从 500 调整到 520 DIP；
- CharacterStage 仍贴住窗口底部，Small / Normal / Large 三档自动保持相同 30 DIP 间距；
- 气泡折叠时自身和 Margin 都不占位，人物不会跳动；
- 当前最长启用语料在 Large 模式下仍保留约 20 DIP 顶部安全区。

30 DIP 指气泡三角尾尖的布局底部到 `CharacterStage` 顶部的距离，允许抗锯齿和布局舍入带来不超过 0.5 DIP 的误差。

## 6. 点击倾斜方向

现有拖动倾斜已根据水平位移进入 `-8°..+8°`，待机摇摆也已双向；缺陷只在点击爱心反应固定使用 `+2.2°`。

点击反应改为确定性交替：

```text
第一次点击  -> -2.2°（向左）
第二次点击  -> +2.2°（向右）
之后        -> 左右严格交替
```

- 不使用随机，避免连续多次同向后看起来仍像故障；
- 人物点击和右键/托盘“说句话”共用同一序列；
- 快速连点即使替换上一段动画，也会按调用次数翻转方向；
- 每段结束仍回到 0°；
- 方向是渲染细节，不扩张 `PetActionCoordinator` 状态，也不改变暂停时仍可点击爱心的现有规则。

## 7. 气泡倒计时状态机

新增一个与 WPF 表面分离、使用单调时间的 `BubbleCountdownController`。`DispatcherTimer` 只负责唤醒，剩余时间由控制器维护。

```text
[Hidden]
  -- Show --> [CountingDown(5s)]
  -- Show while hovered --> [HoverPaused(5s)]

[CountingDown(remaining)]
  -- first target enters --> [HoverPaused(remaining)]
  -- new message --> [CountingDown(5s)]
  -- due --> [Hidden]

[HoverPaused(remaining)]
  -- new message --> [HoverPaused(5s)]
  -- last target leaves --> [CountingDown(remaining)]
  -- explicit hide / close --> [Hidden]
```

悬停来源使用 `[Flags] BubbleHoverTarget { None, Character, Bubble }`，不使用含义模糊的单一布尔值：

- 人物和气泡分别接一次 `MouseEnter/MouseLeave`，不监听高频 `MouseMove`；
- 任一目标仍处于悬停时都保持暂停；
- 从人物移动到气泡时，短暂恢复也只消耗真实经过的毫秒，不会重置 5 秒；
- 悬停期间出现新消息，显示新内容并把剩余时间重置为完整 5 秒，但继续保持暂停；
- 主动隐藏先切换为 `Hidden` 再折叠控件，折叠引发的 `MouseLeave` 不得复活计时器；
- 已排队或提前到达的旧 Tick 必须重新检查状态和截止时间，不能关闭暂停中的气泡；
- 窗口关闭后状态和 WPF Timer 都永久停止。

## 8. 开机自启动

注册位置：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
值名：CompanionDesktopPet
类型：REG_SZ
值：  "D:\最终目录\佳怡桌宠.exe"
```

实现契约：

- 使用 `Environment.ProcessPath`，不用 single-file 场景不可靠的 `Assembly.Location`；
- 绝对路径整体加双引号，支持中文和空格；
- 只有注册表值与当前 EXE 路径按 `OrdinalIgnoreCase` 完全匹配时，菜单才显示已启用；
- 缺失、旧路径、未加引号或畸形值都显示关闭，读取时不静默覆盖；重新勾选会覆盖为当前路径；
- 禁用只删除 `CompanionDesktopPet` 自己的值，并允许值原本不存在；
- 读取/写入捕获 `IOException`、`UnauthorizedAccessException` 和 `SecurityException`；
- 写入失败时恢复原勾选状态，显示一条不写入角色记忆的简短功能错误气泡，程序继续运行，用户可重试；
- 人物菜单和托盘菜单每次打开前都从注册表刷新，不缓存为第二真相源；
- 默认关闭。移动或重命名 EXE 后需要重新勾选，以修复路径。

服务边界：

- `IAutoStartService.TryGetEnabled(out bool enabled)`；
- `IAutoStartService.TrySetEnabled(bool enabled)`；
- 生产实现使用当前用户注册表；测试使用内存假存储，绝不触碰用户真实注册表。

`--smoke-test` 路径显式使用禁用/no-op 实现，不读取也不写入 Run 键。

## 9. 托盘管理

托盘图标由 `App` 持有，而不是由可隐藏的窗口持有。窗口显示成功后才创建托盘；创建失败时桌宠保持可见且程序不崩溃。

人物右键菜单新增：

- `开机自启动`（可勾选）；
- `藏到托盘里 ♡`。

托盘交互：

- 双击图标：显示/隐藏桌宠；
- 右键菜单第一项根据窗口状态显示 `显示佳怡` 或 `藏起佳怡`；
- `说句话 ♡`；
- `暂停动画` / `继续动画`；
- `开机自启动`（与人物菜单读取同一注册表状态）；
- 分隔线；
- `先休息啦（退出）`。

行为规则：

- 隐藏使用 `Window.Hide()`，不能调用 `Close()`，不改变用户的动画暂停设置；
- 显示使用 `Show()`、恢复普通窗口状态并 `Activate()`；
- 人物菜单和托盘调用同一套共享命令，不能复制一份状态逻辑；
- 退出命令幂等：只保存一次设置与记忆，再关闭窗口并由现有显式 Shutdown 流程退出；
- WinForms 托盘回调统一投递到 WPF Dispatcher；
- 托盘菜单打开前刷新可见性、暂停、自启动的标签和勾选状态；
- `App.OnExit` 按 `NotifyIcon.Visible=false`、释放菜单、释放克隆 Icon、释放 NotifyIcon、释放单实例互斥锁的顺序清理，避免幽灵托盘图标；
- `--smoke-test` 完全跳过托盘创建。

`pet.ico` 当前可直接使用。它只有一个高分辨率帧，小尺寸托盘可能略软；补多尺寸 ICO 是可选美术优化，不阻塞本轮功能。

## 10. 组件与所有权

- `Themes/PetTheme.xaml`
  - 菜单语义 token、ContextMenu/MenuItem/Separator 模板。
- `MainWindow.xaml`
  - 底部对齐的气泡/人物栈、新菜单项与模板挂接。
- `AnimationController`
  - 维护下一次点击倾斜符号并严格交替。
- `BubbleCountdownController`
  - 纯状态机、剩余时间和双悬停目标；不引用 WPF 控件。
- `IAutoStartService` / `WindowsAutoStartService`
  - 当前用户 Run 值读写；注册表存储接口可替换。
- `MainWindow.xaml.cs`
  - 将 WPF 事件转换为共享命令，渲染倒计时状态，提供托盘可调用命令。
- `TrayIconService`
  - NotifyIcon、原生托盘菜单、状态同步和资源释放；不拥有业务状态。
- `App`
  - 组合生产服务并拥有托盘生命周期；烟测使用 no-op 系统集成。

现有公开构造函数和测试依赖的 8 参数内部构造函数必须保留；新增依赖通过保留旧 overload 并委托给完整构造函数接入。

## 11. 测试策略

实施必须遵循 RED → GREEN → REFACTOR，并覆盖：

### 11.1 视觉与布局

- Small / Normal / Large 三档渲染后，气泡尾尖底部与人物顶部距离均在 `29.5..30.5` DIP；
- Large + 当前最长启用语料时，气泡顶部不越出窗口；
- 菜单表面渐变、描边、24 圆角和阴影存在；
- 条目 35 高、14 圆角，Separator 是透明—粉色—透明渐变；
- `大小` 子菜单能用鼠标和键盘打开，勾选和禁用触发器正常。

### 11.2 动画

- 连续两次点击反应分别只进入负角度和正角度；
- 两次结束都回到 0°；
- 快速替换动画仍翻转方向；
- 现有拖动双向倾斜、爱心和落地测试继续通过。

### 11.3 气泡状态机

- 显示从 5 秒开始；
- 运行 2 秒后悬停只保存约 3 秒，长时间悬停不消耗；
- 最后一个目标离开后只继续剩余时间；
- 人物和气泡两个标记都离开才恢复；
- 悬停中新消息重置为暂停的 5 秒；
- 主动隐藏、陈旧 Tick 和关闭均不能复活或误关气泡。

### 11.4 自启动

- 缺失、匹配、大小写差异、旧路径、畸形值和异常路径；
- 中文/空格路径被正确双引号包裹并写为 REG_SZ；
- 禁用只删除自己的值且幂等；
- 菜单打开刷新，失败回滚；
- 旧五字段 `settings.json` 原样加载；
- 单元测试只使用假注册表存储。

### 11.5 托盘与生命周期

- 所有托盘菜单命令路由到共享窗口命令；
- 动态标签/勾选状态同步；
- 隐藏不触发 Closed 或应用退出，再显示后仍可交互；
- 重复退出只执行一次；
- Dispose 先隐藏图标并释放全部资源；
- smoke 模式不创建托盘、不访问注册表。

## 12. 验证与交付

只有以下全部通过才算完成：

1. 相关 focused .NET 测试通过；
2. 全量 .NET 与 Python 测试、语料验证器继续通过；
3. 实际窗口人工检查菜单无裁切、三档尺寸保持 30 DIP、悬停暂停与左右倾斜可见；
4. 托盘显示/隐藏、共享命令、自启动启用/禁用和退出清理人工通过；
5. 干净目录重新发布，交付仍是一个 EXE、零 DLL；
6. 隔离 smoke verifier 通过且未留下进程、托盘图标或注册表值；
7. README 更新操作、路径移动限制和托盘恢复方式；
8. 更新现有 GitHub 分支和 PR，不覆盖用户工作树中的既有未提交内容。
