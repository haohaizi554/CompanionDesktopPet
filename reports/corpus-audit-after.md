# Persona Corpus Audit After

The inventory share and the simulated playback share are deliberately separate. The curated file contains technical coverage for traceability; the selector controls what is actually played.

## Before/after comparison

| Metric | Legacy source | Curated v2 |
| --- | --- | --- |
| Total corpus rows | 75375 | 800 |
| Enabled rows | n/a | 800 |
| Archive rows | 0 | 75375 |
| Review rows | 0 | 3265 |
| Exact duplicate texts | 0 | 0 |
| Normalized duplicate texts | 0 | 0 |
| Average text length | 31.817 | 20.802 |
| Length <8 | 0.00% | 0.00% |
| Length 8-16 | 0.03% | 27.00% |
| Length 17-24 | 1.91% | 44.00% |
| Length 25-36 | 88.08% | 29.00% |
| Length >36 | 9.99% | 0.00% |
| Question texts | 8580 | 0 |
| Fake-context heuristic hits | 3445 | 0 |
| self_talk inventory ratio | n/a | 73.12% |
| ambient inventory ratio | n/a | 9.38% |
| user_direct inventory ratio | n/a | 0.00% |
| system_observe inventory ratio | n/a | 17.50% |
| Technical enabled-inventory ratio | n/a | 41.25% |
| Technical simulated-playback ratio | n/a | 15.73% |
| Catchphrase line ratio | 23.84% | 0.00% |
| PII review rows | 0 | 1248 |

## Frequent openings

| Corpus | 2-character opening | Count |
| --- | --- | --- |
| Legacy | 嗯， | 3316 |
| Legacy | 哈？ | 3315 |
| Legacy | 啊推 | 3315 |
| Legacy | 我丢 | 3315 |
| Legacy | 先别 | 2925 |
| v2 enabled | 日历 | 15 |
| v2 enabled | 季节 | 12 |
| v2 enabled | 今天 | 11 |
| v2 enabled | 接口 | 9 |
| v2 enabled | 时间 | 8 |

## Frequent endings

| Corpus | 4-character ending | Count |
| --- | --- | --- |
| Legacy | 列出来。 | 3601 |
| Legacy | 不催你。 | 3600 |
| Legacy | 么吓人。 | 3600 |
| Legacy | 别又忘。 | 3600 |
| Legacy | 动代码。 | 3600 |
| v2 enabled | 更重要。 | 5 |
| v2 enabled | 很合理。 | 3 |
| v2 enabled | 一会儿。 | 2 |
| v2 enabled | 一起看。 | 2 |
| v2 enabled | 了什么。 | 2 |
