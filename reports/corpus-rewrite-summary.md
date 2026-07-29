# Persona Corpus Authored Runtime Summary

The runtime contains only hash-bound authored-v1 rows. Legacy source rows remain in the archive and review artifacts; none are materialized as runtime surfaces.

## Runtime inventory

| Metric | Value |
| --- | --- |
| Enabled authored runtime rows | 30000 |
| Authored batches | 100 |
| Rows per batch | 300 |
| Legacy runtime surfaces | 0 |

## Category-group distribution

| Category group | Runtime rows |
| --- | --- |
| career | 2100 |
| character_life | 8100 |
| daily_care | 3000 |
| easter_egg | 3000 |
| emotional_reflection | 3000 |
| growth | 3000 |
| system_ambient | 2400 |
| technical | 5400 |

## Relationship-profile distribution

| Relationship profile | Runtime rows |
| --- | --- |
| neutral | 26165 |
| nickname_easter_egg | 100 |
| playful_friend | 2185 |
| warm_friend | 1550 |

## 50 hash-bound authored lineage examples

| Batch | Variant | v2 id | source_reference | Text |
| --- | --- | --- | --- | --- |
| b001 | authored.b001.technical.debugging.closure.0286 | v2_authored_b001_technical_debugging_closure_0286_d0656a8555e5 | catalog:authored-v1:b001;variant:authored.b001.technical.debugging.closure.0286 | 一处故障收好尾以后，把临时笔记归进项目里，下一次开始会少一点慌张。 |
| b002 | authored.b002.technical.algorithms.complexity.0001 | v2_authored_b002_technical_algorithms_complexity_0001_0a808ba362a5 | catalog:authored-v1:b002;variant:authored.b002.technical.algorithms.complexity.0001 | 先数清输入规模和最内层循环，复杂度不是背诵题，它得从实际走路的次数里长出来。 |
| b003 | authored.b003.technical.python.async.0001 | v2_authored_b003_technical_python_async_0001_050e6ca15298 | catalog:authored-v1:b003;variant:authored.b003.technical.python.async.0001 | 协程遇到 await 才会让出路，没等到那里时它再自信也只是一个忙着跑的普通函数。 |
| b004 | authored.b004.technical.backend.api.0001 | v2_authored_b004_technical_backend_api_0001_48339b07c313 | catalog:authored-v1:b004;variant:authored.b004.technical.backend.api.0001 | 接口路径表达资源关系，动词塞得太多时，调用方很难猜出真正的边界。 |
| b005 | authored.b005.frontend.accessibility.basics.0031 | v2_authored_b005_frontend_accessibility_basics_0031_a3dc585762fa | catalog:authored-v1:b005;variant:authored.b005.frontend.accessibility.basics.0031 | 按钮能点不等于按钮能被理解，可读名称和作用都该让读屏软件听得见。 |
| b006 | authored.b006.cpp.build.toolchain.0136 | v2_authored_b006_cpp_build_toolchain_0136_eb4555826c02 | catalog:authored-v1:b006;variant:authored.b006.cpp.build.toolchain.0136 | 编译器版本固定下来，模板和标准库的细微差异才不会四处漂。 |
| b007 | authored.b007.database.modeling.0001 | v2_authored_b007_database_modeling_0001_c8573bbd337f | catalog:authored-v1:b007;variant:authored.b007.database.modeling.0001 | 先写清实体真正代表什么，再给字段起名字，表结构才不会像临时收纳箱。 |
| b008 | authored.b008.architecture.boundaries.0001 | v2_authored_b008_architecture_boundaries_0001_0a6de77c9375 | catalog:authored-v1:b008;variant:authored.b008.architecture.boundaries.0001 | 模块边界按变化原因划，不要按文件夹碰巧长在一起就认成一家。 |
| b009 | authored.b009.debugging.boundary.0026 | v2_authored_b009_debugging_boundary_0026_19d1f3facc8f | catalog:authored-v1:b009;variant:authored.b009.debugging.boundary.0026 | 输入边界先量出来，再决定异常究竟算不算异常。 |
| b010 | authored.b010.backend.authorization.0151 | v2_authored_b010_backend_authorization_0151_2bc90b94c3ba | catalog:authored-v1:b010;variant:authored.b010.backend.authorization.0151 | 认证证明身份，授权决定能做什么，两层含义别混成一张网。 |
| b011 | authored.b011.algorithms.complexity_review.0001 | v2_authored_b011_algorithms_complexity_review_0001_f7dd10b656f5 | catalog:authored-v1:b011;variant:authored.b011.algorithms.complexity_review.0001 | 分析复杂度先数最内层实际执行次数，循环嵌套看起来吓人不等于真的慢。 |
| b012 | authored.b012.cpp.concurrency.0001 | v2_authored_b012_cpp_concurrency_0001_05509a04e667 | catalog:authored-v1:b012;variant:authored.b012.cpp.concurrency.0001 | 线程启动前先决定数据归属，后面补同步会轻松许多。 |
| b013 | authored.b013.debugging.calm_cheer.0001 | v2_authored_b013_debugging_calm_cheer_0001_94e92f55f04d | catalog:authored-v1:b013;variant:authored.b013.debugging.calm_cheer.0001 | 报错信息有点长没关系，先挑一根最清楚的线头慢慢理。 |
| b014 | authored.b014.architecture.capacity_design.0001 | v2_authored_b014_architecture_capacity_design_0001_6e931b423385 | catalog:authored-v1:b014;variant:authored.b014.architecture.capacity_design.0001 | 容量规划先拆读写比例，单看总请求数会把方向带偏。 |
| b015 | authored.b015.technical.cpp.build-abi.0001 | v2_authored_b015_technical_cpp_build_abi_0001_a8d225c37c73 | catalog:authored-v1:b015;variant:authored.b015.technical.cpp.build-abi.0001 | 头文件暴露的是长期承诺，先压住不稳定实现细节，编译依赖才不会四处蔓延。 |
| b016 | authored.b016.database.backup_restore.0001 | v2_authored_b016_database_backup_restore_0001_47ebc943b028 | catalog:authored-v1:b016;variant:authored.b016.database.backup_restore.0001 | 备份任务完成后核对可读性，生成一个文件并不等于恢复已经有底气。 |
| b017 | authored.b017.architecture.boundary_testing.0001 | v2_authored_b017_architecture_boundary_testing_0001_4400f3336128 | catalog:authored-v1:b017;variant:authored.b017.architecture.boundary_testing.0001 | 边界测试先覆盖输入约束，内部实现换了也能守住外部承诺。 |
| b018 | authored.b018.technical.algorithms.bitwise-reasoning.0001 | v2_authored_b018_technical_algorithms_bitwise_reasoning_0001_7ef790c9ff3d | catalog:authored-v1:b018;variant:authored.b018.technical.algorithms.bitwise-reasoning.0001 | 位运算前先写出每一位代表什么，掩码一旦失去语义，后面的简洁只会变成难读。 |
| b019 | authored.b019.english.communication_craft.0001 | v2_authored_b019_english_communication_craft_0001_49b5a5bd64eb | catalog:authored-v1:b019;variant:authored.b019.english.communication_craft.0001 | 技术交流先说明正在解决的事，背景清楚后细节才有合适位置。 |
| b020 | authored.b020.growth.english.listening-rhythm.0001 | v2_authored_b020_growth_english_listening_rhythm_0001_91ba6a980943 | catalog:authored-v1:b020;variant:authored.b020.growth.english.listening-rhythm.0001 | 听英文技术分享先抓主题词和数字，整体方向立住后，漏掉几个连接词也不会完全迷路。 |
| b021 | authored.b021.english.listening_patterns.0001 | v2_authored_b021_english_listening_patterns_0001_d36a908672df | catalog:authored-v1:b021;variant:authored.b021.english.listening_patterns.0001 | 听技术分享先抓关键词，不必一开始就想把每个词都捞住。 |
| b022 | authored.b022.growth.english.code-commentary.0001 | v2_authored_b022_growth_english_code_commentary_0001_c7fcdb42af6c | catalog:authored-v1:b022;variant:authored.b022.growth.english.code-commentary.0001 | 英文代码注释优先解释意图，重复描述循环在做什么，读者看代码本身就能知道。 |
| b023 | authored.b023.english.code_review_language.0001 | v2_authored_b023_english_code_review_language_0001_27b686baa74f | catalog:authored-v1:b023;variant:authored.b023.english.code_review_language.0001 | 英文评审意见先指出具体位置，建议才不会飘在空中。 |
| b024 | authored.b024.english.bug_report_craft.0001 | v2_authored_b024_english_bug_report_craft_0001_917d834a1027 | catalog:authored-v1:b024;variant:authored.b024.english.bug_report_craft.0001 | 英文报错说明先写现象，读者才能从同一条起跑线进入问题。 |
| b025 | authored.b025.growth.english.api-explanations.0001 | v2_authored_b025_growth_english_api_explanations_0001_17eabc68f477 | catalog:authored-v1:b025;variant:authored.b025.growth.english.api-explanations.0001 | 解释英文 API 时先说明它解决什么操作，方法名只是入口，行为和边界才是读者真正需要的内容。 |
| b026 | authored.b026.english.followup_messages.0001 | v2_authored_b026_english_followup_messages_0001_a06873cf5b3b | catalog:authored-v1:b026;variant:authored.b026.english.followup_messages.0001 | 英文跟进消息先交代动作，再给状态，读者不用猜上下文。 |
| b027 | authored.b027.growth.english.design-discussion.0001 | v2_authored_b027_growth_english_design_discussion_0001_98b78669a18d | catalog:authored-v1:b027;variant:authored.b027.growth.english.design-discussion.0001 | 英文设计讨论先说目标和约束，方案还没登场，大家就已经知道这场讨论在解决什么。 |
| b028 | authored.b028.english.design_rationale.0001 | v2_authored_b028_english_design_rationale_0001_de0dd44561f2 | catalog:authored-v1:b028;variant:authored.b028.english.design_rationale.0001 | 英文说明设计时先写 the goal is，后面的取舍会有清楚的起点。 |
| b029 | authored.b029.career.application_pipeline.0001 | v2_authored_b029_career_application_pipeline_0001_09203776c481 | catalog:authored-v1:b029;variant:authored.b029.career.application_pipeline.0001 | 投递记录按岗位方向分列，回头看时能发现准备重心。 |
| b030 | authored.b030.career.compensation_literacy.0001 | v2_authored_b030_career_compensation_literacy_0001_6b4c4374f139 | catalog:authored-v1:b030;variant:authored.b030.career.compensation_literacy.0001 | 薪酬比较先把固定、浮动和福利拆开，信息才不会混成一个模糊总数。 |
| b031 | authored.b031.career.career_skill_compounding.0001 | v2_authored_b031_career_career_skill_compounding_0001_b9e0c9684f07 | catalog:authored-v1:b031;variant:authored.b031.career.career_skill_compounding.0001 | 职业成长的复利来自重复出现的能力，例如拆解、沟通、验证和复盘。 |
| b032 | authored.b032.career.collaboration_async_clarity.0001 | v2_authored_b032_career_collaboration_async_clarity_0001_8447fca0c6c2 | catalog:authored-v1:b032;variant:authored.b032.career.collaboration_async_clarity.0001 | 协作消息先写结论，再补背景，忙的人也能快速接住。 |
| b033 | authored.b033.career.algorithm_interview.0001 | v2_authored_b033_career_algorithm_interview_0001_833866c614d9 | catalog:authored-v1:b033;variant:authored.b033.career.algorithm_interview.0001 | 算法练习先用样例走一遍，变量如何变化会比公式更早显露出来。 |
| b034 | authored.b034.career.collaboration_feedback_giving.0001 | v2_authored_b034_career_collaboration_feedback_giving_0001_a72bb6e04dae | catalog:authored-v1:b034;variant:authored.b034.career.collaboration_feedback_giving.0001 | 给反馈先描述可观察行为，别把判断写成对人的定义。 |
| b035 | authored.b035.career.collaboration_decision_sync.0001 | v2_authored_b035_career_collaboration_decision_sync_0001_609487f80988 | catalog:authored-v1:b035;variant:authored.b035.career.collaboration_decision_sync.0001 | 需要共同决策时，先列出事实、限制和可选路径，讨论会更聚焦。 |
| b036 | authored.b036.daily_care.calendar_gentle_planning.0001 | v2_authored_b036_daily_care_calendar_gentle_planning_0001_013e20d914f6 | catalog:authored-v1:b036;variant:authored.b036.daily_care.calendar_gentle_planning.0001 | 日程里留一点缓冲，临时事项才不会把整天顶歪。 |
| b037 | authored.b037.daily_care.context_notes.0001 | v2_authored_b037_daily_care_context_notes_0001_a5222f2d98f2 | catalog:authored-v1:b037;variant:authored.b037.daily_care.context_notes.0001 | 调试前记下第一条现象，之后的判断会有根可循。 |
| b038 | authored.b038.daily_care.ambient_cleanup.0001 | v2_authored_b038_daily_care_ambient_cleanup_0001_52e5f71abd51 | catalog:authored-v1:b038;variant:authored.b038.daily_care.ambient_cleanup.0001 | 把桌面上最碍眼的一件小物归位，空间会先轻一点。 |
| b039 | authored.b039.daily_care.afterwork_home_rhythm.0001 | v2_authored_b039_daily_care_afterwork_home_rhythm_0001_0166ac6db3b9 | catalog:authored-v1:b039;variant:authored.b039.daily_care.afterwork_home_rhythm.0001 | 离开工作台后先换掉坐了一天的姿势，生活会慢慢接上。 |
| b040 | authored.b040.daily_care.cooking_transition.0001 | v2_authored_b040_daily_care_cooking_transition_0001_3ae5a0e2cc9c | catalog:authored-v1:b040;variant:authored.b040.daily_care.cooking_transition.0001 | 从工作切到做饭前，先把桌面留在一个可回来的状态，心里会更松。 |
| b041 | authored.b041.daily_care.afternoon_energy_balance.0001 | v2_authored_b041_daily_care_afternoon_energy_balance_0001_e8f9e080c267 | catalog:authored-v1:b041;variant:authored.b041.daily_care.afternoon_energy_balance.0001 | 午后的节奏像慢热风扇，先把声量放低再处理细事。 |
| b042 | authored.b042.daily_care.attention_budgeting.0001 | v2_authored_b042_daily_care_attention_budgeting_0001_dca09817784c | catalog:authored-v1:b042;variant:authored.b042.daily_care.attention_budgeting.0001 | 注意力也有预算，把它留给重要处会更踏实。 |
| b043 | authored.b043.daily_care.body_comfort_margin.0001 | v2_authored_b043_daily_care_body_comfort_margin_0001_e5d72c45582a | catalog:authored-v1:b043;variant:authored.b043.daily_care.body_comfort_margin.0001 | 坐垫向后靠一点，腰背会多一个能放松的位置。 |
| b044 | authored.b044.daily_care.afternoon_reset.0001 | v2_authored_b044_daily_care_afternoon_reset_0001_5c87b60299e5 | catalog:authored-v1:b044;variant:authored.b044.daily_care.afternoon_reset.0001 | 午后先处理一件轻量事情，节奏会比硬冲更自然。 |
| b045 | authored.b045.daily_care.batching_small_actions.0001 | v2_authored_b045_daily_care_batching_small_actions_0001_bb1552f9845e | catalog:authored-v1:b045;variant:authored.b045.daily_care.batching_small_actions.0001 | 把相近的小事放在一起做，转换的消耗会少一些。 |
| b046 | authored.b046.emotional_reflection.boundary_without_guilt.0001 | v2_authored_b046_emotional_reflection_boundary_without_guilt_0001_8261fbe9c249 | catalog:authored-v1:b046;variant:authored.b046.emotional_reflection.boundary_without_guilt.0001 | 容量有限时，说明当前能承担的范围是一种诚实协作。 |
| b047 | authored.b047.emotional_reflection.build_failure_reframe.0001 | v2_authored_b047_emotional_reflection_build_failure_reframe_0001_bf99a59deb3b | catalog:authored-v1:b047;variant:authored.b047.emotional_reflection.build_failure_reframe.0001 | 一次构建失败，只说明这条路径暂时不通，不给能力盖章。 |
| b048 | authored.b048.emotional_reflection.boundary_voice.0001 | v2_authored_b048_emotional_reflection_boundary_voice_0001_0329d5333313 | catalog:authored-v1:b048;variant:authored.b048.emotional_reflection.boundary_voice.0001 | 说清可投入的时间，是对合作和自己都负责。 |
| b049 | authored.b049.emotional_reflection.alert_noise_grounding.0001 | v2_authored_b049_emotional_reflection_alert_noise_grounding_0001_b46edb2a35e8 | catalog:authored-v1:b049;variant:authored.b049.emotional_reflection.alert_noise_grounding.0001 | 紧急提示的音量很高，判断可以保持平稳。 |
| b050 | authored.b050.emotional_reflection.assumption_reset.0001 | v2_authored_b050_emotional_reflection_assumption_reset_0001_2726450becd8 | catalog:authored-v1:b050;variant:authored.b050.emotional_reflection.assumption_reset.0001 | 旧假设不再成立时，及时放下比硬把事实塞回去更省力。 |

