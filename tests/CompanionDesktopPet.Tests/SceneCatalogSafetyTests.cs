using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SceneCatalogSafetyTests
{
    public static IEnumerable<object?[]> SafeAutomaticRuntimeScenarios()
    {
        var dates = new[]
        {
            new DateTime(2026, 7, 27), // weekday
            new DateTime(2026, 8, 1),  // weekend
            new DateTime(2026, 10, 1)  // holiday
        };
        var hours = new[] { 10, 20, 2, 5 };
        bool?[] fullscreenObservations = [null, false, true];
        foreach (var date in dates)
        {
            foreach (var hour in hours)
            {
                foreach (var observed in fullscreenObservations)
                {
                    yield return [date.AddHours(hour), observed];
                }
            }
        }
    }

    [Fact]
    public void LoadPersonaScenes_PrimaryFailureReturnsFallbackWithoutPoisoningTheType()
    {
        var expected = new InvalidDataException("broken embedded corpus");
        var fallback = PersonaCorpus.All.Take(1).ToArray();
        Exception? reported = null;

        var result = SceneCatalog.LoadPersonaScenes(
            () => throw expected,
            () => fallback,
            reportFailure: exception => reported = exception);

        Assert.Same(expected, result.Failure);
        Assert.Same(expected, reported);
        var scene = Assert.Single(result.Scenes);
        Assert.Equal(fallback[0].SemanticGroup, scene.SemanticGroup);
    }

    [Fact]
    public void StoryArcBuild_InsufficientFallbackScenesDisablesStoriesInsteadOfThrowing()
    {
        var fallbackScenes = SceneCatalog.BuildPersonaScenes(PersonaCorpus.All.Take(1).ToArray());

        var arcs = StoryArcCatalog.Build(fallbackScenes);

        Assert.Empty(arcs);
    }

    [Theory]
    [MemberData(nameof(SafeAutomaticRuntimeScenarios))]
    public void SafeFeedback_PublishedCorpusHasTwoLinesForEveryAutomaticRuntimeScenario(
        DateTime now,
        bool? observed)
    {
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now),
            IsFullscreen: observed,
            EffectiveFullscreen: observed is true);
        var history = new SceneHistory();
        var scheduler = new SceneScheduler();

        var first = scheduler.SelectSafeFeedback(
            SceneCatalog.PersonaScenes, context, history, new Random(260701));
        AssertSafeFeedback(first);
        history.Record(first!.Scene, now, first.Line);
        var second = scheduler.SelectSafeFeedback(
            SceneCatalog.PersonaScenes, context, history, new Random(260702));

        AssertSafeFeedback(second);
        Assert.NotEqual(first.Line.Text, second!.Line.Text);
    }

    [Theory]
    [InlineData(CompanionEvent.Click)]
    [InlineData(CompanionEvent.DragReleased)]
    [InlineData(CompanionEvent.AnimationPaused)]
    [InlineData(CompanionEvent.AnimationResumed)]
    [InlineData(CompanionEvent.SizeChanged)]
    [InlineData(CompanionEvent.PositionRestored)]
    public void SafeFeedback_PublishedCorpusHasTwoLinesForEveryDirectFeedbackEvent(CompanionEvent trigger)
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var context = new SceneContext(trigger, now, CharacterState.Create(now));
        var history = new SceneHistory();
        var scheduler = new SceneScheduler();

        var first = scheduler.SelectSafeFeedback(
            SceneCatalog.PersonaScenes, context, history, new Random(260703));
        AssertSafeFeedback(first);
        history.Record(first!.Scene, now, first.Line);
        var second = scheduler.SelectSafeFeedback(
            SceneCatalog.PersonaScenes, context, history, new Random(260704));

        AssertSafeFeedback(second);
        Assert.NotEqual(first.Line.Text, second!.Line.Text);
    }

    [Fact]
    public void SafeFeedbackCoverage_PublishedCorpusMeetsRuntimeCapacityContract()
    {
        SceneScheduler.ValidateSafeFeedbackCoverage(SceneCatalog.PersonaScenes);
    }

    [Fact]
    public void SafeFeedbackCoverage_OneGenericSceneFailsTheValidatedContract()
    {
        var line = SafeFeedbackLine("single", "single.group", "single safe line");
        var scene = SceneCatalog.CreateScene("single", [line]);

        Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage([scene]));
    }

    [Fact]
    public void SafeFeedbackCoverage_LongSilenceCapacityDoesNotCoverRecentAutomaticPressure()
    {
        var ordinaryFirst = SceneCatalog.CreateScene(
            "ordinary-first",
            [SafeFeedbackLine("ordinary-first", "ordinary.first", "ordinary first")]);
        var ordinarySecond = SceneCatalog.CreateScene(
            "ordinary-second",
            [SafeFeedbackLine("ordinary-second", "ordinary.second", "ordinary second")]);
        var longSilence = SceneCatalog.CreateScene(
            "long-silence",
            [SafeFeedbackLine("long-silence", "long.silence", "long silence") with
            {
                Trigger = DialogueTrigger.LongSilence,
                MaxPerDay = 144
            }]);

        Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(
                [ordinaryFirst, ordinarySecond, longSilence]));
    }

    [Fact]
    public void SafeFeedbackCoverage_DirectFeedbackRestrictedToWeekdayMorningFailsFullRuntimeMatrix()
    {
        var scenes = new[]
        {
            SafeFeedbackScene("automatic-daytime", 2, 72, DialogueTrigger.Morning),
            SafeFeedbackScene("automatic-evening", 2, 15, DialogueTrigger.Evening),
            SafeFeedbackScene("automatic-late", 2, 7, DialogueTrigger.LateNight),
            SafeFeedbackScene(
                "direct-weekday-morning",
                2,
                1,
                DialogueTrigger.Any,
                ["day:weekday", "time:morning"])
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(scenes));

        Assert.Contains(
            "Click at 2026-07-27 20:00 with fullscreen=unknown",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_SharedGenericCapacityCannotBeCountedForEveryDailyBand()
    {
        var genericCapacity = SafeFeedbackScene(
            "generic-shared-capacity",
            lineCount: 2,
            maxPerDay: 72);

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage([genericCapacity]));

        Assert.Contains("shared daily capacity", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 174; found 144", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_DayAndEveningPairNeedsEnoughNeighborCapacity()
    {
        var genericCapacity = SafeFeedbackScene(
            "pair-generic",
            lineCount: 2,
            maxPerDay: 15);
        var daytimeOnlyCapacity = SafeFeedbackScene(
            "pair-daytime",
            lineCount: 1,
            maxPerDay: 114,
            DialogueTrigger.Morning);
        var lateOnlyCapacity = SafeFeedbackScene(
            "pair-late",
            lineCount: 1,
            maxPerDay: 44,
            DialogueTrigger.LateNight);

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(
                [genericCapacity, daytimeOnlyCapacity, lateOnlyCapacity]));

        Assert.Contains("Daytime + Evening", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 174; found 144", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadPersonaScenes_CoverageFailureRecordsDegradedFallbackWithoutValidatingFallback()
    {
        var primary = SafeFeedbackLine("primary", "primary.group", "primary safe line");
        var fallback = SafeFeedbackLine("fallback", "fallback.group", "fallback safe line");

        var result = SceneCatalog.LoadPersonaScenes(
            () => [primary],
            () => [fallback],
            SceneScheduler.ValidateSafeFeedbackCoverage);

        Assert.IsType<InvalidDataException>(result.Failure);
        var fallbackScene = Assert.Single(result.Scenes);
        Assert.Equal(fallback.SemanticGroup, fallbackScene.SemanticGroup);
    }

    private static void AssertSafeFeedback(SafeFeedbackSelection? selection)
    {
        Assert.NotNull(selection);
        Assert.True(selection!.Line.Enabled
                    && !selection.Line.RequiresReply
                    && !selection.Line.HasSeasoningMarker
                    && selection.Scene.StoryArcId is null
                    && selection.Scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
                    && selection.Scene.Tone != "dry_sharp"
                    && selection.Scene.OutputMode != DialogueOutputMode.UserDirect);
    }

    private static SceneDefinition SafeFeedbackScene(
        string id,
        int lineCount,
        int maxPerDay,
        DialogueTrigger trigger = DialogueTrigger.Any,
        IReadOnlyList<string>? requiredContext = null)
    {
        var semanticGroup = id + ".group";
        var lines = Enumerable.Range(1, lineCount)
            .Select(index => SafeFeedbackLine(
                $"{id}-{index}",
                semanticGroup,
                $"{id} safe text {index}",
                maxPerDay) with
            {
                Trigger = trigger,
                RequiredContext = requiredContext ?? ["none"]
            })
            .ToArray();
        return SceneCatalog.CreateScene(id, lines);
    }

    private static DialogueLine SafeFeedbackLine(
        string id,
        string semanticGroup,
        string text,
        int maxPerDay = 1)
    {
        var basis = PersonaCorpus.All.First(line =>
            line.Enabled
            && !line.RequiresReply
            && !line.HasSeasoningMarker
            && line.CategoryGroup != DialogueCategoryGroup.EasterEgg
            && line.Tone != "dry_sharp"
            && line.OutputMode != DialogueOutputMode.UserDirect
            && line.Trigger == DialogueTrigger.Any
            && line.RequiredContext.SequenceEqual(["none"]));
        return basis with
        {
            Id = id,
            TopicId = id + ".topic",
            SemanticGroup = semanticGroup,
            Text = text,
            MaxPerDay = maxPerDay,
            RequiresReply = false,
            Enabled = true
        };
    }
}
