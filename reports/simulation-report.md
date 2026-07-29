# Persona Playback Simulation

This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.
The validator-facing event stream is stored separately with an exact schema and input hashes.

## Run contract

| Field | Value |
| --- | --- |
| Schema version | 3 |
| Days per seed | 30 |
| Seeds | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 |
| Corpus SHA-256 | `1d887627e6b4a8f303a0151cea8b99726d176b9953a396782e88cde69de5633c` |
| Scheduler SHA-256 | `318a2e4b92e56b02de950e8fd627fb34279dab4237148328e88cc0720f8a7f03` |
| Subseed derivation | persona-simulation-v2 |
| Subseed derivation SHA-256 | `e5f6d36ffb5d4936bccca24cb9c7177a63e02d937118342916bd5eea0a83640d` |
| Distribution tolerance | 5.00% |

## Approved metrics

| Metric | Value |
| --- | --- |
| 1. Total attempts | 1500 |
| 2. Actual outputs | 1500 |
| 3. Returned None | 0 |
| 4. Average outputs per day per seed | 5.000 |
| Natural minimum output interval (minutes) | 140.000 |
| 5. Maximum outputs in rolling (now-60m, now] | 1 |
| Natural late-night maximum in rolling (now-60m, now] | 1 |
| Natural blocked adjacent groups | daily_care=0, easter_egg=0, emotional_reflection=0, technical=0 |
| 8. Technical playback ratio | 17.87% |
| 9. EasterEgg playback ratio | 9.80% |
| 10. user_direct playback ratio | 0.00% |
| dry_sharp playback ratio | 2.00% |
| dry_sharp recent-window violations | 0 |
| dry_sharp forbidden metadata hits | 0 |
| seasoning playback ratio | 0.60% |
| seasoning recent-window violations | 0 |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 54 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care (including cross-group pairs) | 49 |
| 16. Average text length | 23.851 |
| 19. Seasoning line ratio | 0.60% |
| 20. Question/reply outputs | 0 |
| 21. Unmet trigger/context outputs | 0 |
| Natural hard violations | none |
| Adversarial hard violations | none |
| Combined hard violations | none |

## Scheduler-derived distribution contract

| Metric | Target | Minimum | Maximum |
| --- | --- | --- | --- |
| technical | 18.00% | 10.00% | 20.00% |
| easter_egg | 10.00% | 8.00% | 12.00% |
| self_talk + ambient | 92.00% | 65.00% | 97.00% |
| user_direct | 0.00% | 0.00% | 15.00% |

## dry_sharp contract evidence

| Metric | Observed | Target | Acceptance | Enforced |
| --- | --- | --- | --- | --- |
| dry_sharp scene inventory | 8/1190 (0.67%) | 0.70% | 0.60%–0.80% | yes |
| dry_sharp row inventory observation | 200/30000 (0.67%) | observation only | n/a | no |
| dry_sharp playback | 30/1500 (2.00%) | 1.00% | 0.00%–4.00% | yes |

Bootstrap scene gap: no (minimum 4 scenes).
Recent playback limit: at most 1 dry_sharp line(s) in the latest 20 outputs.

## Seasoning lexical exposure evidence

| Metric | Observed | Acceptance / policy |
| --- | --- | --- |
| expanded_runtime inventory observation | 18/30000 (0.06%) | observation_only |
| seasoning playback | 0.60% | 0.50%–1.50% |
| seasoning recent window | 0 | max 1 in 20 |

## Scenario and inventory coverage

| Coverage | Value |
| --- | --- |
| Seasons | spring, summer, autumn, winter |
| Dayparts | late_night, morning, noon, afternoon, evening |
| Dawn | True |
| Events | tick, app_start, day_changed |
| Weekday + weekend | False, True |
| Holiday | True |
| Anniversary | True |
| Month boundary | True |
| Nullable signal combinations | 108 |
| Inventory trigger misses | none |
| Inventory context misses | none |
| Unreachable trigger/context pairs | none |

## Adversarial selector and analyzer evidence

