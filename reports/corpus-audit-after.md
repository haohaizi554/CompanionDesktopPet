# Persona Corpus Audit After

The inventory share and the simulated playback share are deliberately separate. The curated file contains technical coverage for traceability; the selector controls what is actually played.

## Before/after comparison

| Metric | Legacy source | Curated v2 |
| --- | --- | --- |
| Total corpus rows | 75375 | 82132 |
| Enabled rows | n/a | 82132 |
| Archive rows | 0 | 75375 |
| Review rows | 0 | 3265 |
| Exact duplicate texts | 0 | 0 |
| Normalized duplicate texts | 0 | 0 |
| Average text length | 31.817 | 28.881 |
| Length <8 | 0.00% | 0.00% |
| Length 8-16 | 0.03% | 3.05% |
| Length 17-24 | 1.91% | 21.89% |
| Length 25-36 | 88.08% | 67.40% |
| Length >36 | 9.99% | 7.65% |
| Question texts | 8580 | 0 |
| Fake-context heuristic hits | 5265 | 0 |
| self_talk inventory ratio | n/a | 92.31% |
| ambient inventory ratio | n/a | 4.60% |
| user_direct inventory ratio | n/a | 0.00% |
| system_observe inventory ratio | n/a | 3.09% |
| Technical enabled-inventory ratio | n/a | 51.62% |
| Technical simulated-playback ratio | n/a | 17.75% |
| Catchphrase line ratio | 28.58% | 16.48% |
| PII review rows | 0 | 1248 |

## Frequent openings

| Corpus | 2-character opening | Count |
| --- | --- | --- |
| Legacy | 嗯， | 3316 |
| Legacy | 哈？ | 3315 |
| Legacy | 啊推 | 3315 |
| Legacy | 我丢 | 3315 |
| Legacy | 先别 | 2925 |
| v2 enabled | 啊推 | 2833 |
| v2 enabled | 我丢 | 2833 |
| v2 enabled | 我看 | 2752 |
| v2 enabled | 先把 | 2684 |
| v2 enabled | 先别 | 2665 |

## Frequent endings

| Corpus | 4-character ending | Count |
| --- | --- | --- |
| Legacy | 列出来。 | 3601 |
| Legacy | 不催你。 | 3600 |
| Legacy | 么吓人。 | 3600 |
| Legacy | 别又忘。 | 3600 |
| Legacy | 动代码。 | 3600 |
| v2 enabled | 就重写。 | 2851 |
| v2 enabled | 它钉住。 | 2843 |
| v2 enabled | 补靠谱。 | 2842 |
| v2 enabled | 通再说。 | 2842 |
| v2 enabled | 动代码。 | 2832 |


## v1.7.0 增补

2026-10-05：上面的正文保持原样。v1.7.0 在桌面版之外增加 Android 浮窗 com.jiayi.companionpet，显示版本 1.7.0。对话步骤与桌面 agent 对齐并打包进 APK，语音仍由这台电脑的显卡经 http://43.138.138.200:8765 合成。运行库仍是 82,132 条、1,723 个语义场景，SHA-256 仍为 339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece。Windows 安装包仍是 v1.6.0。
