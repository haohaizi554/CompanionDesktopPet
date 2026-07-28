using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueEventPolicyTests
{
    private static readonly IReadOnlyDictionary<CompanionEvent, (bool Direct, bool Bypass)>
        Expected = new Dictionary<CompanionEvent, (bool, bool)>
    {
        [CompanionEvent.Startup] = (false, false),
        [CompanionEvent.Click] = (true, true),
        [CompanionEvent.Automatic] = (false, true),
        [CompanionEvent.DragReleased] = (true, true),
        [CompanionEvent.AnimationPaused] = (true, true),
        [CompanionEvent.AnimationResumed] = (true, true),
        [CompanionEvent.SizeChanged] = (true, true),
        [CompanionEvent.PositionRestored] = (true, true),
        [CompanionEvent.ClockTick] = (false, false),
        [CompanionEvent.DayChanged] = (false, false),
        [CompanionEvent.IdleReturned] = (false, false),
        [CompanionEvent.SystemUnlocked] = (false, false),
        [CompanionEvent.SleepResumed] = (false, false),
        [CompanionEvent.FullscreenChanged] = (false, false),
        [CompanionEvent.StoryTimerDue] = (false, false)
    };

    public static IEnumerable<object[]> EveryEvent => Expected.Select(pair => new object[]
    {
        pair.Key,
        pair.Value.Direct,
        pair.Value.Bypass
    });

    [Fact]
    public void PolicyTable_CoversEveryCompanionEventExactlyOnce()
    {
        var covered = Expected.Keys.ToArray();

        Assert.Equal(Enum.GetValues<CompanionEvent>(), covered);
        Assert.Equal(covered.Length, covered.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(EveryEvent))]
    public void Policy_ClassifiesDirectFeedbackAndInterruptionBypass(
        CompanionEvent trigger,
        bool expectedDirectFeedback,
        bool expectedBypass)
    {
        Assert.Equal(expectedDirectFeedback, DialogueEventPolicy.IsDirectFeedback(trigger));
        Assert.Equal(expectedBypass, DialogueEventPolicy.BypassesInterruptionBudget(trigger));
    }
}
