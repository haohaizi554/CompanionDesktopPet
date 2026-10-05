# Persona Playback Simulation

This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.
The validator-facing event stream is stored separately with an exact schema and input hashes.

## Run contract

| Field | Value |
| --- | --- |
| Schema version | 3 |
| Days per seed | 30 |
| Seeds | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99 |
| Corpus SHA-256 | `339358c524785db30badf420a3bdc2b89c7753486e907ff1a5216f68ca5d7ece` |
| Scheduler SHA-256 | `eedc8979fb239a915789af4ff62d55b31a2aeabde3f196dabd7355e73f666f2a` |
| Subseed derivation | persona-simulation-v2 |
| Subseed derivation SHA-256 | `e5f6d36ffb5d4936bccca24cb9c7177a63e02d937118342916bd5eea0a83640d` |
| Distribution tolerance | 5.00% |

## Approved metrics

| Metric | Value |
| --- | --- |
| 1. Total attempts | 15000 |
| 2. Actual outputs | 15000 |
| 3. Returned None | 0 |
| 4. Average outputs per day per seed | 5.000 |
| Natural minimum output interval (minutes) | 140.000 |
| 5. Maximum outputs in rolling (now-60m, now] | 1 |
| Natural late-night maximum in rolling (now-60m, now] | 1 |
| Natural blocked adjacent groups | daily_care=0, easter_egg=0, emotional_reflection=0, technical=0 |
| 8. Technical playback ratio | 17.10% |
| 9. EasterEgg playback ratio | 10.00% |
| 10. user_direct playback ratio | 0.00% |
| dry_sharp playback ratio | 2.00% |
| dry_sharp recent-window violations | 0 |
| dry_sharp forbidden metadata hits | 0 |
| seasoning playback ratio | 1.33% |
| legacy source-tier playback ratio | 30.62% |
| seasoning recent-window violations | 0 |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 455 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care (including cross-group pairs) | 366 |
| 16. Average text length | 25.020 |
| 19. Seasoning line ratio | 1.33% |
| 20. Question/reply outputs | 0 |
| 21. Unmet trigger/context outputs | 0 |
| Natural hard violations | none |
| Adversarial hard violations | none |
| Combined hard violations | none |

## Source-tier playback

| Source tier | Selections | Ratio |
| --- | --- | --- |
| authored | 10407 | 69.38% |
| legacy | 4593 | 30.62% |

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
| dry_sharp scene inventory | 12/1723 (0.70%) | 0.70% | 0.60%–0.80% | yes |
| dry_sharp row inventory observation | 648/82132 (0.79%) | observation only | n/a | no |
| dry_sharp playback | 300/15000 (2.00%) | 1.00% | 0.00%–4.00% | yes |

Bootstrap scene gap: no (minimum 4 scenes).
Recent playback limit: at most 1 dry_sharp line(s) in the latest 20 outputs.

## Seasoning lexical exposure evidence

| Metric | Observed | Acceptance / policy |
| --- | --- | --- |
| expanded_runtime inventory observation | 13533/82132 (16.48%) | observation_only |
| seasoning playback | 1.33% | 0.50%–1.50% |
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
| technical | 2565 | 17.10% |
| growth | 1497 | 9.98% |
| career | 1194 | 7.96% |
| daily_care | 1504 | 10.03% |
| emotional_reflection | 1536 | 10.24% |
| character_life | 3838 | 25.59% |
| easter_egg | 1500 | 10.00% |
| system_ambient | 1366 | 9.11% |

## 7. output_mode playback

| output_mode | Count | Ratio |
| --- | --- | --- |
| self_talk | 12130 | 80.87% |
| ambient | 1504 | 10.03% |
| user_direct | 0 | 0.00% |
| system_observe | 1366 | 9.11% |

## Tone playback

