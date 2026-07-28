# Persona Playback Simulation

This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.
The validator-facing event stream is stored separately with an exact schema and input hashes.

## Run contract

| Field | Value |
| --- | --- |
| Schema version | 3 |
| Days per seed | 30 |
| Seeds | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 |
| Corpus SHA-256 | `3335d72e695528892ddec92076f0f02abacf58fff02ed6bd0aadf67d1cf0cc40` |
| Scheduler SHA-256 | `d0b3cc794a2ee748714e6b69ec8bf2c8b14ea5e149998c13c6d70f150242e401` |
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
| 8. Technical playback ratio | 18.73% |
| 9. EasterEgg playback ratio | 9.93% |
| 10. user_direct playback ratio | 0.00% |
| dry_sharp playback ratio | 4.00% |
| dry_sharp recent-window violations | 0 |
| dry_sharp forbidden metadata hits | 0 |
| seasoning playback ratio | 4.93% |
| seasoning recent-window violations | 0 |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 25 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care (including cross-group pairs) | 49 |
| 16. Average text length | 27.747 |
| 19. Seasoning line ratio | 4.93% |
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
| dry_sharp scene inventory | 22/533 (4.13%) | 5.00% | 4.00%–6.00% | yes |
| dry_sharp row inventory observation | 3878/52132 (7.44%) | observation only | n/a | no |
| dry_sharp playback | 60/1500 (4.00%) | 3.00% | 2.00%–4.00% | yes |

Bootstrap scene gap: no (minimum 4 scenes).
Recent playback limit: at most 1 dry_sharp line(s) in the latest 20 outputs.

## Seasoning lexical exposure evidence

| Metric | Observed | Acceptance / policy |
| --- | --- | --- |
| expanded_runtime inventory observation | 13515/52132 (25.92%) | observation_only |
| seasoning playback | 4.93% | 3.00%–6.00% |
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
| technical | 281 | 18.73% |
| growth | 146 | 9.73% |
| career | 94 | 6.27% |
| daily_care | 151 | 10.07% |
| emotional_reflection | 150 | 10.00% |
| character_life | 409 | 27.27% |
| easter_egg | 149 | 9.93% |
| system_ambient | 120 | 8.00% |

## 7. output_mode playback

| output_mode | Count | Ratio |
| --- | --- | --- |
| self_talk | 1229 | 81.93% |
| ambient | 151 | 10.07% |
| user_direct | 0 | 0.00% |
| system_observe | 120 | 8.00% |

## Tone playback

| tone | Count | Ratio |
| --- | --- | --- |
| calm | 0 | 0.00% |
| curious | 0 | 0.00% |
| dry | 461 | 30.73% |
| dry_sharp | 60 | 4.00% |
| encouraging | 0 | 0.00% |
| gentle | 521 | 34.73% |
| intimate | 0 | 0.00% |
| nostalgic | 133 | 8.87% |
| playful | 325 | 21.67% |
| serious | 0 | 0.00% |
| sleepy | 0 | 0.00% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 6.80% |
| 17-24 | 22.13% |
| 25-36 | 66.53% |
| >36 | 4.53% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 嗯， | 60 |
| 讲真 | 58 |
| 这事 | 40 |
| 先别 | 36 |
| 先把 | 34 |
| 我看 | 33 |
| 偶尔 | 27 |
| 说起 | 27 |
| 偷偷 | 25 |
| 先坐 | 25 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 讲真的 | 58 |
| 我看看 | 33 |
| 说起来 | 27 |
| 这事我 | 26 |
| 偷偷告 | 25 |
| 先坐好 | 25 |
| 说句心 | 24 |
| 偶尔吧 | 23 |
| 先别急 | 22 |
| 行啦， | 22 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 讲真的， | 58 |
| 说起来， | 27 |
| 这事我还 | 26 |
| 偷偷告诉 | 25 |
| 先坐好， | 25 |
| 说句心里 | 24 |
| 偶尔吧， | 23 |
| 先别急， | 22 |
| 慢慢来， | 21 |
| 我陪你捋 | 21 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 这事我还挺 | 26 |
| 偷偷告诉你 | 25 |
| 说句心里话 | 24 |
| 我陪你捋， | 21 |
| 先收集证据 | 20 |
| 你别笑我， | 19 |
| 先从一小步 | 19 |
| 我跟你讲， | 18 |
| 先把目标放 | 17 |
| 先把范围缩 | 17 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 这事我还挺有 | 26 |
| 偷偷告诉你， | 25 |
| 说句心里话， | 24 |
| 先收集证据， | 20 |
| 先从一小步来 | 19 |
| 先把目标放近 | 17 |
| 先把范围缩小 | 17 |
| 我们捋一下， | 17 |
| 换个轻松点的 | 17 |
| 有时候我会想 | 17 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 过就好。 | 30 |
| 你讲讲。 | 29 |
| 真实的。 | 29 |
| 再加码。 | 28 |
| 挺喜欢。 | 28 |
| 再继续。 | 26 |
| 别人说。 | 26 |
| 就够了。 | 26 |
| 是这样。 | 26 |
| 烦不烦。 | 25 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 慢慢过就好。 | 30 |
| 也蛮真实的。 | 29 |
| 口跟你讲讲。 | 29 |
| 一步再加码。 | 28 |
| 我还挺喜欢。 | 28 |
| 太跟别人说。 | 26 |
| 一点就够了。 | 25 |
| 会儿再继续。 | 25 |
| 度，烦不烦。 | 25 |
| ，就是这样。 | 25 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| 嘛，慢慢过就好。 | 30 |
| 就随口跟你讲讲。 | 29 |
| 想想也蛮真实的。 | 29 |
| 小的一步再加码。 | 28 |
| 感觉我还挺喜欢。 | 28 |
| 可不太跟别人说。 | 26 |
| 就歇会儿再继续。 | 25 |
| 比进度，烦不烦。 | 25 |
| 道理，就是这样。 | 25 |
| 反正开心最要紧。 | 24 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 日子嘛，慢慢过就好。 | 30 |
| 现在想想也蛮真实的。 | 29 |
| ，我就随口跟你讲讲。 | 29 |
| 完最小的一步再加码。 | 28 |
| 这种感觉我还挺喜欢。 | 28 |
| 平时可不太跟别人说。 | 26 |
| 么大道理，就是这样。 | 25 |
| 别人比进度，烦不烦。 | 25 |
| 累了就歇会儿再继续。 | 25 |
| ，最小复现跑通再说。 | 24 |

## Seasoning marker counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 你认真的？ | 0 |
| 真的假的 | 8 |
| 啊推 | 18 |
| 我靠 | 12 |
| 我丢 | 16 |
| 我真的不想多说什么了 | 9 |
| 嗯嗯 | 4 |
| 嘿嘿 | 0 |
| 笨蛋 | 0 |
| 小笨蛋 | 0 |
| 本姑娘 | 0 |
| 哼 | 0 |
| 6 | 8 |
| 666 | 0 |
| NB | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | dry_sharp | seasoning | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 1 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 2 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 3 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 9.33% | 4.00% | 5.33% | user_direct_not_observed |
| 4 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 5 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 6 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 7 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 8 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 9 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |

`easter_egg_not_observed`, `user_direct_not_observed`, `dry_sharp_not_observed`, and `seasoning_not_observed` are transparent non-hard observations. They are not fabricated into the event stream.
