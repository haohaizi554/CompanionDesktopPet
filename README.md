# 佳怡桌宠（CompanionDesktopPet）

一个完全离线的 Windows WPF 桌宠，以及配套的可审计中文角色语料系统。

桌宠使用透明人物素材，交互方向保留点击爱心、拖拽倾斜和松手回弹；眨眼与打招呼动作不在目标版本中。角色播报不读取输入内容、剪贴板、文件名或窗口标题，也不依赖网络和在线模型。

> 当前状态：语料 v2、离线选择器、严格校验器和 30 天模拟已完成；WPF 运行时接入与单文件 EXE 发布仍在进行中。本仓库现阶段是可验证的开发版本，不应冒充最终发布包。

## 已完成

- 冻结并校验 75,375 行原始语料，保留字节级 SHA-256 基线。
- 生成 800 条可独立播放的 v2 启用语料，同时保留 75,375 条归档记录、3,265 条人工复核记录和 1,248 条 PII 复核记录。
- 提供纯 Python 3.11 标准库实现的审计、抽取、构建、校验、上下文、历史、选择和模拟工具。
- 使用本地时间、日期、周末、节日、纪念日与长静默等可证明信号；未来 IDE/活跃/全屏信号默认可空，不猜测用户状态。
- 选择器执行 ID/语义冷却、每日上限、滚动小时预算、夜间预算、技术内容与彩蛋配额，并使用局部固定随机种子保证可复现。
- 30 天 × 10 seeds 模拟产生 1,500 次输出：technical 15.73%，`self_talk + ambient` 84%，硬约束违规 0。
- 结构化模拟事件由独立校验器重新计算，当前结果为 `0 hard errors, 0 warnings`。

## 目录

```text
src/CompanionDesktopPet/       WPF 桌宠
src/persona_corpus/            离线语料流水线、选择器与模拟器
data/source/                   不可变原始语料副本
data/intermediate/             可追溯的抽取中间产物
data/optimized/                v2、归档与人工复核 TSV
config/                        调度配置与精确复核白名单
reports/                       before/after、改写、人工复核和模拟报告
tools/                         命令行入口
tests/                         Python 与 .NET 测试
docs/superpowers/              设计说明与实施计划
```

## 环境

- Windows x64
- .NET 9 SDK
- Python 3.11 或更高版本（只使用标准库）

不需要网络服务、数据库、模型 API 或第三方 Python 包。

## 验证语料

```powershell
python -m unittest discover -s tests -p "test_*.py"

python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json
```

预期校验结果：

```text
Validation: 0 hard errors, 0 warnings
```

重新生成确定性模拟和报告：

```powershell
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 10 `
  --report reports/simulation-report.md
```

`--seeds 10` 明确定义为 seeds `0..9`。相同输入会生成字节一致的事件 JSON 与 Markdown 报告。

## 构建桌宠

```powershell
dotnet test CompanionDesktopPet.sln -c Release
dotnet build CompanionDesktopPet.sln -c Release
```

最终目标是一个自包含的 `win-x64` 单文件 EXE，运行目录不需要旁置 DLL。发布验证会在 WPF v2 接入完成后纳入正式交付。

## 数据与隐私

- `data/source/persona-corpus.original.tsv` 是不可变审计输入，不原地覆盖。
- 禁用内容进入 archive；不确定内容进入 review；改写内容保留来源映射。
- 真实姓名、湖南/广东经历、收入与打零工经历、亲昵称呼等内容默认禁用，并列入人工确认报告。
- 自动化不会替代人物授权、虚构身份或默认关系边界的人工判断。

详见：

- `reports/corpus-audit-after.md`
- `reports/corpus-rewrite-summary.md`
- `reports/corpus-manual-review.md`
- `reports/simulation-report.md`

## 当前路线图

- [x] 原始语料审计与不可变基线
- [x] v2 精选语料、归档、复核和 PII 报告
- [x] 严格校验器与确定性离线选择器
- [x] 30 天多 seed 模拟和独立事件复算
- [ ] 将 v2 元数据与选择约束接入 WPF 桌宠
- [ ] 保留爱心与倾斜，移除眨眼和打招呼路径
- [ ] 发布并烟测无旁置 DLL 的单文件 EXE

## 许可

仓库暂未声明开源许可证。在明确人物素材、角色内容和再分发权利前，请勿将素材或语料用于公开再发布。
