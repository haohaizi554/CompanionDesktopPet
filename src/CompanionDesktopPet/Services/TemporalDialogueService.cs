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

    public static TimePeriod GetTimePeriod(DateTime dateTime) => dateTime.Hour switch
    {
        >= 4 and < 6 => TimePeriod.Dawn,
        >= 6 and < 11 => TimePeriod.Morning,
        >= 11 and < 14 => TimePeriod.Noon,
        >= 14 and < 18 => TimePeriod.Afternoon,
        >= 18 and < 23 => TimePeriod.Evening,
        _ => TimePeriod.LateNight,
    };

    internal static DialogueTrigger GetDialogueTrigger(DateTime dateTime) => GetTimePeriod(dateTime) switch
    {
        TimePeriod.Dawn => DialogueTrigger.LateNight,
        TimePeriod.Morning => DialogueTrigger.Morning,
        TimePeriod.Noon => DialogueTrigger.Noon,
        TimePeriod.Afternoon => DialogueTrigger.Afternoon,
        TimePeriod.Evening => DialogueTrigger.Evening,
        _ => DialogueTrigger.LateNight
    };

    internal static string GetTimeContextToken(DateTime dateTime) => GetTimePeriod(dateTime) switch
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
        var periodTrigger = GetDialogueTrigger(dateTime);
        var isHoliday = GetFestivals(dateTime).Count > 0;
        var context = new HashSet<string>(StringComparer.Ordinal)
        {
            dateTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "day:weekend" : "day:weekday",
            GetTimeContextToken(dateTime)
        };
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
