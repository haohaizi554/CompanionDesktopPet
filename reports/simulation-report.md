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
| Scheduler SHA-256 | `6755825b463d92d97db121ca037e100fe770ee789fbc70d0f864bae75ac48a90` |
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
| 8. Technical playback ratio | 18.47% |
| 9. EasterEgg playback ratio | 9.93% |
| 10. user_direct playback ratio | 0.00% |
| dry_sharp playback ratio | 4.00% |
| dry_sharp recent-window violations | 0 |
| dry_sharp forbidden metadata hits | 0 |
| seasoning playback ratio | 4.87% |
| seasoning recent-window violations | 0 |
| 11. ID cooldown repeats | 0 |
| 12. Semantic cooldown repeats | 0 |
| 13. Adjacent same category_group | 17 |
| 14. Adjacent technical | 0 |
| 15a. Adjacent daily_care | 0 |
| 15b. Adjacent emotional_reflection | 0 |
| 15c. Combined adjacent care (including cross-group pairs) | 49 |
| 16. Average text length | 27.692 |
| 19. Seasoning line ratio | 4.87% |
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
| seasoning playback | 4.87% | 3.00%–6.00% |
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
| technical | 277 | 18.47% |
| growth | 148 | 9.87% |
| career | 94 | 6.27% |
| daily_care | 151 | 10.07% |
| emotional_reflection | 151 | 10.07% |
| character_life | 410 | 27.33% |
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
| dry | 459 | 30.60% |
| dry_sharp | 60 | 4.00% |
| encouraging | 0 | 0.00% |
| gentle | 534 | 35.60% |
| intimate | 0 | 0.00% |
| nostalgic | 132 | 8.80% |
| playful | 315 | 21.00% |
| serious | 0 | 0.00% |
| sleepy | 0 | 0.00% |

## 17. Playback text-length distribution

| Length bucket | Ratio |
| --- | --- |
| <8 | 0.00% |
| 8-16 | 5.93% |
| 17-24 | 24.20% |
| 25-36 | 65.27% |
| >36 | 4.60% |

## 18. Frequent openings and endings

### Opening width 2

| Opening | Playback count |
| --- | --- |
| 讲真 | 52 |
| 嗯， | 51 |
| 我看 | 38 |
| 这事 | 37 |
| 行啦 | 35 |
| 先别 | 33 |
| 慢慢 | 33 |
| 偶尔 | 31 |
| 说起 | 30 |
| 先把 | 27 |

### Opening width 3

| Opening | Playback count |
| --- | --- |
| 讲真的 | 52 |
| 我看看 | 38 |
| 行啦， | 35 |
| 慢慢来 | 33 |
| 说起来 | 30 |
| 行吧， | 25 |
| 偶尔吧 | 24 |
| 反正啊 | 24 |
| 我跟你 | 23 |
| 听我的 | 22 |

### Opening width 4

| Opening | Playback count |
| --- | --- |
| 讲真的， | 52 |
| 慢慢来， | 33 |
| 说起来， | 30 |
| 偶尔吧， | 24 |
| 反正啊， | 24 |
| 我跟你讲 | 23 |
| 听我的， | 22 |
| 你先听我 | 20 |
| 先别急， | 20 |
| 我看看， | 20 |

### Opening width 5

| Opening | Playback count |
| --- | --- |
| 我跟你讲， | 23 |
| 这事我还挺 | 20 |
| 说句心里话 | 19 |
| 先停一下， | 18 |
| 我看看啊， | 18 |
| 按自己的节 | 18 |
| 我一直觉得 | 17 |
| 换个轻松点 | 17 |
| 这事能修， | 17 |
| 先从一小步 | 16 |

### Opening width 6

| Opening | Playback count |
| --- | --- |
| 这事我还挺有 | 20 |
| 说句心里话， | 19 |
| 按自己的节奏 | 18 |
| 我一直觉得， | 17 |
| 换个轻松点的 | 17 |
| 先从一小步来 | 16 |
| 今天先啃这一 | 15 |
| 我突然想起， | 15 |
| 偷偷告诉你， | 14 |
| 先把目标放近 | 14 |

### Ending width 4

| Ending | Playback count |
| --- | --- |
| 认真的。 | 31 |
| 慢慢啃。 | 29 |
| 轻一点。 | 28 |
| 挺喜欢。 | 27 |
| 然会来。 | 27 |
| 补靠谱。 | 27 |
| 过就好。 | 27 |
| 就够了。 | 26 |
| 不催你。 | 25 |
| 别人说。 | 24 |

### Ending width 6

| Ending | Playback count |
| --- | --- |
| 我是认真的。 | 31 |
| ，你慢慢啃。 | 29 |
| 你脑补靠谱。 | 27 |
| 心里轻一点。 | 27 |
| 态自然会来。 | 27 |
| 慢慢过就好。 | 27 |
| 我还挺喜欢。 | 27 |
| 我又不催你。 | 25 |
| 一点就够了。 | 24 |
| 也不算见外。 | 24 |

### Ending width 8

| Ending | Playback count |
| --- | --- |
| 这句我是认真的。 | 31 |
| 儿呢，你慢慢啃。 | 29 |
| 嘛，慢慢过就好。 | 27 |
| 好像心里轻一点。 | 27 |
| 志比你脑补靠谱。 | 27 |
| 感觉我还挺喜欢。 | 27 |
| ，状态自然会来。 | 27 |
| 来，我又不催你。 | 25 |
| 可不太跟别人说。 | 24 |
| 推进一点就够了。 | 24 |

### Ending width 10

| Ending | Playback count |
| --- | --- |
| 在这儿呢，你慢慢啃。 | 29 |
| 分钟，状态自然会来。 | 27 |
| 日子嘛，慢慢过就好。 | 27 |
| 说完好像心里轻一点。 | 27 |
| 这种感觉我还挺喜欢。 | 27 |
| ，日志比你脑补靠谱。 | 27 |
| 慢慢来，我又不催你。 | 25 |
| 你说这些也不算见外。 | 24 |
| 复制，写你自己的话。 | 24 |
| 平时可不太跟别人说。 | 24 |

## Seasoning marker counts

| Catchphrase | Playback count |
| --- | --- |
| 哈？ | 0 |
| 你认真的？ | 0 |
| 真的假的 | 10 |
| 啊推 | 20 |
| 我靠 | 12 |
| 我丢 | 18 |
| 我真的不想多说什么了 | 3 |
| 嗯嗯 | 5 |
| 嘿嘿 | 0 |
| 笨蛋 | 0 |
| 小笨蛋 | 0 |
| 本姑娘 | 0 |
| 哼 | 0 |
| 6 | 6 |
| 666 | 1 |
| NB | 0 |

## 22. Per-seed results and anomalies

| Seed | Attempts | Outputs | None | Technical | Self-talk + ambient | user_direct | EasterEgg | dry_sharp | seasoning | Anomalies |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 1 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 2 | 150 | 150 | 0 | 19.33% | 92.00% | 0.00% | 9.33% | 4.00% | 4.67% | user_direct_not_observed |
| 3 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 4 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 5 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 5.33% | user_direct_not_observed |
| 6 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 7 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 8 | 150 | 150 | 0 | 18.67% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |
| 9 | 150 | 150 | 0 | 18.00% | 92.00% | 0.00% | 10.00% | 4.00% | 4.67% | user_direct_not_observed |

`easter_egg_not_observed`, `user_direct_not_observed`, `dry_sharp_not_observed`, and `seasoning_not_observed` are transparent non-hard observations. They are not fabricated into the event stream.