| Case | Selector decision | Expected | Analyzer codes | Status |
| --- | --- | --- | --- | --- |
| minimum_interval:7m59s:reject | rejected | rejected | interrupt_budget_violation, minimum_interval_violation | pass |
| minimum_interval:8m00s:allow | selected | selected | none | pass |
| interrupt_cost:1:12m:reject_below | rejected | rejected | interrupt_budget_violation | pass |
| interrupt_cost:1:12m:allow_exact | selected | selected | none | pass |
| interrupt_cost:2:16m:reject_below | rejected | rejected | interrupt_budget_violation | pass |
| interrupt_cost:2:16m:allow_exact | selected | selected | none | pass |
| interrupt_cost:3:24m:reject_below | rejected | rejected | interrupt_budget_violation | pass |
| interrupt_cost:3:24m:allow_exact | selected | selected | none | pass |
| interrupt_cost:4:40m:reject_below | rejected | rejected | interrupt_budget_violation | pass |
| interrupt_cost:4:40m:allow_exact | selected | selected | none | pass |
| interrupt_cost:5:60m:reject_below | rejected | rejected | interrupt_budget_violation | pass |
| interrupt_cost:5:60m:allow_exact | selected | selected | none | pass |
| rolling_hour:max:reject | rejected | rejected | hourly_budget_violation | pass |
| rolling_hour:max:allow | selected | selected | none | pass |
| late_night:max:reject | rejected | rejected | late_night_budget_violation | pass |
| late_night:max:allow | selected | selected | none | pass |
| adjacent_group:daily_care:reject | rejected | rejected | adjacent_group_violation:daily_care | pass |
| adjacent_group:daily_care:allow_different_previous | selected | selected | none | pass |
| adjacent_group:easter_egg:reject | rejected | rejected | adjacent_group_violation:easter_egg, recent_easter_egg_violation | pass |
| adjacent_group:easter_egg:allow_different_previous | selected | selected | none | pass |
| adjacent_group:emotional_reflection:reject | rejected | rejected | adjacent_group_violation:emotional_reflection | pass |
| adjacent_group:emotional_reflection:allow_different_previous | selected | selected | none | pass |
| adjacent_group:technical:reject | rejected | rejected | adjacent_group_violation:technical | pass |
| adjacent_group:technical:allow_different_previous | selected | selected | none | pass |
| max_per_day:reject | rejected | rejected | adjacent_semantic_violation, max_per_day_violation | pass |
| recent:technical:reject | rejected | rejected | adjacent_group_violation:technical, recent_technical_violation | pass |
| recent:user_direct:reject | rejected | rejected | recent_user_direct_violation | pass |
| recent:easter_egg:reject | rejected | rejected | recent_easter_egg_violation | pass |

## 6. category_group playback

| category_group | Count | Ratio |
| --- | --- | --- |
| technical | 268 | 17.87% |
| growth | 151 | 10.07% |
| career | 103 | 6.87% |
| daily_care | 154 | 10.27% |
| emotional_reflection | 154 | 10.27% |
| character_life | 397 | 26.47% |
| easter_egg | 147 | 9.80% |
| system_ambient | 126 | 8.40% |

## 7. output_mode playback

| output_mode | Count | Ratio |
| --- | --- | --- |
| self_talk | 1220 | 81.33% |
| ambient | 154 | 10.27% |
| user_direct | 0 | 0.00% |
| system_observe | 126 | 8.40% |

## Tone playback

