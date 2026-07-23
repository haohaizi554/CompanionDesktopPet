# 佳怡桌宠（CompanionDesktopPet）

一个完全离线的 Windows x64 WPF 桌宠，以及配套的可审计中文角色语料系统。

> 当前状态：Persona Corpus v2、WPF 离线运行时接入、自包含单文件发布与隔离烟测均已完成。最终交付位于 `outputs/CompanionDesktopPet/`。

桌宠保留左键点击爱心、拖拽倾斜和松手落地回弹；不包含眨眼、wink、挥手、旧 `GetGreeting` 或打招呼动画。它不读取输入内容、剪贴板、文件名或窗口标题，也不依赖网络、数据库或在线模型。

## 最终交付

```text
outputs/CompanionDesktopPet/佳怡桌宠.exe
outputs/CompanionDesktopPet/使用说明.txt
```

`佳怡桌宠.exe` 是 `win-x64` 自包含单文件应用，运行时不需要安装 .NET，也不依赖旁置 DLL。

## 操作

- 左键单击：显示爱心，并按当前场景说一句话或安静做动作。
- 按住左键拖动：移动桌宠，移动时倾斜，松手后回弹。
- 右键人物：说句话、暂停/继续、调整大小、切换置顶、恢复位置或退出。

本机偏好、冷却历史和微剧情进度保存在 `%LOCALAPPDATA%\CompanionDesktopPet`。

## 已验证能力

- 冻结并校验 75,375 行不可变原始语料证据及字节副本。
- 生成 800 条完整、可独立播放的 v2 启用语料；原始行继续保留在归档、复核和来源映射中。
- 20 列 v2 元数据、严格校验器、确定性离线选择器与 30 天 × 10 seeds 模拟已经接入。
- 选择器执行触发器/上下文、ID/语义冷却、每日上限、最小间隔、滚动小时预算、夜间预算与组配额。
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
  -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe
```

验证器拒绝额外 EXE 和 DLL/PDB/JSON 等运行时 sidecar，核对 publish 与交付 EXE 的 SHA-256，并扫描最终 EXE 原始字节中的 UTF-8/UTF-16 直接身份标记。地区、收入等通用词可能合法存在于自包含 .NET/ICU 词典中，因此由应用程序集测试和 v2 语料门禁检查，而不对整个运行时包做易误报的单词扫描。随后验证器把 EXE 单独复制到 `outputs/verify/`，以 `--smoke-test` 启动并只跟踪本次 PID；只有应用在时限内完成真实 WPF 资源与启动气泡初始化、正常关闭并自行以退出码 0 结束才算成功。超时后的强制终止仅用于清理且仍判失败，非零退出同样失败。

## 数据、隐私与限制

- `src/CompanionDesktopPet/Assets/persona-corpus.tsv` 与 `data/source/persona-corpus.original.tsv` 是不可变审计证据，不原地覆盖。
- 禁用内容进入 archive；不确定内容与 PII 进入 review；改写内容保留来源引用和原因。
- 具体 PII marker 不编入运行时程序集；安全性由语料构建/测试门禁及最终 EXE UTF-8/UTF-16 原始字节扫描共同保证。
- IDE 前台、连续活跃、空闲返回和全屏等未来信号默认未知，不猜测用户状态。
- 自动检查不能替代人物授权、虚构身份、关系边界和再分发权利的人工审批。

## 许可

仓库暂未声明开源许可证。在明确人物素材、角色内容和再分发权利前，请勿公开再发布素材、语料或构建产物。
