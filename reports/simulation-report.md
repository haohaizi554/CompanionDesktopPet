# Persona Playback Simulation

This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.
The validator-facing event stream is stored separately with an exact schema and input hashes.

## Run contract

| Field | Value |
| --- | --- |
| Schema version | 1 |
| Days per seed | 30 |
| Seeds | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 |
| Corpus SHA-256 | `5f6f1aa3b0f88a1491d3cce54f355c9829513565822463ea4219a54ed536d39d` |
| Scheduler SHA-256 | `539a23ae33c4cd990090e7bcca757e4c143706172f0db557000fb66cd5226831` |

## Approved metrics

| Metric | Value |
| --- | --- |
| 1. Total attempts | 1500 |
| 2. Actual outputs | 1500 |
| 3. Returned None | 0 |
| 4. Average outputs per day per seed | 5.000 |
| 5. Maximum outputs in rolling (now-60m, now] | 1 |
| 8. Technical playback ratio | 15.73% |
| 9. EasterEgg playback ratio | 0.00% |
| 10. user_direct playback ratio | 0.00% |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 45 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care | 0 |
| 16. Average text length | 21.843 |
| 19. Catchphrase line ratio | 0.00% |
| 20. Question/reply outputs | 0 |
| 21. Unmet trigger/context outputs | 0 |
| Hard violations | none |

## 6. category_group playback

| category_group | Count | Ratio |
| --- | --- | --- |
| technical | 236 | 15.73% |
| growth | 119 | 7.93% |
| career | 75 | 5.00% |
| daily_care | 262 | 17.47% |
| emotional_reflection | 90 | 6.00% |
| character_life | 478 | 31.87% |
| easter_egg | 0 | 0.00% |
| system_ambient | 240 | 16.00% |

## 7. output_mode playback

| output_mode | Count | Ratio |
| --- | --- | --- |
| self_talk | 998 | 66.53% |
| ambient | 262 | 17.47% |
| user_direct | 0 | 0.00% |
| system_observe | 240 | 16.00% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 21.93% |
| 17-24 | 36.20% |
| 25-36 | 41.87% |
| >36 | 0.00% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 今天 | 21 |
| 有些 | 20 |
| 下午 | 17 |
| 一天 | 15 |
| 把零 | 15 |
| 时间 | 15 |
| 周末 | 14 |
| 英语 | 14 |
| 上午 | 13 |
| 屏幕 | 13 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 把零食 | 15 |
| 我喜欢 | 13 |
| 旧书页 | 10 |
| 洗好的 | 10 |
| 今天的 | 9 |
| 把闲置 | 9 |
| 注意力 | 9 |
| 练英语 | 9 |
| 上午的 | 8 |
| 天色变 | 8 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 把零食当 | 15 |
| 旧书页边 | 10 |
| 洗好的杯 | 10 |
| 把闲置纸 | 9 |
| 注意力散 | 9 |
| 练英语的 | 9 |
| 天色变暗 | 8 |
| 手腕轻轻 | 8 |
| 擦干净窗 | 8 |
| 看雨水把 | 8 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 把零食当正 | 15 |
| 旧书页边的 | 10 |
| 洗好的杯子 | 10 |
| 把闲置纸盒 | 9 |
| 练英语的时 | 9 |
| 天色变暗以 | 8 |
| 手腕轻轻转 | 8 |
| 擦干净窗边 | 8 |
| 看雨水把远 | 8 |
| 鸡蛋和番茄 | 8 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 把零食当正餐 | 15 |
| 旧书页边的笔 | 10 |
| 洗好的杯子排 | 10 |
| 把闲置纸盒裁 | 9 |
| 练英语的时候 | 9 |
| 天色变暗以后 | 8 |
| 手腕轻轻转一 | 8 |
| 擦干净窗边再 | 8 |
| 看雨水把远处 | 8 |
| 鸡蛋和番茄， | 8 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 更持久。 | 15 |
| 更重要。 | 12 |
| 点颜色。 | 11 |
| 有秩序。 | 10 |
| 的想法。 | 10 |
| 很可爱。 | 9 |
| 成就感。 | 9 |
| 会散些。 | 8 |
| 开边界。 | 8 |
| 式切换。 | 8 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 实感更持久。 | 15 |
| 当时的想法。 | 10 |
| 得很有秩序。 | 10 |
| 很有成就感。 | 9 |
| 省得很可爱。 | 9 |
| 也能成一餐。 | 8 |
| 会松开边界。 | 8 |
| 活模式切换。 | 8 |
| 过一样清爽。 | 8 |
| 酸意会散些。 | 8 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| 的踏实感更持久。 | 15 |
| 会显得很有秩序。 | 10 |
| 发现当时的想法。 | 10 |
| 句就很有成就感。 | 9 |
| 钱也省得很可爱。 | 9 |
| 启动过一样清爽。 | 8 |
| 圈，酸意会散些。 | 8 |
| 往生活模式切换。 | 8 |
| 绪也会松开边界。 | 8 |
| 随手也能成一餐。 | 8 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 带来的踏实感更持久。 | 15 |
| 活也会显得很有秩序。 | 10 |
| 看会发现当时的想法。 | 10 |
| 置，钱也省得很可爱。 | 9 |
| 顺一句就很有成就感。 | 9 |
| 模式往生活模式切换。 | 8 |
| 茄，随手也能成一餐。 | 8 |
| 转一圈，酸意会散些。 | 8 |
| 重新启动过一样清爽。 | 8 |
| ，思绪也会松开边界。 | 8 |

## Catchphrase counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 我丢 | 0 |
| 我靠 | 0 |
| 真的假的 | 0 |
| 啊推 | 0 |
| 小笨蛋 | 0 |
| 我真的不想多说什么了 | 0 |
| 本姑娘 | 0 |
| 玥玥 | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 15.33% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 1 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 2 | 150 | 150 | 0 | 15.33% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 3 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 4 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 5 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 6 | 150 | 150 | 0 | 15.33% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 7 | 150 | 150 | 0 | 15.33% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 8 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |
| 9 | 150 | 150 | 0 | 16.00% | 84.00% | 0.00% | 0.00% | easter_egg_not_observed, user_direct_not_observed |

`easter_egg_not_observed` and `user_direct_not_observed` are transparent non-hard observations. They are not fabricated into the event stream; the current selector and enabled inventory naturally produced zero during this fixed schedule.
