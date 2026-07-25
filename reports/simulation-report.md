# Persona Playback Simulation

This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.
The validator-facing event stream is stored separately with an exact schema and input hashes.

## Run contract

| Field | Value |
| --- | --- |
| Schema version | 2 |
| Days per seed | 30 |
| Seeds | 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 |
| Corpus SHA-256 | `3335d72e695528892ddec92076f0f02abacf58fff02ed6bd0aadf67d1cf0cc40` |
| Scheduler SHA-256 | `4eaa40cd28d58aaa9dcecaaded539f25ceb39b35a4fc1cd9012d422cd414b462` |
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
| 9. EasterEgg playback ratio | 9.87% |
| 10. user_direct playback ratio | 0.00% |
| dry_sharp playback ratio | 4.00% |
| dry_sharp recent-window violations | 0 |
| dry_sharp forbidden metadata hits | 0 |
| seasoning playback ratio | 4.93% |
| seasoning recent-window violations | 0 |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 24 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care (including cross-group pairs) | 53 |
| 16. Average text length | 27.550 |
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
| growth | 145 | 9.67% |
| career | 96 | 6.40% |
| daily_care | 151 | 10.07% |
| emotional_reflection | 151 | 10.07% |
| character_life | 408 | 27.20% |
| easter_egg | 148 | 9.87% |
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
| dry | 462 | 30.80% |
| dry_sharp | 60 | 4.00% |
| encouraging | 0 | 0.00% |
| gentle | 536 | 35.73% |
| intimate | 0 | 0.00% |
| nostalgic | 122 | 8.13% |
| playful | 320 | 21.33% |
| serious | 0 | 0.00% |
| sleepy | 0 | 0.00% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 8.47% |
| 17-24 | 23.53% |
| 25-36 | 63.07% |
| >36 | 4.93% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 讲真 | 53 |
| 嗯， | 49 |
| 先别 | 46 |
| 先把 | 41 |
| 我看 | 36 |
| 行啦 | 34 |
| 这事 | 33 |
| 今天 | 29 |
| 听我 | 28 |
| 先坐 | 27 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 讲真的 | 53 |
| 我看看 | 36 |
| 行啦， | 34 |
| 听我的 | 28 |
| 先坐好 | 27 |
| 先别急 | 26 |
| 啊推， | 23 |
| 说起来 | 23 |
| 先停一 | 22 |
| 先把目 | 22 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 讲真的， | 53 |
| 听我的， | 28 |
| 先坐好， | 27 |
| 先别急， | 26 |
| 说起来， | 23 |
| 先停一下 | 22 |
| 先把目标 | 22 |
| 偷偷告诉 | 21 |
| 我跟你讲 | 21 |
| 先别摆烂 | 20 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 先停一下， | 22 |
| 先把目标放 | 22 |
| 偷偷告诉你 | 21 |
| 我跟你讲， | 21 |
| 先别摆烂， | 20 |
| 我们捋一下 | 20 |
| 先把范围缩 | 19 |
| 想到这里， | 19 |
| 我一直觉得 | 19 |
| 换个轻松点 | 19 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 先把目标放近 | 22 |
| 偷偷告诉你， | 21 |
| 我们捋一下， | 20 |
| 先把范围缩小 | 19 |
| 我一直觉得， | 19 |
| 换个轻松点的 | 19 |
| 说句心里话， | 18 |
| 有时候我会想 | 17 |
| 这事我还挺有 | 17 |
| 先从一小步来 | 15 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 然会来。 | 30 |
| 再继续。 | 28 |
| 就够了。 | 28 |
| 己的话。 | 27 |
| 认真的。 | 27 |
| 真实的。 | 26 |
| 轻一点。 | 26 |
| 也挺好。 | 25 |
| 动代码。 | 25 |
| 补靠谱。 | 25 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 态自然会来。 | 30 |
| 你自己的话。 | 27 |
| 我是认真的。 | 27 |
| 一点就够了。 | 26 |
| 也蛮真实的。 | 26 |
| 会儿再继续。 | 26 |
| 一点也挺好。 | 25 |
| 你脑补靠谱。 | 25 |
| 实再动代码。 | 25 |
| 个点就算赚。 | 24 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| ，状态自然会来。 | 30 |
| 这句我是认真的。 | 27 |
| ，写你自己的话。 | 27 |
| 就歇会儿再继续。 | 26 |
| 想想也蛮真实的。 | 26 |
| 志比你脑补靠谱。 | 25 |
| 推进一点就够了。 | 25 |
| 普通一点也挺好。 | 25 |
| 认事实再动代码。 | 25 |
| 反正开心最要紧。 | 24 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 分钟，状态自然会来。 | 30 |
| 复制，写你自己的话。 | 27 |
| 现在想想也蛮真实的。 | 26 |
| 累了就歇会儿再继续。 | 26 |
| 先确认事实再动代码。 | 25 |
| 每天推进一点就够了。 | 25 |
| ，日志比你脑补靠谱。 | 25 |
| 平时可不太跟别人说。 | 24 |
| 慢慢来，我又不催你。 | 24 |
| 日子嘛，慢慢过就好。 | 24 |

## Seasoning marker counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 你认真的？ | 0 |
| 真的假的 | 10 |
| 啊推 | 23 |
| 我靠 | 8 |
| 我丢 | 22 |
| 我真的不想多说什么了 | 5 |
| 嗯嗯 | 2 |
| 嘿嘿 | 0 |
| 笨蛋 | 0 |
| 小笨蛋 | 0 |
| 本姑娘 | 0 |
| 哼 | 0 |
| 6 | 5 |
| 666 | 0 |
| NB | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | dry_sharp | seasoning | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 1 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 2 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 3 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 4 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 5 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 9.33% | 4.00% | 5.33% | user_direct_not_observed |
| 6 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 7 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 9.33% | 4.00% | 5.33% | user_direct_not_observed |
| 8 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 9 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |

`easter_egg_not_observed`, `user_direct_not_observed`, `dry_sharp_not_observed`, and `seasoning_not_observed` are transparent non-hard observations. They are not fabricated into the event stream.