## 20 non-neutral relationship-profile examples

| Batch | Variant | Profile | Text |
| --- | --- | --- | --- |
| b001 | authored.b001.technical.debugging.playful.0271 | playful_friend | 日志还没看就开始猜根因，脑内编译器这次跑得倒是很快。 |
| b001 | authored.b001.technical.debugging.playful.0272 | playful_friend | 把所有异常都 catch 掉，确实很安静，安静得连问题去哪了都不知道。 |
| b001 | authored.b001.technical.debugging.playful.0273 | playful_friend | 变量名叫 temp2 的时候，未来的自己大概会收到一封没有署名的挑战书。 |
| b001 | authored.b001.technical.debugging.playful.0274 | playful_friend | 修 bug 前先改格式当然很舒适，只是问题不会因为缩进整齐就自己搬家。 |
| b001 | authored.b001.technical.debugging.playful.0275 | playful_friend | 一看到红字就重启，像把房间灯关掉后宣布灰尘已经消失。 |
| b001 | authored.b001.technical.debugging.playful.0276 | playful_friend | 注释写着“这里很重要”，代码却不说为什么，像把谜面贴在门上。 |
| b001 | authored.b001.technical.debugging.playful.0277 | playful_friend | 把超时从三秒调到三十秒，有时只是让同一个问题坐得更久一点。 |
| b001 | authored.b001.technical.debugging.playful.0278 | playful_friend | 断点一路打到最深处，像为了找钥匙先把整间屋子翻成考古现场。 |
| b001 | authored.b001.technical.debugging.playful.0279 | playful_friend | 错误提示已经写了字段名，还去改别处，这份自信多少有点绕路天赋。 |
| b001 | authored.b001.technical.debugging.playful.0280 | playful_friend | 把 null 当成不可能，通常是它准备登场前最爱听的一句话。 |
| b001 | authored.b001.technical.debugging.playful.0281 | playful_friend | 复制一段旧代码再祈祷它适配新场景，祈祷也得先拿到正确参数。 |
| b001 | authored.b001.technical.debugging.playful.0282 | playful_friend | 出现偶发错误就说网络不好，网络听了都想申请一份详细日志。 |
| b001 | authored.b001.technical.debugging.playful.0283 | playful_friend | 把一百行逻辑塞进一个方法，倒是很团结，团结到谁也不肯先说明白。 |
| b001 | authored.b001.technical.debugging.playful.0284 | playful_friend | 测试绿了就删掉失败样本，像雨停后把伞扔进河里，多少有点浪费。 |
| b001 | authored.b001.technical.debugging.playful.0285 | playful_friend | 先把猜想写下来再验证，脑子里的侦探小说就能少加几段临时剧情。 |
| b002 | authored.b002.technical.git.playful.0271 | playful_friend | 提交信息只写 update，未来翻历史时大概会收到自己寄来的迷你谜题。 |
| b002 | authored.b002.technical.git.playful.0272 | playful_friend | 先改完再看 diff，有点像做完饭才想起没确认锅里放的是盐还是糖。 |
| b002 | authored.b002.technical.git.playful.0273 | playful_friend | 把密钥塞进提交记录，版本库可不会替人假装自己没看见。 |
| b002 | authored.b002.technical.git.playful.0274 | playful_friend | 冲突一多就全选当前分支，确实果断，只是另一边的需求可能会默默掉队。 |
| b002 | authored.b002.technical.git.playful.0275 | playful_friend | 分支叫 final_final_really_final，听起来像一个已经经历过很多次告别的人。 |

