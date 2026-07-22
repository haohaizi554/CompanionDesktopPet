# Persona Corpus Baseline Audit

- Input: `src/CompanionDesktopPet/Assets/persona-corpus.tsv`
- SHA-256: `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`
- Audit mode: bounded rare character 3-gram candidate buckets; no all-pairs comparison

## Summary

| Metric | Value |
| --- | --- |
| Total lines | 75375 |
| Categories | 21 |
| Exact duplicate rows beyond first | 0 |
| Normalized duplicate rows beyond first | 0 |
| Question lines | 8580 |
| Likely PII lines | 1191 |
| Bounded near-duplicate pairs | 455894 |

## Category distribution

| Category | Count |
| --- | --- |
| Algorithms | 3900 |
| Architecture | 3900 |
| Backend | 3900 |
| Career | 3900 |
| Cpp | 3900 |
| DailyCare | 3900 |
| Database | 3900 |
| Debugging | 3900 |
| EmotionalSupport | 3900 |
| EnglishPractice | 3900 |
| Frontend | 3900 |
| GitDevOps | 3900 |
| Java | 3900 |
| Networks | 3900 |
| Python | 3900 |
| Study | 3900 |
| Systems | 3900 |
| DressesHobbies | 2925 |
| ProactiveChat | 2925 |
| WanderingLife | 2925 |
| EasterEgg | 300 |

## Text-length distribution

| Characters | Count |
| --- | --- |
| 0–10 | 0 |
| 11–20 | 376 |
| 21–30 | 27298 |
| 31–50 | 47691 |
| 51+ | 10 |

## Risk indicators

| Indicator | Count | Examples |
| --- | --- | --- |
| Chinese or ASCII question mark | 8580 | source line 1, source line 2, source line 3, source line 4, source line 5, source line 6, source line 7, source line 8, source line 9, source line 10, source line 11, source line 12, source line 13, source line 14, source line 15, source line 16, source line 17, source line 18, source line 19, source line 20 |
| Likely PII marker | 1191 | source line 69226, source line 69227, source line 69228, source line 69229, source line 69230, source line 69231, source line 69232, source line 69233, source line 69234, source line 69235, source line 69236, source line 69237, source line 69238, source line 69239, source line 69240, source line 69241, source line 69242, source line 69243, source line 69244, source line 69245 |
| High-risk phrase `你今天` | 260 | source line 58501, source line 58502, source line 58503, source line 58504, source line 58505, source line 58506, source line 58507, source line 58508, source line 58509, source line 58510, source line 58511, source line 58512, source line 58513, source line 58696, source line 58697, source line 58698, source line 58699, source line 58700, source line 58701, source line 58702 |
| High-risk phrase `你现在` | 260 | source line 62466, source line 62467, source line 62468, source line 62469, source line 62470, source line 62471, source line 62472, source line 62473, source line 62474, source line 62475, source line 62476, source line 62477, source line 62478, source line 62661, source line 62662, source line 62663, source line 62664, source line 62665, source line 62666, source line 62667 |
| High-risk phrase `你觉得` | 195 | source line 66444, source line 66445, source line 66446, source line 66447, source line 66448, source line 66449, source line 66450, source line 66451, source line 66452, source line 66453, source line 66454, source line 66455, source line 66456, source line 66639, source line 66640, source line 66641, source line 66642, source line 66643, source line 66644, source line 66645 |
| High-risk phrase `告诉我` | 195 | source line 66353, source line 66354, source line 66355, source line 66356, source line 66357, source line 66358, source line 66359, source line 66360, source line 66361, source line 66362, source line 66363, source line 66364, source line 66365, source line 66548, source line 66549, source line 66550, source line 66551, source line 66552, source line 66553, source line 66554 |

## Catchphrase distribution

| Phrase | Count | Examples |
| --- | --- | --- |
| 哈？ | 3315 | source line 1, source line 2, source line 3, source line 4, source line 5, source line 6, source line 7, source line 8, source line 9, source line 10, source line 11, source line 12, source line 13, source line 14, source line 15, source line 16, source line 17, source line 18, source line 19, source line 20 |
| 笨蛋 | 390 | source line 60256, source line 60257, source line 60258, source line 60259, source line 60260, source line 60261, source line 60262, source line 60263, source line 60264, source line 60265, source line 60266, source line 60267, source line 60268, source line 60269, source line 60270, source line 60271, source line 60272, source line 60273, source line 60274, source line 60275 |
| 玥玥 | 30 | source line 75136, source line 75137, source line 75138, source line 75139, source line 75140, source line 75141, source line 75142, source line 75143, source line 75144, source line 75145, source line 75146, source line 75147, source line 75148, source line 75149, source line 75150, source line 75151, source line 75152, source line 75153, source line 75154, source line 75155 |

