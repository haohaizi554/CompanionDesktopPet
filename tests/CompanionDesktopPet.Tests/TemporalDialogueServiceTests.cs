using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class TemporalDialogueServiceTests
{
    public static TheoryData<int, TimePeriod> TimePeriodBoundaries => new()
    {
        { 0, TimePeriod.LateNight },
        { 4, TimePeriod.LateNight },
        { 5, TimePeriod.Dawn },
        { 7, TimePeriod.Dawn },
        { 8, TimePeriod.Morning },
        { 10, TimePeriod.Morning },
        { 11, TimePeriod.Noon },
        { 13, TimePeriod.Noon },
        { 14, TimePeriod.Afternoon },
        { 17, TimePeriod.Afternoon },
        { 18, TimePeriod.Evening },
        { 22, TimePeriod.Evening },
        { 23, TimePeriod.LateNight },
    };

    [Theory]
    [MemberData(nameof(TimePeriodBoundaries))]
    public void GetTimePeriod_UsesStableHourBoundaries(int hour, TimePeriod expected)
    {
        var dateTime = new DateTime(2026, 7, 22, hour, 30, 0);

        Assert.Equal(expected, TemporalDialogueService.GetTimePeriod(dateTime));
    }

    public static TheoryData<DateTime, string> FixedGregorianFestivals => new()
    {
        { new DateTime(2026, 1, 1), "元旦" },
        { new DateTime(2026, 2, 14), "情人节" },
        { new DateTime(2026, 3, 8), "妇女节" },
        { new DateTime(2026, 4, 1), "愚人节" },
        { new DateTime(2026, 5, 1), "劳动节" },
        { new DateTime(2026, 6, 1), "儿童节" },
        { new DateTime(2026, 9, 10), "教师节" },
        { new DateTime(2026, 10, 1), "国庆节" },
        { new DateTime(2026, 12, 24), "平安夜" },
        { new DateTime(2026, 12, 25), "圣诞节" },
    };

    [Theory]
    [MemberData(nameof(FixedGregorianFestivals))]
    public void GetFestivals_RecognizesFixedGregorianDates(DateTime date, string festival)
    {
        Assert.Contains(festival, TemporalDialogueService.GetFestivals(date));
    }

    public static TheoryData<DateTime, string> LunarFestivals => new()
    {
        { new DateTime(2026, 2, 17), "春节" },
        { new DateTime(2026, 3, 3), "元宵节" },
        { new DateTime(2026, 6, 19), "端午节" },
        { new DateTime(2026, 8, 19), "七夕" },
        { new DateTime(2026, 9, 25), "中秋节" },
        { new DateTime(2026, 10, 18), "重阳节" },
    };

    [Theory]
    [MemberData(nameof(LunarFestivals))]
    public void GetFestivals_RecognizesChineseLunarFestivals(DateTime date, string festival)
    {
        Assert.Contains(festival, TemporalDialogueService.GetFestivals(date));
    }

    [Fact]
    public void GetFestivals_DoesNotTreatALeapLunarMonthAsTheRegularFestivalMonth()
    {
        Assert.Contains("重阳节", TemporalDialogueService.GetFestivals(new DateTime(2014, 10, 2)));
        Assert.DoesNotContain("重阳节", TemporalDialogueService.GetFestivals(new DateTime(2014, 11, 1)));
    }

    [Fact]
    public void GetFestivals_RecognizesBothProgrammerObservances()
    {
        Assert.Contains("程序员节（第256天）", TemporalDialogueService.GetFestivals(new DateTime(2024, 9, 12)));
        Assert.Contains("程序员节（第256天）", TemporalDialogueService.GetFestivals(new DateTime(2025, 9, 13)));
        Assert.Contains("1024程序员节", TemporalDialogueService.GetFestivals(new DateTime(2026, 10, 24)));
    }

    [Fact]
    public void GetFestivals_OutsideLunarCalendarRangeStillReturnsGregorianFestivals()
    {
        Assert.Contains("元旦", TemporalDialogueService.GetFestivals(new DateTime(2200, 1, 1)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(18)]
    [InlineData(23)]
    public void GetContextualLines_OffersSeveralUniqueLinesForEveryTimePeriod(int hour)
    {
        var lines = TemporalDialogueService.GetContextualLines(new DateTime(2026, 7, 22, hour, 0, 0));

        Assert.True(lines.Count >= 5);
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.DoesNotContain(lines, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void GetContextualLines_AddsFestivalSpecificCandidatesWithoutDroppingTimeCandidates()
    {
        var regularEveningCount = TemporalDialogueService
            .GetContextualLines(new DateTime(2026, 7, 22, 20, 0, 0))
            .Count;
        var festivalLines = TemporalDialogueService
            .GetContextualLines(new DateTime(2026, 10, 24, 20, 0, 0));

        Assert.True(festivalLines.Count > regularEveningCount);
        Assert.All(festivalLines, line =>
            Assert.Contains(line, PersonaCorpus.All.Select(item => item.Text)));
    }

    [Fact]
    public void GetContextualLines_IsDeterministicForTheInjectedDateTime()
    {
        var dateTime = new DateTime(2026, 9, 25, 21, 30, 0);

        Assert.Equal(
            TemporalDialogueService.GetContextualLines(dateTime),
            TemporalDialogueService.GetContextualLines(dateTime));
    }

    [Fact]
    public void GetContextualLines_ReturnsOnlyEnabledV2CorpusText()
    {
        var enabled = PersonaCorpus.All.Select(line => line.Text).ToHashSet(StringComparer.Ordinal);

        foreach (var dateTime in new[]
                 {
                     new DateTime(2026, 1, 1, 7, 0, 0),
                     new DateTime(2026, 7, 22, 15, 0, 0),
                     new DateTime(2026, 10, 24, 23, 0, 0)
                 })
        {
            Assert.All(TemporalDialogueService.GetContextualLines(dateTime), line => Assert.Contains(line, enabled));
        }
    }
}