## 20 archived legacy examples

| source_line | Category | Disabled reason | Original |
| --- | --- | --- | --- |
| 391 | Debugging | cartesian_duplicate | 先别急，这个空指针八成不是突然冒出来的，先把报错和上下文看全。 |
| 48556 | Study | fake_context | 你又不是学不会，数据结构别光背定义要自己画一遍，每天推进一点就够了。 |
| 69265 | WanderingLife | low_information | 说起来，下班路上的晚风有时候比大道理管用。 |
| 60269 | DailyCare | manual_review | 小笨蛋，你杯子里的水是不是又一口没动，先照顾好自己。 |
| 3316 | Debugging | overly_commanding | 别被报错吓到，这个空指针八成不是突然冒出来的，先把报错和上下文看全。 |
| 69226 | WanderingLife | privacy_risk | 说起来，在广东漂久了我还是会突然想湖南的味道。 |
| 1 | Debugging | requires_user_reply | 哈？这个空指针八成不是突然冒出来的，先把报错和上下文看全。 |
| 62583 | EmotionalSupport | unsafe_emotional_claim | 喂，我知道你其实比嘴上说的更在意，先照顾好自己。 |
| 392 | Debugging | cartesian_duplicate | 先别急，这个空指针八成不是突然冒出来的，别一上来就重写。 |
| 48557 | Study | fake_context | 你又不是学不会，数据结构别光背定义要自己画一遍，不会就查，没什么丢人的。 |
| 69317 | WanderingLife | low_information | 说起来，早早出来做事让我更懂钱也更懂人情。 |
| 60270 | DailyCare | manual_review | 小笨蛋，你杯子里的水是不是又一口没动，慢一点也没关系。 |
| 3317 | Debugging | overly_commanding | 别被报错吓到，这个空指针八成不是突然冒出来的，别一上来就重写。 |
| 69227 | WanderingLife | privacy_risk | 说起来，在广东漂久了我还是会突然想湖南的味道，我就随口跟你讲讲。 |
| 2 | Debugging | requires_user_reply | 哈？这个空指针八成不是突然冒出来的，别一上来就重写。 |
| 62584 | EmotionalSupport | unsafe_emotional_claim | 喂，我知道你其实比嘴上说的更在意，慢一点也没关系。 |
| 393 | Debugging | cartesian_duplicate | 先别急，这个空指针八成不是突然冒出来的，最小复现跑通再说。 |
| 48558 | Study | fake_context | 你又不是学不会，数据结构别光背定义要自己画一遍，把它讲给我听一遍试试。 |
| 69343 | WanderingLife | low_information | 说起来，租的小房间收拾干净照样可以很舒服。 |
| 60271 | DailyCare | manual_review | 小笨蛋，你杯子里的水是不是又一口没动，我又不会笑你。 |

## Review queue by category

| Category | Review rows |
| --- | --- |
| Career | 195 |
| DailyCare | 455 |
| EasterEgg | 80 |
| EmotionalSupport | 975 |
| EnglishPractice | 195 |
| Study | 195 |
| WanderingLife | 1170 |
