# v1.3.0 authored 语料集成任务面板

日期：2026-07-29
状态：全部完成；源码、语料、文档、正式 EXE、GitHub Release、线上 CI 与工作树清理均已验收

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
| `persona-corpus-v2.tsv` | 82,132 | `339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece` |
| `persona-corpus-archive.tsv` | 75,375 | `b7d9a5f2fd6f4750ea2b688206f77bf45a2b59ca12c09f36281c72efc620721d` |
| `persona-corpus-review.tsv` | 3,265 | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| `pii-review.tsv` | 1,248 | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |
| `persona-surface-manifest.tsv` | 51,326 | `bcf9c97be0e4b1d7b7db11fcb46f44de17ef0ade6cb2e79d69f8af69bdbc637d` |
| `persona-authorship-ledger.tsv` | 30,000 | `31305579ebf55d2d49c3227d7c7664b16e89abd0e9aab3cbbfa11ae3e0cace8d` |

## 正式发布结果

- GitHub Release：[v1.3.0](https://github.com/haohaizi554/CompanionDesktopPet/releases/tag/v1.3.0)，标题精确为 `v1.3.0`
- source commit：`20fcbe2051e4d1c0f382a59aab5f30b22b8462f5`
- 正式 EXE：79,112,877 字节，SHA-256 `9d20f5a546d10c65ac5b65558dbcc722f96ceef5e63fb89484dcb69bc420d5e6`
- 正式 ZIP：79,127,137 字节，SHA-256 `1ea1799d4e2cdfb13703ad6b815557a83c704da2cc1835f67280be7ed8f7907e`
- CI：main run `30450632805` 与 tag run `30450700771` 均成功；云端 smoke PID `1952`、本地回下载 smoke PID `32112`，均 `ExitCode=0`

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
- [x] Release 配置构建、单文件 EXE 验证与 SHA-256 登记
- [x] 推送 annotated `v1.3.0` 标签并发布具体中文 Release，标题仅 `v1.3.0`
- [x] GitHub Actions main/tag 流水线全绿，8 项资产代理回下载复验

## 收尾

- [x] 执行 `git worktree prune` 并核对注册表与 `D:\desktop`：仅保留当前 `D:\desktop\CompanionDesktopPet`，无多余 `CompanionDesktopPet-*` 工作树目录
