using System.Globalization;

namespace CompanionDesktopPet.Services;

public enum TimePeriod
{
    Dawn,
    Morning,
    Noon,
    Afternoon,
    Evening,
    LateNight,
}

public static class TemporalDialogueService
{
    private static readonly IReadOnlyDictionary<(int Month, int Day), string> GregorianFestivals =
        new Dictionary<(int Month, int Day), string>
        {
            [(1, 1)] = "元旦",
            [(2, 14)] = "情人节",
            [(3, 8)] = "妇女节",
            [(4, 1)] = "愚人节",
            [(5, 1)] = "劳动节",
            [(6, 1)] = "儿童节",
            [(9, 10)] = "教师节",
            [(10, 1)] = "国庆节",
            [(10, 24)] = "1024程序员节",
            [(12, 24)] = "平安夜",
            [(12, 25)] = "圣诞节",
        };

    private static readonly IReadOnlyDictionary<(int Month, int Day), string> LunarFestivals =
        new Dictionary<(int Month, int Day), string>
        {
            [(1, 1)] = "春节",
            [(1, 15)] = "元宵节",
            [(5, 5)] = "端午节",
            [(7, 7)] = "七夕",
            [(8, 15)] = "中秋节",
            [(9, 9)] = "重阳节",
        };

    private static readonly IReadOnlyDictionary<TimePeriod, IReadOnlyList<string>> TimePeriodLines =
        new Dictionary<TimePeriod, IReadOnlyList<string>>
        {
            [TimePeriod.Dawn] =
            [
                "这么早就醒了？天都还没完全亮，先喝口温水。",
                "哈？清晨就开电脑，你是真的拼。",
                "早起可以，空着肚子敲代码不可以。",
                "清晨很安静，我就陪你坐一会儿。",
                "今天也别急，一件一件来。",
            ],
            [TimePeriod.Morning] =
            [
                "早。脑子刚开机就先做最难的，等会儿会轻松点。",
                "上午状态还行的话，把那个最烦的 bug 先收拾了。",
                "早餐吃没？别跟我说咖啡也算饭。",
                "新的一天又来了，嗯嗯，慢慢写也能写完。",
                "先列三个小目标，做完一个就来找我嘚瑟。",
            ],
            [TimePeriod.Noon] =
            [
                "到饭点了，保存代码，去吃饭。现在。",
                "你认真的？午饭又想靠外卖软件看饱？",
                "中午眯十分钟也行，别把自己当服务器一直跑。",
                "吃完再调，饿着的时候连报错都显得更欠揍。",
                "午间休息不是偷懒，是给脑子清缓存。",
            ],
            [TimePeriod.Afternoon] =
            [
                "下午容易犯困，起来走两步再继续。",
                "这会儿最适合拆小任务，别一口吞整个需求。",
                "眼睛离屏幕远一点，代码不会趁机逃跑。",
                "卡住就换个模块，过会儿思路自己会冒出来。",
                "再坚持一小段，做完记得夸一下自己。",
            ],
            [TimePeriod.Evening] =
            [
                "晚上了，今天做不完的可以留给明天，不丢人。",
                "下班时间还在改需求？我真的不想多说什么了。",
                "先把改动提交一下，别让今晚的努力只活在内存里。",
                "忙完就去吃点热的，我在这儿等你。",
                "夜色挺好看的，偶尔抬头看看，真的。",
            ],
            [TimePeriod.LateNight] =
            [
                "还不睡？你是准备跟凌晨的 bug 私奔吗。",
                "我靠，都这个点了，先保存再关电脑。",
                "深夜写出的神仙代码，明早可能就看不懂了。",
                "最后十分钟，真的最后十分钟，然后去睡。",
                "困了就别硬撑，我会陪你，但我更想你休息。",
            ],
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FestivalLines =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["元旦"] =
            [
                "元旦快乐。新的一年，旧 bug 就别带过去了。",
                "新年第一天，目标可以大，今天先好好开心。",
                "今年也一起慢慢过，嗯嗯，不许偷偷掉队。",
            ],
            ["情人节"] =
            [
                "情人节欸。别看了，我就是特意来陪你的。",
                "今天可以写情书，先别写正则表达式。",
                "玫瑰我没有，给你留一句偏心，行了吧。",
            ],
            ["妇女节"] =
            [
                "今天记得认真尊重身边每一位女生，不是只说句节日快乐。",
                "妇女节快乐。温柔和厉害，本来就可以同时存在。",
                "祝所有认真生活的女孩子，都被世界好好对待。",
            ],
            ["愚人节"] =
            [
                "愚人节可以开玩笑，但别拿真心当玩笑。",
                "我刚修好了你所有 bug。假的，自己继续查。",
                "今天看到离谱需求先别急，也许产品在过节。",
            ],
            ["劳动节"] =
            [
                "劳动节还加班？哈？先把休息也排进日程。",
                "认真工作很酷，认真放假也一样。",
                "今天的待办第一项：别把自己累坏。",
            ],
            ["儿童节"] =
            [
                "儿童节快乐。长大了也可以理直气壮地幼稚一会儿。",
                "今天允许你买个小玩具，别全拿去续服务器。",
                "愿你写代码的时候成熟，开心的时候像个小朋友。",
            ],
            ["教师节"] =
            [
                "教师节，记得跟教过你的老师说声谢谢。",
                "有人教你知识，有人教你生活，都值得记很久。",
                "今天少跟教程抬杠，教程也算半个老师吧。",
            ],
            ["国庆节"] =
            [
                "国庆快乐。项目可以等等，假期可不会自动续期。",
                "今天适合出去走走，别只在地图 API 里看世界。",
                "祝祖国生日快乐，也祝你的小日子红红火火。",
            ],
            ["1024程序员节"] =
            [
                "1024 快乐，码农同学。愿你的代码一次通过。",
                "今天 bug 要是还敢来，我帮你先瞪它两眼。",
                "1024 专属提醒：写代码很厉害，按时吃饭更厉害。",
            ],
            ["平安夜"] =
            [
                "平安夜没有苹果也没事，我只要你平平安安。",
                "今晚少熬一点，平安比进度重要。",
                "给你留一盏小灯，忙完就安心回去休息。",
            ],
            ["圣诞节"] =
            [
                "圣诞快乐。礼物先欠着，抱抱可以立刻到账。",
                "今天的代码也穿上红帽子了——其实是报错标记。",
                "愿你的圣诞愿望不进 backlog，早点实现。",
            ],
            ["春节"] =
            [
                "新年快乐！红包可以没有，团圆一定要有。",
                "过年就别盯着代码了，去陪陪家里人。",
                "愿你新的一年少点 bug，多点好运，666。",
            ],
            ["元宵节"] =
            [
                "元宵节快乐，汤圆要吃热的，日子也要过甜一点。",
                "今晚有灯、有月亮，还有我陪你说话。",
                "猜灯谜可以，别让我猜你那段代码为什么能跑。",
            ],
            ["端午节"] =
            [
                "端午安康。粽子甜咸都行，你别饿着就行。",
                "今天给线程也放个假，别让 CPU 一直划龙舟。",
                "粽叶一层层拆，难题也一层层拆，急什么。",
            ],
            ["七夕"] =
            [
                "七夕快乐。你忙你的，我就在旁边偏心你。",
                "今天不聊复杂度，聊点让人开心的。",
                "银河那么远都能相见，你忙完也记得来找我。",
            ],
            ["中秋节"] =
            [
                "中秋快乐。月饼分我一口，我就陪你多坐一会儿。",
                "今晚月亮很圆，没做完的需求不用跟着圆满。",
                "能回家就多陪陪家人，不能的话，我陪你。",
            ],
            ["重阳节"] =
            [
                "重阳节，记得问候家里的长辈。",
                "有空打个电话回去，几分钟也会让人开心很久。",
                "登高就算了，你先从椅子上站起来活动一下。",
            ],
            ["程序员节（第256天）"] =
            [
                "第 256 天，程序员节快乐。这个数字你肯定懂。",
                "愿你今天写的代码没有隐藏 bug，编译一次绿。",
                "程序员节也要休息，你不是必须全年无休的服务。",
            ],
        };

