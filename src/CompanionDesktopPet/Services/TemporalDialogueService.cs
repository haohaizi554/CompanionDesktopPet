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

    private static readonly Lazy<IReadOnlyDictionary<ContextBucket, IReadOnlyList<string>>>
        ContextualLinesByBucket = new(
            BuildContextualLinesByBucket,
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static TimePeriod GetTimePeriod(DateTime dateTime) => dateTime.Hour switch
    {
        >= 4 and < 6 => TimePeriod.Dawn,
        >= 6 and < 11 => TimePeriod.Morning,
        >= 11 and < 14 => TimePeriod.Noon,
        >= 14 and < 18 => TimePeriod.Afternoon,
        >= 18 and < 23 => TimePeriod.Evening,
        _ => TimePeriod.LateNight,
    };

    internal static DialogueTrigger GetDialogueTrigger(DateTime dateTime) =>
        GetDialogueTrigger(GetTimePeriod(dateTime));

    private static DialogueTrigger GetDialogueTrigger(TimePeriod period) => period switch
    {
        TimePeriod.Dawn => DialogueTrigger.LateNight,
        TimePeriod.Morning => DialogueTrigger.Morning,
        TimePeriod.Noon => DialogueTrigger.Noon,
        TimePeriod.Afternoon => DialogueTrigger.Afternoon,
        TimePeriod.Evening => DialogueTrigger.Evening,
        _ => DialogueTrigger.LateNight
    };

    internal static string GetTimeContextToken(DateTime dateTime) =>
        GetTimeContextToken(GetTimePeriod(dateTime));

    private static string GetTimeContextToken(TimePeriod period) => period switch
    {
        TimePeriod.Dawn => "time:dawn",
        TimePeriod.Morning => "time:morning",
        TimePeriod.Noon => "time:noon",
        TimePeriod.Afternoon => "time:afternoon",
        TimePeriod.Evening => "time:evening",
        _ => "time:late_night"
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
        var period = GetTimePeriod(dateTime);
        var isHoliday = GetFestivals(dateTime).Count > 0;
        var bucket = new ContextBucket(
            period,
            dateTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            isHoliday);
        return ContextualLinesByBucket.Value[bucket];
    }

    private static IReadOnlyDictionary<ContextBucket, IReadOnlyList<string>>
        BuildContextualLinesByBucket()
    {
        var buckets = new Dictionary<ContextBucket, IReadOnlyList<string>>();
        foreach (var period in Enum.GetValues<TimePeriod>())
        {
            foreach (var isWeekend in new[] { false, true })
            {
                foreach (var isHoliday in new[] { false, true })
                {
                    var bucket = new ContextBucket(period, isWeekend, isHoliday);
                    buckets.Add(bucket, BuildContextualLines(bucket));
                }
            }
        }

        return buckets;
    }

    private static IReadOnlyList<string> BuildContextualLines(ContextBucket bucket)
    {
        var periodTrigger = GetDialogueTrigger(bucket.Period);
        var context = new HashSet<string>(StringComparer.Ordinal)
        {
            bucket.IsWeekend ? "day:weekend" : "day:weekday",
            GetTimeContextToken(bucket.Period)
        };
        if (bucket.IsHoliday)
        {
            context.Add("holiday");
            context.Add("date:holiday");
        }

        var lines = PersonaCorpus.All
            .Where(line => line.Trigger == DialogueTrigger.Any
                           || line.Trigger == periodTrigger
                           || (bucket.IsHoliday && line.Trigger == DialogueTrigger.Holiday))
            .Where(line => line.RequiredContext.Count == 1 && line.RequiredContext[0] == "none"
                           || line.RequiredContext.All(context.Contains))
            .Select(line => line.Text)
            .ToArray();
        return Array.AsReadOnly(lines);
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

    private readonly record struct ContextBucket(
        TimePeriod Period,
        bool IsWeekend,
        bool IsHoliday);
}
