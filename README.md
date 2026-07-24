# 佳怡桌宠（CompanionDesktopPet）

一个完全离线的 Windows x64 WPF 桌宠，以及配套的可审计中文角色语料系统。她不读取输入内容、剪贴板、文件名或窗口标题，也不依赖网络、数据库或在线模型。

> 当前状态：Persona Corpus v2、WPF 离线运行时接入、自包含单文件发布与隔离烟测均已完成。最终交付位于 `outputs/CompanionDesktopPet/`。

左键点击会显示爱心、左右交替轻轻倾斜，并给出一句回复。点击回复有长期运行兜底：即使冷却历史逐渐累积，或从旧版容易陷入静默的本地记忆恢复，后续点击也不会永久失声。桌宠保留自然单次/偶发双次眨眼，启动后会显示一次本地“嗨♡”，也可从右键面板选择 `打个招呼♡`；这些都是纯本地 UI 动作，不由语料驱动。

## 最终交付

```text
outputs/CompanionDesktopPet/佳怡桌宠.exe
outputs/CompanionDesktopPet/使用说明.txt
```

`佳怡桌宠.exe` 是 `win-x64` 自包含单文件应用，运行时不需要安装 .NET，也不依赖外部 DLL、JSON、PDB 或其他运行时 sidecar。

## 体验与操作

- 左键单击人物：显示爱心、按左右方向交替轻轻倾斜，并按当前场景说一句话。
- 按住左键拖动：移动桌宠，移动时按方向倾斜，松手后回弹。
- 气泡与人物之间保持 30 DIP 的视觉距离；鼠标停在人物或气泡上时，只暂停当前气泡剩余的消失倒计时，移开后从剩余时间继续。
- 右键人物：打开卡哇伊风格控制面板，可说句话、`打个招呼♡`、暂停/继续动画、调整大小、切换置顶、设置开机自启动、恢复位置、藏到托盘或退出。
- 托盘：双击图标切换显示/隐藏；右键菜单可显示/隐藏、说句话、暂停/继续、切换开机自启动或退出。

暂停会复位并暂停待机动作、自动眨眼和问候；点击爱心与左右倾斜、拖动/落地、手动说话和托盘操作仍可使用。

## 托盘与窗口恢复

如果托盘图标异常、窗口被藏起后找不到，重新运行同一个 `佳怡桌宠.exe` 即可把已经运行的窗口强制恢复到正常状态并激活；不会再启动一只重复桌宠。正常退出会先冻结交互、尽力保存设置与角色记忆，再清理托盘图标并关闭进程。

## 开机自启动

开机自启动默认关闭，只会在用户主动勾选后写入当前用户注册表：

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
值名：CompanionDesktopPet
```

它不会写入系统级启动项。若勾选后移动或重命名了 EXE，请先关闭“开机自启动”，再重新开启一次，让保存的路径更新为新位置。

本机偏好、冷却历史和微剧情进度保存在 `%LOCALAPPDATA%\CompanionDesktopPet`。

## 已验证能力

- 冻结并校验 75,375 行不可变原始语料证据及字节副本。
- 生成 800 条完整、可独立播放的 v2 启用语料；原始行继续保留在归档、复核和来源映射中。
- 20 列 v2 元数据、严格校验器、确定性离线选择器与 30 天 × 10 seeds 模拟已经接入。
- 选择器执行触发器/上下文、ID/语义冷却、每日上限、最小间隔、滚动小时预算、夜间预算与组配额。
- 点击专用恢复路径通过 8 小时连续会话、900 次连续点击和旧版静默记忆恢复测试；主动播报仍遵守原有静默预算。
- WPF 只嵌入 v2 资源；75,375 行旧语料不进入运行时。
- Release 测试、干净 self-contained single-file publish、源/副本 SHA-256、固定种子重建和隔离单 EXE 烟测作为最终门禁。

完整语料维护契约、20 字段说明和精确命令见 [README-persona-corpus.md](README-persona-corpus.md)。

## 目录

```text
src/CompanionDesktopPet/       WPF 桌宠
src/persona_corpus/            离线语料流水线、选择器与模拟器
data/source/                   不可变原始语料副本
data/intermediate/             可追溯抽取产物与来源映射
data/optimized/                v2、归档与人工复核 TSV
config/                        调度配置与精确复核白名单
reports/                       审计、改写、人工复核与模拟报告
scripts/                       发布隔离验证脚本
tools/                         语料命令行入口
tests/                         Python、PowerShell 与 .NET 测试
outputs/CompanionDesktopPet/   最终交付
```

## 环境

- Windows x64
- .NET 9 SDK
- Python 3.11 或更高版本（只使用标准库）

运行最终 EXE 不需要 Python 或 .NET SDK；这些工具只用于源码验证与重新构建。

## 新鲜验证

```powershell
python -m unittest discover -s tests -v

python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json

python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 10 `
  --report reports/simulation-report.md

dotnet restore CompanionDesktopPet.sln -r win-x64
dotnet test CompanionDesktopPet.sln -c Release --no-restore
```

校验器合格输出是 `Validation: 0 hard errors, 0 warnings`；模拟必须为零硬约束违规。

## 干净发布与隔离烟测

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

验证器要求 `publish/` 只包含 `CompanionDesktopPet.exe`，最终交付目录只包含 `佳怡桌宠.exe` 和明确允许的 `使用说明.txt`；额外 EXE、目录及 DLL/PDB/JSON/TXT 等 sidecar 都会使验证失败。它还会核对 publish 与交付 EXE 的 SHA-256。身份彩蛋只有在精确列入 editorial manifest 后才可进入运行时语料；安全边界由应用启动时对 `PersonaCorpus` exact editorial manifest 的自校验、Python validator 和程序集测试共同承担，不再使用无法区分已批准内容与泄露的 EXE 原始字节 marker 扫描。随后验证器把 EXE 单独复制到 `outputs/verify/`，以 `--smoke-test` 启动并只跟踪本次 PID；只有应用在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束才算成功。超时后的强制终止仅用于清理且仍判失败，非零退出同样失败。

## 数据、隐私与限制

- `src/CompanionDesktopPet/Assets/persona-corpus.tsv` 与 `data/source/persona-corpus.original.tsv` 是不可变审计证据，不原地覆盖。
- 禁用内容进入 archive；不确定内容与 PII 进入 review；改写内容保留来源引用和原因。
- 身份彩蛋只有精确列入 editorial manifest 后才可进入 `PersonaCorpus`；应用启动自校验该 exact manifest，Python validator 和程序集测试共同阻止未审批内容进入运行时。
- IDE 前台、连续活跃、空闲返回和全屏等未来信号默认未知，不猜测用户状态。
- 自动检查不能替代人物授权、虚构身份、关系边界和再分发权利的人工审批。

## 许可

仓库暂未声明开源许可证。在明确人物素材、角色内容和再分发权利前，请勿公开再发布素材、语料或构建产物。