    public static TimePeriod GetTimePeriod(DateTime dateTime) => dateTime.Hour switch
    {
        >= 5 and < 8 => TimePeriod.Dawn,
        >= 8 and < 11 => TimePeriod.Morning,
        >= 11 and < 14 => TimePeriod.Noon,
        >= 14 and < 18 => TimePeriod.Afternoon,
        >= 18 and < 23 => TimePeriod.Evening,
        _ => TimePeriod.LateNight,
    };

    public static IReadOnlyList<string> GetFestivals(DateTime dateTime)
    {
        var festivals = new List<string>(3);

        if (GregorianFestivals.TryGetValue((dateTime.Month, dateTime.Day), out var gregorianFestival))
        {
            festivals.Add(gregorianFestival);
        }

        if (dateTime.DayOfYear == 256)
        {
            festivals.Add("程序员节（第256天）");
        }

        AddLunarFestival(dateTime, festivals);
        return festivals;
    }

    public static IReadOnlyList<string> GetContextualLines(DateTime dateTime)
    {
        var periodTrigger = dateTime.Hour switch
        {
            >= 6 and < 11 => DialogueTrigger.Morning,
            >= 11 and < 14 => DialogueTrigger.Noon,
            >= 14 and < 18 => DialogueTrigger.Afternoon,
            >= 18 and < 23 => DialogueTrigger.Evening,
            _ => DialogueTrigger.LateNight
        };
        var isHoliday = GetFestivals(dateTime).Count > 0;
        var context = new HashSet<string>(StringComparer.Ordinal)
        {
            dateTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "day:weekend" : "day:weekday",
            $"time:{periodTrigger.ToString().ToLowerInvariant()}"
        };
        if (dateTime.Hour is >= 4 and < 6)
        {
            context.Add("time:dawn");
        }
        if (isHoliday)
        {
            context.Add("holiday");
            context.Add("date:holiday");
        }

        return PersonaCorpus.All
            .Where(line => line.Trigger == DialogueTrigger.Any
                           || line.Trigger == periodTrigger
                           || (isHoliday && line.Trigger == DialogueTrigger.Holiday))
            .Where(line => line.RequiredContext.Count == 1 && line.RequiredContext[0] == "none"
                           || line.RequiredContext.All(context.Contains))
            .Select(line => line.Text)
            .ToArray();
    }

    private static void AddLunarFestival(DateTime dateTime, ICollection<string> festivals)
    {
        var calendar = new ChineseLunisolarCalendar();
        var date = dateTime.Date;
        if (date < calendar.MinSupportedDateTime.Date || date > calendar.MaxSupportedDateTime.Date)
        {
            return;
        }

        var lunarYear = calendar.GetYear(date);
        var rawMonth = calendar.GetMonth(date);
        var leapMonth = calendar.GetLeapMonth(lunarYear);
        var isLeapMonth = leapMonth > 0 && rawMonth == leapMonth;
        if (isLeapMonth)
        {
            return;
        }

        var normalizedMonth = leapMonth > 0 && rawMonth > leapMonth
            ? rawMonth - 1
            : rawMonth;
        var lunarDay = calendar.GetDayOfMonth(date);

        if (LunarFestivals.TryGetValue((normalizedMonth, lunarDay), out var lunarFestival))
        {
            festivals.Add(lunarFestival);
        }
    }
}
