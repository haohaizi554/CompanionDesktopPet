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
    public void LoadPersonaScenes_DiagnosticFailureStillReturnsFallbackAndOriginalFailure()
    {
        var primaryFailure = new InvalidDataException("broken embedded corpus");
        var diagnosticFailure = new InvalidOperationException("trace unavailable");
        var fallback = PersonaCorpus.All.Take(1).ToArray();

        var result = SceneCatalog.LoadPersonaScenes(
            () => throw primaryFailure,
            () => fallback,
            reportFailure: _ => throw diagnosticFailure);

        Assert.Same(primaryFailure, result.Failure);
        Assert.Equal(fallback[0].SemanticGroup, Assert.Single(result.Scenes).SemanticGroup);
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
            "Click at 2200-03-03 05:00 with fullscreen=unknown",
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
        Assert.Contains("must be at least 148; found 144", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_DuplicateLineIdsUseMaximumDailyCapacityNotSum()
    {
        var primary = SafeFeedbackScene(
            "duplicate-capacity-primary",
            lineCount: 2,
            maxPerDay: 90);
        var duplicate = SceneCatalog.CreateScene(
            "duplicate-capacity-secondary",
            primary.Lines
                .Select(line => line with
                {
                    SemanticGroup = "duplicate-capacity-secondary.group",
                    Text = "secondary " + line.Text
                })
                .ToArray());

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage([primary, duplicate]));

        Assert.Contains("shared daily capacity", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 184; found 180", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_MorningAndNoonPairNeedsEnoughSharedCapacity()
    {
        var genericCapacity = SafeFeedbackScene(
            "pair-generic",
            lineCount: 2,
            maxPerDay: 30);
        var dawnOnlyCapacity = SafeFeedbackScene(
            "pair-dawn",
            lineCount: 2,
            maxPerDay: 2,
            DialogueTrigger.Any,
            ["time:dawn"]);
        var afternoonOnlyCapacity = SafeFeedbackScene(
            "pair-afternoon",
            lineCount: 2,
            maxPerDay: 24,
            DialogueTrigger.Any,
            ["time:afternoon"]);
        var eveningOnlyCapacity = SafeFeedbackScene(
            "pair-evening",
            lineCount: 2,
            maxPerDay: 15,
            DialogueTrigger.Any,
            ["time:evening"]);
        var lateOnlyCapacity = SafeFeedbackScene(
            "pair-late",
            lineCount: 2,
            maxPerDay: 5,
            DialogueTrigger.Any,
            ["time:late_night"]);

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(
                [
                    genericCapacity,
                    dawnOnlyCapacity,
                    afternoonOnlyCapacity,
                    eveningOnlyCapacity,
                    lateOnlyCapacity
                ]));

        Assert.Contains("Morning + Noon", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 96; found 60", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_NoNoonOrAfternoonRowsFailsTheFullRuntimeMatrix()
    {
        var scenes = new[]
        {
            SafeFeedbackScene("missing-noon-afternoon-morning", 2, 72, requiredContext: ["time:morning"]),
            SafeFeedbackScene("missing-noon-afternoon-evening", 2, 15, requiredContext: ["time:evening"]),
            SafeFeedbackScene("missing-noon-afternoon-late", 2, 7, requiredContext: ["time:late_night"]),
            SafeFeedbackScene("missing-noon-afternoon-dawn", 2, 7, requiredContext: ["time:dawn"])
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(scenes));

        Assert.Contains("Automatic", error.Message, StringComparison.Ordinal);
        Assert.Contains("11:00", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_LateNightCapacityCannotBeStrandedInDawnOnlyRows()
    {
        var scenes = CapacityScenes(
            morningPerLine: 72,
            noonPerLine: 18,
            afternoonPerLine: 24,
            eveningPerLine: 15,
            lateNightPerLine: 2,
            dawnPerLine: 5);

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(scenes));

        Assert.Contains("LateNight", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 10; found 4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_DawnCapacityCannotBeStrandedInLateNightOnlyRows()
    {
        var scenes = CapacityScenes(
            morningPerLine: 72,
            noonPerLine: 18,
            afternoonPerLine: 24,
            eveningPerLine: 15,
            lateNightPerLine: 6,
            dawnPerLine: 1);

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(scenes));

        Assert.Contains("Dawn", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be at least 4; found 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverage_SpringAndWinterAbsenceFailsTheFullRuntimeMatrix()
    {
        var scenes = new[] { "summer", "autumn" }
            .SelectMany(season => CapacityScenes(
                morningPerLine: 72,
                noonPerLine: 18,
                afternoonPerLine: 24,
                eveningPerLine: 15,
                lateNightPerLine: 5,
                dawnPerLine: 2,
                additionalContext: ["season:" + season]))
            .ToArray();

        var error = Assert.Throws<InvalidDataException>(() =>
            SceneScheduler.ValidateSafeFeedbackCoverage(scenes));

        Assert.Contains("Automatic", error.Message, StringComparison.Ordinal);
        Assert.Contains("2200-03", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFeedbackCoverageDates_UseEveryActualCalendarContextCombination()
    {
        var combinations = SceneScheduler.SafeFeedbackCoverageDates
            .Select(date =>
            {
                var context = new SceneContext(
                    CompanionEvent.Automatic,
                    date.AddHours(12),
                    CharacterState.Create(date.AddHours(12)));
                var tokens = SceneScheduler.ContextTokens(context);
                var season = tokens.Single(token => token.StartsWith("season:", StringComparison.Ordinal));
                var day = tokens.Single(token => token.StartsWith("day:", StringComparison.Ordinal));
                var holiday = TemporalDialogueService.GetFestivals(date).Count > 0;
                Assert.Equal(holiday, tokens.Contains("holiday"));
                Assert.Equal(holiday, tokens.Contains("date:holiday"));
                Assert.DoesNotContain("date:month_boundary", tokens);
                return (season, day, holiday);
            })
            .ToHashSet();

        Assert.Equal(16, combinations.Count);
        foreach (var season in new[] { "season:spring", "season:summer", "season:autumn", "season:winter" })
        {
            foreach (var day in new[] { "day:weekday", "day:weekend" })
            {
                Assert.Contains((season, day, false), combinations);
                Assert.Contains((season, day, true), combinations);
            }
        }
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

    private static IReadOnlyList<SceneDefinition> CapacityScenes(
        int morningPerLine,
        int noonPerLine,
        int afternoonPerLine,
        int eveningPerLine,
        int lateNightPerLine,
        int dawnPerLine,
        IReadOnlyList<string>? additionalContext = null)
    {
        var contextPrefix = additionalContext ?? [];
        return
        [
            SafeFeedbackScene("capacity-morning-" + string.Join('-', contextPrefix), 2, morningPerLine,
                requiredContext: [.. contextPrefix, "time:morning"]),
            SafeFeedbackScene("capacity-noon-" + string.Join('-', contextPrefix), 2, noonPerLine,
                requiredContext: [.. contextPrefix, "time:noon"]),
            SafeFeedbackScene("capacity-afternoon-" + string.Join('-', contextPrefix), 2, afternoonPerLine,
                requiredContext: [.. contextPrefix, "time:afternoon"]),
            SafeFeedbackScene("capacity-evening-" + string.Join('-', contextPrefix), 2, eveningPerLine,
                requiredContext: [.. contextPrefix, "time:evening"]),
            SafeFeedbackScene("capacity-late-" + string.Join('-', contextPrefix), 2, lateNightPerLine,
                requiredContext: [.. contextPrefix, "time:late_night"]),
            SafeFeedbackScene("capacity-dawn-" + string.Join('-', contextPrefix), 2, dawnPerLine,
                requiredContext: [.. contextPrefix, "time:dawn"])
        ];
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