| tone | Count | Ratio |
| --- | --- | --- |
| calm | 1828 | 12.19% |
| curious | 584 | 3.89% |
| dry | 3812 | 25.41% |
| dry_sharp | 300 | 2.00% |
| encouraging | 381 | 2.54% |
| gentle | 4469 | 29.79% |
| intimate | 104 | 0.69% |
| nostalgic | 1087 | 7.25% |
| playful | 2033 | 13.55% |
| serious | 198 | 1.32% |
| sleepy | 204 | 1.36% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 6.35% |
| 17-24 | 48.97% |
| 25-36 | 40.67% |
| >36 | 4.00% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 我看 | 208 |
| 嗯， | 207 |
| 先把 | 169 |
| 先别 | 153 |
| 讲真 | 151 |
| 我把 | 131 |
| 这事 | 117 |
| 你先 | 98 |
| 行啦 | 98 |
| 慢慢 | 94 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 我看看 | 177 |
| 讲真的 | 151 |
| 你先听 | 98 |
| 行啦， | 98 |
| 先别急 | 91 |
| 我们捋 | 89 |
| 行吧， | 89 |
| 慢慢来 | 88 |
| 先把范 | 87 |
| 按步骤 | 87 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 讲真的， | 151 |
| 你先听我 | 98 |
| 我看看， | 92 |
| 先别急， | 91 |
| 我们捋一 | 89 |
| 慢慢来， | 88 |
| 先把范围 | 87 |
| 按步骤来 | 87 |
| 我看看啊 | 85 |
| 说起来， | 83 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 我们捋一下 | 89 |
| 先把范围缩 | 87 |
| 按步骤来， | 87 |
| 我看看啊， | 85 |
| 按自己的节 | 81 |
| 你先听我说 | 77 |
| 先收集证据 | 76 |
| 我陪你捋， | 75 |
| 先看现象， | 73 |
| 先停一下， | 70 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 我们捋一下， | 89 |
| 先把范围缩小 | 87 |
| 按自己的节奏 | 81 |
| 你先听我说， | 77 |
| 先收集证据， | 76 |
| 先从一小步来 | 69 |
| 今天先啃这一 | 68 |
| 偷偷告诉你， | 68 |
| 先把目标放近 | 65 |
| 我突然想起， | 62 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 就够了。 | 120 |
| 它钉住。 | 102 |
| 就算赚。 | 102 |
| 丢人的。 | 101 |
| 轻一点。 | 100 |
| 慢慢啃。 | 99 |
| 就重写。 | 98 |
| 记下来。 | 96 |
| 补靠谱。 | 95 |
| 谈优雅。 | 92 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 一点就够了。 | 107 |
| 个点就算赚。 | 102 |
| 什么丢人的。 | 101 |
| 试把它钉住。 | 100 |
| ，你慢慢啃。 | 99 |
| 上来就重写。 | 98 |
| 原因记下来。 | 96 |
| 你脑补靠谱。 | 95 |
| ，再谈优雅。 | 92 |
| ，就是这样。 | 91 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| 懂一个点就算赚。 | 102 |
| ，没什么丢人的。 | 101 |
| 个测试把它钉住。 | 100 |
| 儿呢，你慢慢啃。 | 99 |
| 别一上来就重写。 | 98 |
| 便把原因记下来。 | 96 |
| 志比你脑补靠谱。 | 95 |
| 推进一点就够了。 | 94 |
| 正确，再谈优雅。 | 92 |
| 道理，就是这样。 | 91 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 能弄懂一个点就算赚。 | 102 |
| 就查，没什么丢人的。 | 101 |
| ，写个测试把它钉住。 | 100 |
| 在这儿呢，你慢慢啃。 | 99 |
| 次顺便把原因记下来。 | 96 |
| ，日志比你脑补靠谱。 | 95 |
| 每天推进一点就够了。 | 94 |
| 保证正确，再谈优雅。 | 92 |
| 么大道理，就是这样。 | 91 |
| 别人比进度，烦不烦。 | 89 |

## Seasoning marker counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 你认真的？ | 0 |
| 真的假的 | 40 |
| 啊推 | 46 |
| 我靠 | 32 |
| 我丢 | 38 |
| 我真的不想多说什么了 | 27 |
| 嗯嗯 | 7 |
| 嘿嘿 | 0 |
| 笨蛋 | 0 |
| 小笨蛋 | 0 |
| 本姑娘 | 0 |
| 哼 | 1 |
| 6 | 15 |
| 666 | 0 |
| NB | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | dry_sharp | seasoning | legacy source tier | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 32.00% | user_direct_not_observed |
| 1 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 2 | 150 | 150 | 0 | 16.67% | 92.00% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 3 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 4 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 5 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 6 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 7 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 8 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 9 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 10 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 11 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 12 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 13 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 14 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 15 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 16 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 17 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 18 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 19 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 20 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 21 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 22 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 23 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 24 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 25 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 26 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 27 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 28 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 29 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 30 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 31 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 32 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 33 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 34 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 35 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 36 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 37 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 38 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 39 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 40 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 41 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 42 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 43 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 44 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 45 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 46 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 47 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 48 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 49 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 50 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 51 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 52 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 53 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 54 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 55 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 56 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 57 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 58 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 59 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 60 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 61 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 62 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 63 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 64 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 65 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 66 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 67 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 68 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 32.00% | user_direct_not_observed |
| 69 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 70 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 71 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 72 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 73 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 74 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 75 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 76 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 77 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 29.33% | user_direct_not_observed |
| 78 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 79 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 80 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 81 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 82 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 83 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 84 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 85 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 86 | 150 | 150 | 0 | 17.33% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 87 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 88 | 150 | 150 | 0 | 18.00% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 89 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 90 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 91 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 92 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |
| 93 | 150 | 150 | 0 | 16.67% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 94 | 150 | 150 | 0 | 18.00% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 95 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 96 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.67% | user_direct_not_observed |
| 97 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 98 | 150 | 150 | 0 | 16.67% | 91.33% | 0.00% | 10.00% | 2.00% | 1.33% | 30.00% | user_direct_not_observed |
| 99 | 150 | 150 | 0 | 17.33% | 90.67% | 0.00% | 10.00% | 2.00% | 1.33% | 31.33% | user_direct_not_observed |

`easter_egg_not_observed`, `user_direct_not_observed`, `dry_sharp_not_observed`, and `seasoning_not_observed` are transparent non-hard observations. They are not fabricated into the event stream.
