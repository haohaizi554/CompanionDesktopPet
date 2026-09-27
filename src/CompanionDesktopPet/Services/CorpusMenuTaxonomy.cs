namespace CompanionDesktopPet.Services;

internal static class CorpusMenuTaxonomy
{
    private static readonly string[] Leads =
    [
        "换个轻松点的话题", "这事我还挺有感触", "有时候我会想", "说句心里话", "偷偷告诉你",
        "我突然想起", "想到这里", "我跟你讲", "你别笑我", "讲真的", "说起来", "偶尔吧",
        "反正啊", "我真的不想多说什么了", "先把范围缩小", "先收集证据", "我们捋一下",
        "按步骤来", "真的假的", "先停一下", "先别急", "这事能修", "先看现象", "听我的",
        "我看看啊", "我看看", "我靠", "我丢", "啊推", "嗯", "行吧", "先别摆烂", "先坐好",
        "你先听我说", "你先听我一句", "先听我说", "我说真的", "认真讲", "过来一点",
        "先歇半分钟", "我提醒你一下", "我还是要说", "今天先啃这一块", "按自己的节奏",
        "先把目标放近一点", "我陪你捋", "慢慢来", "行啦", "啊"
    ];

    private static readonly Dictionary<DialogueCategory, (string Title, string[] Keys)[]> Rules = new()
    {
        [DialogueCategory.ProactiveChat] =
        [
            ("英语", ["english", "phrasebook", "phonetic", "英文", "发音", "跟读"]),
            ("写代码", ["code", "debug", "debugging", "git", "terminal", "cache", "json", "csv", "api", "schema", "query", "syntax", "commit", "diff", "bugfix", "wireframe", "semver", "merge", "patch", "algorithm", "database", "bracket", "keyboard", "build", "代码", "终端", "调试", "函数", "堆栈"]),
            ("穿衣", ["skirt", "fabric", "coat", "zipper", "accessory", "outfit", "wardrobe", "shoelace", "yarn", "sock", "swatch", "braid", "raincoat", "comb", "hanger", "ribbon", "裙子", "衣柜", "发夹", "耳饰"]),
            ("吃喝", ["bread", "pantry", "chopstick", "icecream", "lemon", "cookie", "tea", "kettle", "rice", "soup", "spice", "fruit", "pasta", "tangerine", "seaweed", "soda", "sugar", "salt", "recipe", "mug", "spoon", "fork", "ladle", "strainer", "toaster", "steamer", "meal", "snack", "coffee", "厨房", "米饭"]),
            ("街上", ["city", "street", "bus", "tram", "ferry", "crosswalk", "sidewalk", "vending", "newsstand", "bakery", "shop", "market", "museum", "library", "bookstore", "park", "footbridge", "scooter", "bicycle", "parking", "booth", "suitcase", "ticket", "pavement", "curb", "bollard", "sweeper", "skewer", "arcade", "aquarium", "claw", "neighborhood", "canteen", "windshield", "chalk", "streetlamp", "flower", "walk", "route", "cafe", "elevator", "thrift", "camera", "movie", "telescope", "stall", "map", "城市", "街头", "公交", "便利店"]),
            ("天气", ["rain", "frost", "snow", "cloud", "umbrella", "wind", "humid", "weather", "fan", "下雨", "晚风"]),
            ("手作", ["origami", "clay", "bead", "puzzle", "boardgame", "marble", "watercolor", "music", "vinyl", "paint", "mosaic", "miniature", "stamp", "badminton", "tennis", "game", "印章", "折纸", "拼图"]),
            ("日子", ["budget", "receipt", "payday", "calendar", "achievement", "microtask", "postcard", "package", "confidence", "hope", "dream", "花费", "日历", "工资"]),
            ("纸笔", ["paper", "pencil", "notebook", "pen", "ink", "letter", "cork", "magnet", "tape", "clip", "ruler", "stationery", "desk", "eraser", "press", "铅笔", "笔记本"]),
            ("收拾", ["soap", "toothbrush", "bathroom", "towel", "broom", "mop", "dustpan", "doormat", "laundry", "mirror", "slipper", "shoe", "bag", "pillow", "blanket", "curtain", "被子", "肥皂", "毛巾"])
        ],
        [DialogueCategory.DressesHobbies] =
        [
            ("裙子", ["裙"]),
            ("英语", ["英文", "英语", "发音", "表达"]),
            ("打扮", ["发饰", "颜色", "打扮", "衣服", "照片"]),
            ("小爱好", ["爱好", "房间", "歌", "周末"])
        ],
        [DialogueCategory.WanderingLife] =
        [
            ("路上", ["路", "晚风", "风景"]),
            ("吃饭", ["米粉", "吃"]),
            ("工作与钱", ["工作", "赚", "钱"]),
            ("房间", ["房间", "收拾"]),
            ("心情", ["乐观", "开心", "自在"])
        ],
        [DialogueCategory.CharacterLife] =
        [
            ("书与歌", ["listening", "reading", "歌", "书", "旋律"]),
            ("英语", ["english", "英文"]),
            ("厨房", ["cooking", "kitchen", "杯", "厨房"]),
            ("天气", ["rain", "雨"]),
            ("手作", ["paper", "折"]),
            ("花钱", ["budget", "花钱"]),
            ("路上", ["daydream", "路灯", "傍晚"])
        ],
        [DialogueCategory.DailyCare] =
        [
            ("吃饭喝水", ["吃", "饭", "外卖", "零食", "餐", "咖啡", "食物", "杯"]),
            ("睡觉与休息", ["睡", "被窝", "枕头", "放松"]),
            ("桌面收拾", ["桌", "收拾", "归位", "充电", "钥匙"]),
            ("屏幕与身体", ["屏幕", "手腕", "肩", "姿势", "键盘", "眼睛"]),
            ("出门", ["太阳", "透气", "外套", "走走"]),
            ("工作收尾", ["收工", "工作", "明天", "清单", "任务"])
        ],
        [DialogueCategory.EmotionalSupport] =
        [
            ("允许停下", ["暂停", "休息", "歇一下", "空白"]),
            ("成长与比较", ["成长", "进度", "比较", "学习"]),
            ("犯错以后", ["犯错", "失败", "否定", "修正", "回滚"]),
            ("把压力说清", ["压力", "委屈", "耗空", "完美"]),
            ("照顾自己", ["肩膀", "心情", "勇敢"])
        ],
        [DialogueCategory.Career] =
        [
            ("面试求职", ["面试", "简历", "薪", "作品集", "岗位", "算法题", "自我介绍"]),
            ("协作", ["评审", "甩锅", "协作", "事实"]),
            ("路线", ["路线", "跳槽", "职业", "实习"]),
            ("入职", ["入职", "对齐", "负责人", "试用"])
        ],
        [DialogueCategory.Study] =
        [
            ("科目", ["编译", "操作系统", "网络", "离散", "线性代数", "概率", "数据结构", "数据库", "范式"]),
            ("英语", ["英语", "英文"]),
            ("笔记", ["笔记", "重写", "课堂", "复盘"]),
            ("怎么学", ["计划", "注意力", "解释", "进度", "错题"])
        ],
        [DialogueCategory.EnglishPractice] =
        [
            ("读与听", ["文档", "README", "听", "长句", "单词", "生词"]),
            ("开口", ["跟读", "开口", "发音", "口音"]),
            ("写下来", ["邮件", "commit", "写进"]),
            ("用在代码里", ["bug", "issue", "变量", "错误信息", "命名"])
        ],
        [DialogueCategory.EasterEgg] =
        [
            ("名字", ["nickname", "名贴", "昵称"]),
            ("裙子与打扮", ["skirt", "ribbon", "裙", "发饰"]),
            ("写代码", ["debug", "recursion", "commit", "代码", "递归", "函数"]),
            ("季节与纸页", ["maple", "bookmark", "叶子", "书签", "枫"])
        ],
        [DialogueCategory.SystemAmbient] =
        [
            ("编辑器", ["editor", "commit", "breakpoint", "linter", "code", "schema", "markdown", "font", "断点", "函数"]),
            ("桌面与窗口", ["window", "desktop", "dock", "monitor", "wallpaper", "widget", "split", "窗口", "桌面", "图标"]),
            ("状态与图表", ["status", "histogram", "profiler", "trace", "latency", "scrollbar", "状态栏"]),
            ("键盘与输入", ["keyboard", "shortcut", "keycap", "cli", "键帽", "快捷"]),
            ("安静的角落", ["quiet", "clock", "fan", "calendar", "日历"])
        ],
        [DialogueCategory.Architecture] =
        [
            ("边界与模块", ["模块", "职责", "公共库"]),
            ("接口与变化", ["接口", "契约", "版本", "兼容", "事件"]),
            ("失败与韧性", ["失败", "超时", "重试", "熔断", "降级"]),
            ("数据与缓存", ["一致性", "缓存", "所有权"]),
            ("取舍与演进", ["迁移", "重构", "技术债", "取舍", "选型", "抽象"]),
            ("观测与容量", ["可观测", "指标", "容量", "监控"])
        ],
        [DialogueCategory.Python] =
        [
            ("类型与对象", ["类型", "泛型", "数据类", "默认参数"]),
            ("异步与并发", ["异步", "await", "协程", "线程", "进程"]),
            ("测试", ["pytest", "夹具", "测试"]),
            ("异常与文件", ["异常", "except", "编码", "pathlib", "路径"]),
            ("包与环境", ["虚拟环境", "依赖", "导入", "模块"]),
            ("写法", ["推导", "装饰器", "生成器", "pandas", "序列化"])
        ],
        [DialogueCategory.Systems] =
        [
            ("进程与线程", ["进程", "线程", "僵尸", "信号"]),
            ("内存与调度", ["内存", "调度", "上下文切换", "CPU"]),
            ("文件与磁盘", ["磁盘", "句柄", "日志"]),
            ("权限与隔离", ["权限", "容器", "隔离"]),
            ("时间与观测", ["时钟", "时间测量", "负载"])
        ],
        [DialogueCategory.GitDevOps] =
        [
            ("分支与合并", ["分支", "合并", "rebase", "冲突", "提交"]),
            ("构建与发布", ["构建", "发布", "镜像", "回滚", "版本"]),
            ("流水线", ["流水线", "预检"]),
            ("配置与密钥", ["配置", "密钥", "依赖"]),
            ("监控与故障", ["监控", "告警", "故障", "健康"])
        ],
        [DialogueCategory.Algorithms] =
        [
            ("图与搜索", ["拓扑", "广度优先", "搜索树", "回溯", "图遍历"]),
            ("动态规划", ["动态规划", "贪心"]),
            ("窗口与区间", ["滑动窗口", "前缀和", "双指针", "单调栈", "区间"]),
            ("复杂度", ["复杂度", "输入规模"]),
            ("数据结构", ["数据结构", "并查集"]),
            ("正确性", ["不变量", "二分", "样例", "反例", "递归"])
        ],
        [DialogueCategory.Frontend] =
        [
            ("组件与状态", ["组件", "状态", "闭包", "路由"]),
            ("布局与样式", ["布局", "样式", "像素", "动画", "适配"]),
            ("性能与加载", ["首屏", "包体", "加载", "渲染"]),
            ("表单与交互", ["表单", "按钮", "校验"]),
            ("可访问性", ["可访问", "缩放"]),
            ("请求与缓存", ["请求", "缓存", "接口"])
        ],
        [DialogueCategory.Cpp] =
        [
            ("内存与对象", ["内存", "指针", "引用", "所有权", "拷贝", "移动", "析构"]),
            ("并发", ["线程", "原子"]),
            ("编译与模板", ["模板", "编译", "宏", "ABI"]),
            ("生命周期", ["生命周期", "越界", "未定义", "迭代器"])
        ],
        [DialogueCategory.Debugging] =
        [
            ("复现与假设", ["复现", "假设", "偶现"]),
            ("日志与堆栈", ["日志", "堆栈", "异常"]),
            ("环境与配置", ["配置", "乱码", "编码"]),
            ("并发与时间", ["并发", "时区", "卡死", "锁"]),
            ("数据与边界", ["空指针", "越界", "泄漏", "浮点"])
        ],
        [DialogueCategory.Backend] =
        [
            ("接口与校验", ["接口", "参数", "校验", "错误码"]),
            ("缓存与任务", ["缓存", "定时任务", "批处理"]),
            ("幂等与重试", ["幂等", "重试", "重复消费", "分布式锁"]),
            ("权限", ["鉴权", "权限"]),
            ("超时与降级", ["超时", "降级", "链路追踪"])
        ],
        [DialogueCategory.Database] =
        [
            ("索引与查询", ["索引", "查询", "分页", "联表"]),
            ("约束与模型", ["约束", "字段", "JSON"]),
            ("事务与锁", ["事务", "死锁"]),
            ("迁移与备份", ["迁移", "备份", "读写分离"])
        ],
        [DialogueCategory.Java] =
        [
            ("集合与类型", ["泛型", "集合", "Optional", "equals", "Stream"]),
            ("并发与事务", ["线程池", "事务", "锁范围"]),
            ("内存与运行", ["JVM", "内存", "空指针"]),
            ("框架与依赖", ["Spring", "Bean", "依赖"])
        ],
        [DialogueCategory.Networks] =
        [
            ("连接与传输", ["长连接", "心跳", "粘包", "重传", "TCP"]),
            ("超时与路由", ["超时", "DNS", "路由"]),
            ("安全", ["密钥", "证书", "HTTPS", "脱敏", "跨域"]),
            ("协议边界", ["限流", "协议", "签名"])
        ]
    };