## Prefix distribution

### Length 2

| Prefix | Count |
| --- | --- |
| 啊推 | 3315 |
| 我丢 | 3315 |
| 先别 | 2925 |
| 先把 | 2925 |
| 我看 | 2925 |
| 真的 | 2925 |
| 这事 | 2730 |
| 先停 | 2535 |
| 你认 | 2340 |
| 先收 | 2340 |
| 先看 | 2340 |
| 别被 | 2340 |
| 听我 | 2340 |
| 我们 | 2340 |
| 我真 | 2340 |

### Length 3

| Prefix | Count |
| --- | --- |
| 我看看 | 2925 |
| 真的假 | 2925 |
| 先停一 | 2535 |
| 你认真 | 2340 |
| 先别急 | 2340 |
| 先把范 | 2340 |
| 先收集 | 2340 |
| 先看现 | 2340 |
| 别被报 | 2340 |
| 听我的 | 2340 |
| 我们捋 | 2340 |
| 我真的 | 2340 |
| 按步骤 | 2340 |
| 这事能 | 2340 |
| 你先听 | 975 |

### Length 4

| Prefix | Count |
| --- | --- |
| 真的假的 | 2925 |
| 先停一下 | 2535 |
| 你认真的 | 2340 |
| 先把范围 | 2340 |
| 先收集证 | 2340 |
| 先看现象 | 2340 |
| 别被报错 | 2340 |
| 我们捋一 | 2340 |
| 我真的不 | 2340 |
| 按步骤来 | 2340 |
| 这事能修 | 2340 |
| 你先听我 | 975 |
| 先别急这 | 702 |
| 听我的这 | 702 |
| 我看看这 | 702 |

### Length 5

| Prefix | Count |
| --- | --- |
| 先把范围缩 | 2340 |
| 先收集证据 | 2340 |
| 别被报错吓 | 2340 |
| 我们捋一下 | 2340 |
| 我真的不想 | 2340 |
| 你认真的这 | 702 |
| 先停一下这 | 702 |
| 先看现象这 | 702 |
| 按步骤来这 | 702 |
| 真的假的这 | 702 |
| 这事能修这 | 702 |
| 先别急这个 | 624 |
| 听我的这个 | 624 |
| 我看看这个 | 624 |
| 今天先啃这 | 585 |

### Length 6

| Prefix | Count |
| --- | --- |
| 先把范围缩小 | 2340 |
| 别被报错吓到 | 2340 |
| 我真的不想多 | 2340 |
| 先收集证据这 | 702 |
| 我们捋一下这 | 702 |
| 你认真的这个 | 624 |
| 先停一下这个 | 624 |
| 先看现象这个 | 624 |
| 按步骤来这个 | 624 |
| 真的假的这个 | 624 |
| 这事能修这个 | 624 |
| 今天先啃这一 | 585 |
| 你又不是学不 | 585 |
| 先从一小步来 | 585 |
| 先把目标放近 | 585 |

## Suffix distribution

### Length 4

| Suffix | Count |
| --- | --- |
| 下文看全 | 3600 |
| 个新问题 | 3600 |
| 交别又忘 | 3600 |
| 再动代码 | 3600 |
| 再谈优雅 | 3600 |
| 出列出来 | 3600 |
| 又不催你 | 3600 |
| 因记下来 | 3600 |
| 把它钉住 | 3600 |
| 来就重写 | 3600 |
| 脑补靠谱 | 3600 |
| 跑通再说 | 3600 |
| 那么吓人 | 3600 |
| 点就够了 | 901 |
| 一遍试试 | 900 |

### Length 6

| Suffix | Count |
| --- | --- |
| 一上来就重写 | 3600 |
| 事实再动代码 | 3600 |
| 入输出列出来 | 3600 |
| 出三个新问题 | 3600 |
| 和上下文看全 | 3600 |
| 复现跑通再说 | 3600 |
| 就没那么吓人 | 3600 |
| 得提交别又忘 | 3600 |
| 把原因记下来 | 3600 |
| 来我又不催你 | 3600 |
| 正确再谈优雅 | 3600 |
| 比你脑补靠谱 | 3600 |
| 测试把它钉住 | 3600 |
| 一个点就算赚 | 900 |
| 儿呢你慢慢啃 | 900 |