| tone | Count | Ratio |
| --- | --- | --- |
| calm | 249 | 16.60% |
| curious | 100 | 6.67% |
| dry | 296 | 19.73% |
| dry_sharp | 30 | 2.00% |
| encouraging | 47 | 3.13% |
| gentle | 429 | 28.60% |
| intimate | 22 | 1.47% |
| nostalgic | 65 | 4.33% |
| playful | 205 | 13.67% |
| serious | 37 | 2.47% |
| sleepy | 20 | 1.33% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 7.33% |
| 17-24 | 57.73% |
| 25-36 | 30.40% |
| >36 | 4.53% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 我把 | 21 |
| 一个 | 15 |
| 一条 | 11 |
| 一次 | 11 |
| 一段 | 11 |
| 每次 | 11 |
| 一张 | 9 |
| 英文 | 9 |
| 技术 | 8 |
| 测试 | 8 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 雷琳玥 | 6 |
| 把临时 | 4 |
| 把喜欢 | 4 |
| 每次把 | 4 |
| 给自己 | 4 |
| 一颗小 | 3 |
| 临时补 | 3 |
| 保留一 | 3 |
| 分支名 | 3 |
| 可靠的 | 3 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 把喜欢的 | 4 |
| 雷琳玥把 | 4 |
| 临时补丁 | 3 |
| 标签不需 | 3 |
| 看一眼房 | 3 |
| 维护一份 | 3 |
| 一块布折 | 2 |
| 一次只替 | 2 |
| 一段项目 | 2 |
| 一颗小心 | 2 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 临时补丁叠 | 3 |
| 标签不需要 | 3 |
| 看一眼房间 | 3 |
| 维护一份简 | 3 |
| 一块布折好 | 2 |
| 一次只替一 | 2 |
| 一段项目视 | 2 |
| 一颗小心停 | 2 |
| 三个快捷键 | 2 |
| 书签背面写 | 2 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 临时补丁叠太 | 3 |
| 标签不需要文 | 3 |
| 维护一份简洁 | 3 |
| 一块布折好又 | 2 |
| 一次只替一个 | 2 |
| 一段项目视频 | 2 |
| 一颗小心停在 | 2 |
| 三个快捷键同 | 2 |
| 书签背面写着 | 2 |
| 保留一点试验 | 2 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 更清楚。 | 11 |
| 会更稳。 | 6 |
| 很安静。 | 6 |
| 很清楚。 | 6 |
| 的路径。 | 6 |
| 很利落。 | 5 |
| 更可靠。 | 5 |
| 更重要。 | 5 |
| 的位置。 | 5 |
| 更从容。 | 4 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 显得很清楚。 | 4 |
| 会慢慢退后。 | 3 |
| 张迷宫门票。 | 3 |
| 更容易对齐。 | 3 |
| 经很讲道理。 | 3 |
| 一点呼吸感。 | 2 |
| 一眼能看清。 | 2 |
| 不是一回事。 | 2 |
| 业感的地基。 | 2 |
| 个自然断点。 | 2 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| 到一张迷宫门票。 | 3 |
| 就已经很讲道理。 | 3 |
| 度会更容易对齐。 | 3 |
| 一份安静的约定。 | 2 |
| 一声很轻的和弦。 | 2 |
| 一排竖起的小窗。 | 2 |
| 不急着和它较劲。 | 2 |
| 不用翻考古现场。 | 2 |
| 么就一眼能看清。 | 2 |
| 习惯会慢慢显影。 | 2 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 会收到一张迷宫门票。 | 3 |
| 出来就已经很讲道理。 | 3 |
| 际进度会更容易对齐。 | 3 |
| 一步都可能改变结果。 | 2 |
| 么时候开始偏离正常。 | 2 |
| 使用者只能练习读心。 | 2 |
| 便换了一件外套回来。 | 2 |
| 像一张带机关的地图。 | 2 |
| 可以顺手省掉的注脚。 | 2 |
| 和桌边都会热闹一点。 | 2 |

## Seasoning marker counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 你认真的？ | 0 |
| 真的假的 | 0 |
| 啊推 | 0 |
| 我靠 | 0 |
| 我丢 | 0 |
| 我真的不想多说什么了 | 0 |
| 嗯嗯 | 5 |
| 嘿嘿 | 0 |
| 笨蛋 | 0 |
| 小笨蛋 | 0 |
| 本姑娘 | 0 |
| 哼 | 2 |
| 6 | 2 |
| 666 | 0 |
| NB | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | dry_sharp | seasoning | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |
| 1 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 9.33% | 2.00% | 1.33% | user_direct_not_observed |
| 2 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |
| 3 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |
| 4 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 9.33% | 2.00% | 0.67% | user_direct_not_observed |
| 5 | 150 | 150 | 0 | 17.33% | 92.00% | 0.00% | 10.00% | 2.00% | 0.00% | seasoning_not_observed, seasoning_ratio_below_minimum, user_direct_not_observed |
| 6 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 9.33% | 2.00% | 0.00% | seasoning_not_observed, seasoning_ratio_below_minimum, user_direct_not_observed |
| 7 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |
| 8 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |
| 9 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 2.00% | 0.67% | user_direct_not_observed |

`easter_egg_not_observed`, `user_direct_not_observed`, `dry_sharp_not_observed`, and `seasoning_not_observed` are transparent non-hard observations. They are not fabricated into the event stream.