    private static readonly Dictionary<DialogueCategory, string> Defaults = new()
    {
        [DialogueCategory.ProactiveChat] = "屋里的小东西",
        [DialogueCategory.DressesHobbies] = "小爱好",
        [DialogueCategory.WanderingLife] = "心情",
        [DialogueCategory.CharacterLife] = "日常",
        [DialogueCategory.DailyCare] = "把日子安顿好",
        [DialogueCategory.EmotionalSupport] = "把自己放轻",
        [DialogueCategory.Career] = "做事的节奏",
        [DialogueCategory.Study] = "慢慢弄懂",
        [DialogueCategory.EnglishPractice] = "把英语用起来",
        [DialogueCategory.EasterEgg] = "小彩蛋",
        [DialogueCategory.SystemAmbient] = "屏幕边上",
        [DialogueCategory.Architecture] = "结构取舍",
        [DialogueCategory.Python] = "写法与习惯",
        [DialogueCategory.Systems] = "系统边界",
        [DialogueCategory.GitDevOps] = "交付",
        [DialogueCategory.Algorithms] = "算法直觉",
        [DialogueCategory.Frontend] = "页面",
        [DialogueCategory.Cpp] = "语言细节",
        [DialogueCategory.Debugging] = "排查",
        [DialogueCategory.Backend] = "服务",
        [DialogueCategory.Database] = "数据",
        [DialogueCategory.Java] = "语言细节",
        [DialogueCategory.Networks] = "网络"
    };