### Length 8

| Suffix | Count |
| --- | --- |
| 一点就没那么吓人 | 3600 |
| 了记得提交别又忘 | 3600 |
| 保证正确再谈优雅 | 3600 |
| 写个测试把它钉住 | 3600 |
| 慢慢来我又不催你 | 3600 |
| 把输入输出列出来 | 3600 |
| 报错和上下文看全 | 3600 |
| 日志比你脑补靠谱 | 3600 |
| 最小复现跑通再说 | 3600 |
| 确认事实再动代码 | 3600 |
| 顺便把原因记下来 | 3600 |
| 题带出三个新问题 | 3600 |
| 了就歇会儿再继续 | 900 |
| 分钟状态自然会来 | 900 |
| 别人比进度烦不烦 | 900 |

### Length 10

| Suffix | Count |
| --- | --- |
| 个问题带出三个新问题 | 3600 |
| 住就把输入输出列出来 | 3600 |
| 修好了记得提交别又忘 | 3600 |
| 先把报错和上下文看全 | 3600 |
| 拆小一点就没那么吓人 | 3600 |
| 这次顺便把原因记下来 | 3600 |
| 不会就查没什么丢人的 | 900 |
| 你搞定了我给你说个6 | 900 |
| 做十分钟状态自然会来 | 900 |
| 做完最小的一步再加码 | 900 |
| 别只复制写你自己的话 | 900 |
| 别跟别人比进度烦不烦 | 900 |
| 在累了就歇会儿再继续 | 900 |
| 天能弄懂一个点就算赚 | 900 |
| 把它讲给我听一遍试试 | 900 |

## Duplicate and similarity examples

| Kind | Source | Text |
| --- | --- | --- |
| Near duplicate (0.909) | source line 1, source line 196 | 哈？这个空指针八成不是突然冒出来的，先把报错和上下文看全。 / 你认真的？这个空指针八成不是突然冒出来的，先把报错和上下文看全。 |
| Near duplicate (0.898) | source line 2, source line 197 | 哈？这个空指针八成不是突然冒出来的，别一上来就重写。 / 你认真的？这个空指针八成不是突然冒出来的，别一上来就重写。 |
| Near duplicate (0.902) | source line 3, source line 198 | 哈？这个空指针八成不是突然冒出来的，最小复现跑通再说。 / 你认真的？这个空指针八成不是突然冒出来的，最小复现跑通再说。 |
| Near duplicate (0.902) | source line 4, source line 199 | 哈？这个空指针八成不是突然冒出来的，日志比你脑补靠谱。 / 你认真的？这个空指针八成不是突然冒出来的，日志比你脑补靠谱。 |
| Near duplicate (0.909) | source line 5, source line 200 | 哈？这个空指针八成不是突然冒出来的，拆小一点就没那么吓人。 / 你认真的？这个空指针八成不是突然冒出来的，拆小一点就没那么吓人。 |
| Near duplicate (0.902) | source line 6, source line 201 | 哈？这个空指针八成不是突然冒出来的，写个测试把它钉住。 / 你认真的？这个空指针八成不是突然冒出来的，写个测试把它钉住。 |
| Near duplicate (0.906) | source line 7, source line 202 | 哈？这个空指针八成不是突然冒出来的，先保证正确，再谈优雅。 / 你认真的？这个空指针八成不是突然冒出来的，先保证正确，再谈优雅。 |
| Near duplicate (0.912) | source line 8, source line 203 | 哈？这个空指针八成不是突然冒出来的，卡住就把输入输出列出来。 / 你认真的？这个空指针八成不是突然冒出来的，卡住就把输入输出列出来。 |
| Near duplicate (0.906) | source line 9, source line 204 | 哈？这个空指针八成不是突然冒出来的，你慢慢来，我又不催你。 / 你认真的？这个空指针八成不是突然冒出来的，你慢慢来，我又不催你。 |
| Near duplicate (0.909) | source line 10, source line 205 | 哈？这个空指针八成不是突然冒出来的，修好了记得提交，别又忘。 / 你认真的？这个空指针八成不是突然冒出来的，修好了记得提交，别又忘。 |
| Near duplicate (0.906) | source line 11, source line 206 | 哈？这个空指针八成不是突然冒出来的，先确认事实再动代码。 / 你认真的？这个空指针八成不是突然冒出来的，先确认事实再动代码。 |
| Near duplicate (0.918) | source line 12, source line 207 | 哈？这个空指针八成不是突然冒出来的，别让一个问题带出三个新问题。 / 你认真的？这个空指针八成不是突然冒出来的，别让一个问题带出三个新问题。 |
| Near duplicate (0.915) | source line 13, source line 208 | 哈？这个空指针八成不是突然冒出来的，搞定这次顺便把原因记下来。 / 你认真的？这个空指针八成不是突然冒出来的，搞定这次顺便把原因记下来。 |
| Near duplicate (0.906) | source line 14, source line 209 | 哈？这个越界问题先盯住索引和长度，先把报错和上下文看全。 / 你认真的？这个越界问题先盯住索引和长度，先把报错和上下文看全。 |
| Near duplicate (0.894) | source line 15, source line 210 | 哈？这个越界问题先盯住索引和长度，别一上来就重写。 / 你认真的？这个越界问题先盯住索引和长度，别一上来就重写。 |
| Near duplicate (0.898) | source line 16, source line 211 | 哈？这个越界问题先盯住索引和长度，最小复现跑通再说。 / 你认真的？这个越界问题先盯住索引和长度，最小复现跑通再说。 |
| Near duplicate (0.898) | source line 17, source line 212 | 哈？这个越界问题先盯住索引和长度，日志比你脑补靠谱。 / 你认真的？这个越界问题先盯住索引和长度，日志比你脑补靠谱。 |
| Near duplicate (0.906) | source line 18, source line 213 | 哈？这个越界问题先盯住索引和长度，拆小一点就没那么吓人。 / 你认真的？这个越界问题先盯住索引和长度，拆小一点就没那么吓人。 |
| Near duplicate (0.898) | source line 19, source line 214 | 哈？这个越界问题先盯住索引和长度，写个测试把它钉住。 / 你认真的？这个越界问题先盯住索引和长度，写个测试把它钉住。 |
| Near duplicate (0.902) | source line 20, source line 215 | 哈？这个越界问题先盯住索引和长度，先保证正确，再谈优雅。 / 你认真的？这个越界问题先盯住索引和长度，先保证正确，再谈优雅。 |

