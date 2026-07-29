# v1.3.0 authored 语料集成任务面板

日期：2026-07-29
状态：源码、语料、文档与预发布门禁已完成，EXE/Release 发布进行中

## 目标与当前结果

- 运行时语料：30,000 条，全部来自 `data/authored/v1/b001-b100.tsv`
- 来源类型：`curated_authored=30,000`，`legacy_surface_variant=0`
- 语义场景：1,190 个
- 关系画像：`neutral=26,165`、`warm_friend=1,550`、`playful_friend=2,185`、`nickname_easter_egg=100`
- 原始语料审计：75,375 条 disposition；3,265 条 review；1,248 条 PII review
- 来源追溯：30,000 条 authorship ledger，每条绑定 batch、variant、文本哈希、元数据哈希与根哈希
- 运行时资源：EXE 项目直接嵌入 `data/optimized/persona-corpus-v2.tsv`

## Current generated outputs

| File | Data rows | SHA-256 |
| --- | ---: | --- |
| `persona-corpus-v2.tsv` | 30,000 | `1d887627e6b4a8f303a0151cea8b99726d176b9953a396782e88cde69de5633c` |
| `persona-corpus-archive.tsv` | 75,375 | `a9e78adefbeff30e44f88e7eae1a953a8e76c499f29d58d1ec334eb6f75e03bc` |
| `persona-corpus-review.tsv` | 3,265 | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| `pii-review.tsv` | 1,248 | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |
| `persona-surface-manifest.tsv` | 0 | `a2353ed6480ec1e75c40add4ae36ed7884724ca88a64b25f8b1c9282f975037c` |
| `persona-authorship-ledger.tsv` | 30,000 | `31305579ebf55d2d49c3227d7c7664b16e89abd0e9aab3cbbfa11ae3e0cace8d` |

## 已完成

- [x] 契约版本化：运行时范围、精确行数、语义场景数、legacy 行数、source kind
- [x] authored loader、manifest 哈希校验与 30,000 条一对一构建
- [x] v2 TSV 增加 `relationship_profile`，Python/C# 解析和校验同步
- [x] 场景历史加入关系画像配额：warm_friend 最近 20 次最多 2 次；nickname_easter_egg 最近 100 次最多 1 次
- [x] C# 全量测试 641/641 通过
- [x] Python 全量测试 375/375 通过
- [x] 30 天 × 10 seeds 模拟：1,500/1,500 outputs，0 hard violations
- [x] 联合 validator：0 hard errors，0 warnings
- [x] CI 证据合同、发布包装合同与两个 generator `--check` 通过
- [x] README、发布检查清单、审计文档与哈希注册表更新
- [x] canonical outputs 重建并登记行数与 SHA-256

## 待完成

- [ ] Release 配置构建、单文件 EXE 验证与 SHA-256 登记
- [ ] 推送 `v1.3.0` 标签并发布中文 Release（标题仅 `v1.3.0`）
- [ ] 等待 GitHub Actions 全绿
- [ ] 清理多余工作树