    public static IEnumerable<string> Order(DialogueCategory category)
    {
        if (Rules.TryGetValue(category, out var rules))
        {
            foreach (var rule in rules)
            {
                yield return rule.Title;
            }
        }

        yield return Default(category);
    }

    public static string Assign(DialogueCategory category, string topicId, IReadOnlyList<DialogueLine> lines)
    {
        var slug = Slug(topicId);
        if (slug.Length > 0)
        {
            var (family, score) = Score(category, slug.Replace('_', ' '), latinWeight: 2);
            if (score > 0 && family is not null)
            {
                return family;
            }
        }

        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var (family, score) = Score(category, Clause(line.Text), latinWeight: 1);
            if (score > 0 && family is not null)
            {
                votes.TryGetValue(family, out var count);
                votes[family] = count + 1;
            }
        }

        string? best = null;
        var bestCount = 0;
        foreach (var pair in votes)
        {
            if (pair.Value > bestCount)
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }

        return best ?? Default(category);
    }

    private static string Default(DialogueCategory category) =>
        Defaults.TryGetValue(category, out var title) ? title : "随手记";

    private static (string? Family, int Score) Score(DialogueCategory category, string blob, int latinWeight)
    {
        if (!Rules.TryGetValue(category, out var rules))
        {
            return (null, 0);
        }

        string? best = null;
        var bestScore = 0;
        foreach (var rule in rules)
        {
            var score = 0;
            foreach (var key in rule.Keys)
            {
                if (HasHan(key))
                {
                    if (blob.Contains(key, StringComparison.Ordinal))
                    {
                        score++;
                    }
                }
                else if (LatinHit(blob, key))
                {
                    score += latinWeight;
                }
            }

            if (score > bestScore)
            {
                best = rule.Title;
                bestScore = score;
            }
        }

        return (best, bestScore);
    }

    private static string Slug(string topicId)
    {
        var leaf = topicId;
        var dot = topicId.LastIndexOf('.');
        if (dot >= 0 && dot < topicId.Length - 1)
        {
            leaf = topicId[(dot + 1)..];
        }

        if (leaf.Length > 2 && leaf[0] == 'b')
        {
            var split = leaf.IndexOf('_');
            if (split is > 1 and < 6 && leaf[1..split].All(char.IsDigit))
            {
                leaf = leaf[(split + 1)..];
            }
        }

        if (leaf.StartsWith("topic_", StringComparison.Ordinal))
        {
            var hexAt = leaf.LastIndexOf('_');
            if (hexAt > 0 && leaf.Length - hexAt > 6 && IsHex(leaf[(hexAt + 1)..]))
            {
                return string.Empty;
            }
        }

        return leaf;
    }

    private static string Clause(string text)
    {
        var value = text.Trim();
        foreach (var lead in Leads)
        {
            if (value.StartsWith(lead, StringComparison.Ordinal))
            {
                value = value[lead.Length..].TrimStart('，', ',', ' ');
                break;
            }
        }

        var cut = value.IndexOf('，');
        return cut > 0 ? value[..cut] : value;
    }

    private static bool LatinHit(string blob, string key)
    {
        var start = 0;
        while (start <= blob.Length - key.Length)
        {
            var index = blob.IndexOf(key, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var before = index == 0 || !IsLatin(blob[index - 1]);
            var afterAt = index + key.Length;
            var after = afterAt >= blob.Length || !IsLatin(blob[afterAt]);
            if (before && after)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static bool IsLatin(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsHex(string value) =>
        value.Length > 0 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HasHan(string value) =>
        value.Any(character => character is >= '\u4e00' and <= '\u9fff');
}