## Flagged line examples

| Source | Category | Text |
| --- | --- | --- |
| source line 1 | Debugging | 哈？这个空指针八成不是突然冒出来的，先把报错和上下文看全。 |
| source line 2 | Debugging | 哈？这个空指针八成不是突然冒出来的，别一上来就重写。 |
| source line 3 | Debugging | 哈？这个空指针八成不是突然冒出来的，最小复现跑通再说。 |
| source line 4 | Debugging | 哈？这个空指针八成不是突然冒出来的，日志比你脑补靠谱。 |
| source line 5 | Debugging | 哈？这个空指针八成不是突然冒出来的，拆小一点就没那么吓人。 |
| source line 6 | Debugging | 哈？这个空指针八成不是突然冒出来的，写个测试把它钉住。 |
| source line 7 | Debugging | 哈？这个空指针八成不是突然冒出来的，先保证正确，再谈优雅。 |
| source line 8 | Debugging | 哈？这个空指针八成不是突然冒出来的，卡住就把输入输出列出来。 |
| source line 9 | Debugging | 哈？这个空指针八成不是突然冒出来的，你慢慢来，我又不催你。 |
| source line 10 | Debugging | 哈？这个空指针八成不是突然冒出来的，修好了记得提交，别又忘。 |
| source line 11 | Debugging | 哈？这个空指针八成不是突然冒出来的，先确认事实再动代码。 |
| source line 12 | Debugging | 哈？这个空指针八成不是突然冒出来的，别让一个问题带出三个新问题。 |
| source line 13 | Debugging | 哈？这个空指针八成不是突然冒出来的，搞定这次顺便把原因记下来。 |
| source line 14 | Debugging | 哈？这个越界问题先盯住索引和长度，先把报错和上下文看全。 |
| source line 15 | Debugging | 哈？这个越界问题先盯住索引和长度，别一上来就重写。 |
| source line 16 | Debugging | 哈？这个越界问题先盯住索引和长度，最小复现跑通再说。 |
| source line 17 | Debugging | 哈？这个越界问题先盯住索引和长度，日志比你脑补靠谱。 |
| source line 18 | Debugging | 哈？这个越界问题先盯住索引和长度，拆小一点就没那么吓人。 |
| source line 19 | Debugging | 哈？这个越界问题先盯住索引和长度，写个测试把它钉住。 |
| source line 20 | Debugging | 哈？这个越界问题先盯住索引和长度，先保证正确，再谈优雅。 |
