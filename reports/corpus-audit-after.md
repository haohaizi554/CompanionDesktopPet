# Persona Corpus Audit After

The inventory share and the simulated playback share are deliberately separate. The curated file contains technical coverage for traceability; the selector controls what is actually played.

## Before/after comparison

| Metric | Legacy source | Curated v2 |
| --- | --- | --- |
| Total corpus rows | 75375 | 52132 |
| Enabled rows | n/a | 52132 |
| Archive rows | 0 | 75375 |
| Review rows | 0 | 3265 |
| Exact duplicate texts | 0 | 0 |
| Normalized duplicate texts | 0 | 0 |
| Average text length | 31.817 | 31.810 |
| Length <8 | 0.00% | 0.00% |
| Length 8-16 | 0.03% | 0.46% |
| Length 17-24 | 1.91% | 1.33% |
| Length 25-36 | 88.08% | 88.57% |
| Length >36 | 9.99% | 9.65% |
| Question texts | 8580 | 0 |
| Fake-context heuristic hits | 5265 | 0 |
| self_talk inventory ratio | n/a | 98.24% |
| ambient inventory ratio | n/a | 1.49% |
| user_direct inventory ratio | n/a | 0.00% |
| system_observe inventory ratio | n/a | 0.27% |
| Technical enabled-inventory ratio | n/a | 70.97% |
| Technical simulated-playback ratio | n/a | 18.47% |
| Catchphrase line ratio | 28.58% | 25.92% |
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
| v2 enabled | 先别 | 2662 |
| v2 enabled | 我看 | 2662 |
| v2 enabled | 真的 | 2655 |

## Frequent endings

| Corpus | 4-character ending | Count |
| --- | --- | --- |
| Legacy | 列出来。 | 3601 |
| Legacy | 不催你。 | 3600 |
| Legacy | 么吓人。 | 3600 |
| Legacy | 别又忘。 | 3600 |
| Legacy | 动代码。 | 3600 |
| v2 enabled | 就重写。 | 2851 |
| v2 enabled | 它钉住。 | 2842 |
| v2 enabled | 补靠谱。 | 2842 |
| v2 enabled | 通再说。 | 2842 |
| v2 enabled | 动代码。 | 2831 |
