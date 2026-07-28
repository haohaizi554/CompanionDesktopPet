using CompanionDesktopPet.Services;
using System.Diagnostics;
using System.IO;

namespace CompanionDesktopPet.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class OfflineCompanionAgentTests
{
    [Fact]
    public void CreateSnapshot_ReturnsDetachedCharacterStateAndStoryCollection()
    {
        var now = new DateTime(2026, 7, 25, 10, 0, 0, DateTimeKind.Local);
        var agent = new OfflineCompanionAgent();
        agent.Respond(CompanionEvent.Click, now, new Random(20260725));
        var first = agent.CreateSnapshot();
        var expectedEnergy = first.State.Energy;
        var injectedStory = new StoryProgress("external-mutation", 0, now.AddHours(1));
        var firstHistory = Assert.IsType<SceneHistoryEntry[]>(first.History);
        var expectedHistoryEntry = Assert.Single(firstHistory);

        first.State.Energy = 0;
        first.State.ActiveStories = [.. first.State.ActiveStories, injectedStory];
        firstHistory[0] = expectedHistoryEntry with { SceneId = "external-mutation" };
        var second = agent.CreateSnapshot();
        var secondHistory = Assert.IsType<SceneHistoryEntry[]>(second.History);

        Assert.NotSame(first.State, second.State);
        Assert.NotSame(first.State.ActiveStories, second.State.ActiveStories);
        Assert.NotSame(firstHistory, secondHistory);
        Assert.Equal(expectedEnergy, second.State.Energy);
        Assert.DoesNotContain(injectedStory, second.State.ActiveStories);
        Assert.Equal(expectedHistoryEntry, Assert.Single(secondHistory));
    }

    [Fact]
    public void WarmUp_CatalogFailureThrowsDeterministicExceptionWithOriginalInnerException()
    {
        var originalFailure = new InvalidDataException("corrupt persona corpus");
        var fallback = SceneCatalog.BuildPersonaScenes(FallbackDialogueCatalog.All);
        var agent = new OfflineCompanionAgent(() => new SceneCatalogLoadResult(fallback, originalFailure));

        var exception = Assert.Throws<InvalidDataException>(agent.WarmUp);

        Assert.Equal(
            "The validated v2 persona corpus is unavailable; degraded dialogue cannot report ready.",
            exception.Message);
        Assert.Same(originalFailure, exception.InnerException);
    }

    [Fact]
    public void WarmUp_HealthyCatalogCompletesNormally()
    {
        var agent = new OfflineCompanionAgent(
            () => new SceneCatalogLoadResult(SceneCatalog.PersonaScenes, null));

        agent.WarmUp();
    }

    [Fact]
    public void SceneCatalog_AllScenesDisableCorpusDrivenAnimationCues()
    {
        Assert.All(SceneCatalog.All, scene => Assert.Equal("none", scene.AnimationCue));
    }

    [Fact]
    public void NextStoryDueAt_ExposesTheEarliestPendingStoryWithoutMutation()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var state = CharacterState.Create(now);
        state.ActiveStories =
        [
            new StoryProgress(StoryArcCatalog.All[0].Id, 1, now.AddHours(6)),
            new StoryProgress(StoryArcCatalog.All[1].Id, 1, now.AddHours(3))
        ];
        var snapshot = new AgentMemorySnapshot(state, [], 0, null, []);
        var agent = new OfflineCompanionAgent(snapshot);
        var service = new DialogueService(snapshot);

        Assert.Equal(now.AddHours(3), agent.NextStoryDueAt);
        Assert.Equal(now.AddHours(3), service.NextStoryDueAt);
        Assert.Equal(2, state.ActiveStories.Count);
    }

    [Fact]
    public void Respond_ClickImmediatelyAfterStartup_BypassesOnlyTheProactiveInterruptionBudget()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var agent = new OfflineCompanionAgent();
        var random = new Random(20260724);
        var startup = agent.Respond(CompanionEvent.Startup, now, random);

        var click = agent.Respond(CompanionEvent.Click, now.AddSeconds(1), random);

        Assert.True(startup.ShouldDisplayText);
        Assert.True(click.ShouldDisplayText);
        Assert.NotNull(click.SourceLine);
        Assert.NotEqual(startup.SourceLine!.Id, click.SourceLine!.Id);
        Assert.DoesNotContain(
            click.SourceLine.CategoryGroup,
            DialogueForest.BlockAdjacentCategoryGroups.Where(group => group == startup.SourceLine.CategoryGroup));
    }

    [Fact]
    public void Respond_AutomaticRemainsVisibleOneSecondAfterStartupUnderRecentHistoryPressure()
    {
        var pressure = CreateSafeFeedbackPressure();

        var reply = pressure.Agent.Respond(
            CompanionEvent.Automatic,
            pressure.Target,
            new Random(260705));

        AssertValidatedSafeReply(reply, pressure.PreviousText);
    }

    [Theory]
    [InlineData(CompanionEvent.Click)]
    [InlineData(CompanionEvent.DragReleased)]
    [InlineData(CompanionEvent.AnimationPaused)]
    [InlineData(CompanionEvent.AnimationResumed)]
    [InlineData(CompanionEvent.SizeChanged)]
    [InlineData(CompanionEvent.PositionRestored)]
    public void Respond_DirectFeedbackRemainsVisibleUnderRecentHistoryPressure(CompanionEvent trigger)
    {
        var pressure = CreateSafeFeedbackPressure();

        var reply = pressure.Agent.Respond(
            trigger,
            pressure.Target,
            new Random(260706 + (int)trigger));

        AssertValidatedSafeReply(reply, pressure.PreviousText);
    }

    [Fact]
    public void Respond_ClockTickRemainsSilentWhenRecentHistoryBlocksTheBudget()
    {
        var pressure = CreateSafeFeedbackPressure();

        var reply = pressure.Agent.Respond(
            CompanionEvent.ClockTick,
            pressure.Target,
            new Random(260707));

        Assert.False(reply.ShouldDisplayText);
        Assert.Equal("intentional_silence", reply.SceneId);
        Assert.Null(reply.SourceLine);
    }

    [Fact]
    public void Respond_EmitsOnlyEnabledV2LinesWithProvenanceAcrossEveryRoute()
    {
        var enabledById = PersonaCorpus.All.ToDictionary(line => line.Id);
        var agent = new OfflineCompanionAgent();
        var random = new Random(12345);
        var start = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Local);
        var triggers = new[]
        {
            CompanionEvent.Startup,
            CompanionEvent.Click,
            CompanionEvent.Automatic,
            CompanionEvent.ClockTick,
            CompanionEvent.DayChanged,
            CompanionEvent.IdleReturned
        };

        for (var turn = 0; turn < 240; turn++)
        {
            var reply = agent.Respond(triggers[turn % triggers.Length], start.AddHours(turn * 8), random);
            if (!reply.ShouldDisplayText)
            {
                continue;
            }

            Assert.NotNull(reply.SourceLine);
            Assert.True(enabledById.TryGetValue(reply.SourceLine!.Id, out var enabled));
            Assert.Same(enabled, reply.SourceLine);
            Assert.Equal(reply.SourceLine.Text, reply.Text);
            Assert.Equal(reply.SourceLine.Category, reply.Category);
            Assert.Equal(reply.SourceLine.SemanticGroup, reply.SemanticGroup);
            Assert.Equal("none", reply.AnimationCue);
        }
    }

    [Fact]
    public void Respond_MultiSeedPublishedOutputMeetsEasterEggPlaybackContract()
    {
        const int days = 30;
        var seeds = Enumerable.Range(0, 10).Select(index => 2026072500 + index).ToArray();
        var publicationHours = new[] { 5, 11, 17, 23 };
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);
        var enabledById = PersonaCorpus.All.ToDictionary(line => line.Id, StringComparer.Ordinal);
        var sceneById = SceneCatalog.All.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
        var allPlayback = new List<SceneHistoryEntry>(seeds.Length * days * publicationHours.Length);
        var seedRatios = new List<double>(seeds.Length);

        foreach (var seed in seeds)
        {
            var agent = new OfflineCompanionAgent();
            var random = new Random(seed);
            for (var day = 0; day < days; day++)
            {
                foreach (var hour in publicationHours)
                {
                    var reply = agent.Respond(
                        CompanionEvent.Automatic,
                        start.AddDays(day).AddHours(hour),
                        random);
                    Assert.True(reply.ShouldDisplayText, $"seed={seed} day={day} hour={hour}");
                    Assert.NotNull(reply.SourceLine);
                    Assert.Same(enabledById[reply.SourceLine!.Id], reply.SourceLine);
                    Assert.Contains(reply.SourceLine, sceneById[reply.SceneId].Lines);
                }
            }

            var seedPlayback = agent.CreateSnapshot().History.ToArray();
            Assert.Equal(days * publicationHours.Length, seedPlayback.Length);
            AssertEasterEggRecentQuota(seedPlayback, seed);
            var seedEasterEggRatio = seedPlayback.Count(entry =>
                entry.CategoryGroup == DialogueCategoryGroup.EasterEgg) / (double)seedPlayback.Length;
            Assert.InRange(
                seedEasterEggRatio,
                PersonaContractGenerated.EasterEggPlaybackMinimum,
                PersonaContractGenerated.EasterEggPlaybackMaximum);
            seedRatios.Add(seedEasterEggRatio);
            allPlayback.AddRange(seedPlayback);
        }

        var easterEggCount = allPlayback.Count(entry =>
            entry.CategoryGroup == DialogueCategoryGroup.EasterEgg);
        var easterEggRatio = easterEggCount / (double)allPlayback.Count;
        Console.WriteLine(
            $"C# runtime EasterEgg exposure: {easterEggCount}/{allPlayback.Count} "
            + $"({easterEggRatio:P2}); seeds={seeds.Length}; "
            + $"seed_range={seedRatios.Min():P2}-{seedRatios.Max():P2}; "
            + $"publication_hours={string.Join(',', publicationHours)}");

        Assert.InRange(
            easterEggRatio,
            PersonaContractGenerated.EasterEggPlaybackMinimum,
            PersonaContractGenerated.EasterEggPlaybackMaximum);
    }

    [Fact]
    public void Respond_NeverRepeatsBlockedGroupsAdjacentlyOrInsideSemanticCooldown()
    {
        var agent = new OfflineCompanionAgent();
        var random = new Random(72026);
        var start = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Local);

        for (var turn = 0; turn < 800; turn++)
        {
            agent.Respond(CompanionEvent.Automatic, start.AddHours(turn * 8), random);
        }

        var entries = agent.CreateSnapshot().History;
        var blocked = DialogueForest.BlockAdjacentCategoryGroups;
        for (var index = 1; index < entries.Count; index++)
        {
            Assert.False(
                entries[index].CategoryGroup == entries[index - 1].CategoryGroup
                && blocked.Contains(entries[index].CategoryGroup));
        }

        foreach (var group in entries.GroupBy(entry => entry.SemanticGroup))
        {
            var ordered = group.OrderBy(entry => entry.PlayedAt).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var scene = SceneCatalog.All.Single(item => item.Id == ordered[index].SceneId);
                Assert.True(ordered[index].PlayedAt - ordered[index - 1].PlayedAt >= scene.SemanticCooldown);
            }
        }
    }

    [Fact]
    public void Respond_DueStoryNodeStillUsesV2Provenance()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var arc = StoryArcCatalog.All[0];
        var state = CharacterState.Create(now);
        state.ActiveStories = [new StoryProgress(arc.Id, 1, now.AddMinutes(-1))];
        var snapshot = new AgentMemorySnapshot(state, [], 0, null, []);
        var reply = new OfflineCompanionAgent(snapshot)
            .Respond(CompanionEvent.StoryTimerDue, now, new Random(17));

        Assert.True(reply.ShouldDisplayText);
        Assert.NotNull(reply.SourceLine);
        Assert.Contains(reply.SourceLine, PersonaCorpus.All);
        Assert.Equal(arc.Id, SceneCatalog.All.Single(scene => scene.Id == reply.SceneId).StoryArcId);
    }

    [Fact]
    public void Snapshot_RetainsSelectedLineSchedulingMetadata()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var agent = new OfflineCompanionAgent();
        var reply = agent.Respond(CompanionEvent.Click, now, new Random(3));
        Assert.True(reply.ShouldDisplayText);

        var restored = new OfflineCompanionAgent(agent.CreateSnapshot());
        var entry = Assert.Single(restored.CreateSnapshot().History);
        Assert.Equal(reply.SourceLine!.Id, entry.DialogueLineId);
        Assert.Equal(reply.SourceLine.CategoryGroup, entry.CategoryGroup);
        Assert.Equal(reply.SourceLine.OutputMode, entry.OutputMode);
        Assert.Equal(reply.SourceLine.Trigger, entry.DialogueTrigger);
        Assert.Equal(reply.SourceLine.InterruptionCost, entry.InterruptionCost);
    }

    [Fact]
    public void Respond_WhenADueStoryIsBudgetBlocked_DefersItInsteadOfSpinning()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var arc = StoryArcCatalog.All[0];
        var previous = arc.Nodes[0].Lines[0];
        var state = CharacterState.Create(now.AddMinutes(-1));
        state.ActiveStories = [new StoryProgress(arc.Id, 1, now)];
        var history = new SceneHistoryEntry(
            arc.Nodes[0].Id,
            previous.SemanticGroup,
            now.AddMinutes(-1),
            previous.Text,
            previous.Id,
            previous.Category,
            previous.CategoryGroup,
            previous.OutputMode,
            previous.Trigger,
            previous.InterruptionCost,
            DateOnly.FromDateTime(now.AddMinutes(-1)));
        var agent = new OfflineCompanionAgent(new AgentMemorySnapshot(state, [history], 1, previous.Category, [previous.Text]));

        var reply = agent.Respond(CompanionEvent.StoryTimerDue, now, new Random(1));

        Assert.False(reply.ShouldDisplayText);
        Assert.True(Assert.Single(agent.CreateSnapshot().State.ActiveStories).DueAt > now);
    }

    [Fact]
    public void Respond_ClicksRemainLiveAcrossAnEightHourSession()
    {
        var agent = new OfflineCompanionAgent();
        var random = new Random(20260724);
        var start = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local);

        for (var minute = 0; minute <= 8 * 60; minute += 5)
        {
            var now = start.AddMinutes(minute);
            if (minute % 30 == 0)
            {
                agent.Respond(CompanionEvent.Automatic, now, random);
            }

            var click = agent.Respond(CompanionEvent.Click, now.AddSeconds(1), random);
            Assert.True(
                click.ShouldDisplayText,
                $"Click became silent at minute {minute}; scene={click.SceneId}.");
        }
    }

    [Fact]
    public void Respond_RepeatedClicksPreservePlaybackRulesAndAutomaticAvailability()
    {
        var run = RunRepeatedClickPlayback(clickCount: 900, measurePerformance: false);

        AssertPlaybackRules(run);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Respond_RepeatedClicksStayWithinSteadyStateBudget()
    {
        var run = RunRepeatedClickPlayback(clickCount: 900, measurePerformance: true);
        var orderedLatencies = run.ClickLatencies.Order().ToArray();
        var meanLatency = run.ClickLatencies.Average();
        var p95Latency = orderedLatencies[(int)Math.Ceiling(orderedLatencies.Length * 0.95) - 1];
        var p99Latency = orderedLatencies[(int)Math.Ceiling(orderedLatencies.Length * 0.99) - 1];
        var coldLatency = run.ClickLatencies[0];
        var warmMaximumLatency = run.ClickLatencies.Skip(1).Max();

        Console.WriteLine(
            $"900 clicks: mean={meanLatency:F3}ms p95={p95Latency:F3}ms "
            + $"p99={p99Latency:F3}ms cold={coldLatency:F3}ms warm_max={warmMaximumLatency:F3}ms "
            + $"agent_allocated={run.AllocatedBytes:N0} elapsed={run.Elapsed}");

        Assert.True(run.Elapsed < TimeSpan.FromSeconds(20), run.Elapsed.ToString());
        Assert.True(run.AllocatedBytes < 256L * 1024 * 1024, $"allocated bytes: {run.AllocatedBytes:N0}");
        Assert.True(p95Latency < 50, $"p95 click latency: {p95Latency:F3}ms");
        Assert.True(p99Latency < 100, $"p99 click latency: {p99Latency:F3}ms");
        Assert.True(coldLatency < 3_000, $"cold click latency: {coldLatency:F3}ms");
        Assert.True(warmMaximumLatency < 500, $"warm maximum click latency: {warmMaximumLatency:F3}ms");
    }

    private static RepeatedClickRun RunRepeatedClickPlayback(int clickCount, bool measurePerformance)
    {
        // Startup owns corpus/catalog materialization; the performance gate measures
        // the steady interactive path after that one-time prewarm.
        _ = SceneCatalog.All.Count;
        var agent = new OfflineCompanionAgent();
        var random = new Random(20260724);
        var start = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local);
        string? previousLineId = null;
        var usedDeepFallback = false;
        // CreateSnapshot deliberately copies the whole history. Keep a test-only
        // mirror from the actual replies so those observer allocations do not
        // pollute the measured Respond hot path.
        var observedHistory = new SceneHistory();
        long allocatedBytes = 0;
        var stopwatch = measurePerformance ? Stopwatch.StartNew() : null;
        var clickLatencies = measurePerformance ? new double[clickCount] : Array.Empty<double>();

        for (var clickIndex = 0; clickIndex < clickCount; clickIndex++)
        {
            var now = start.AddSeconds(clickIndex * 10);
            var historyBefore = observedHistory.Entries;
            var allocatedBeforeClick = measurePerformance ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var clickStarted = measurePerformance ? Stopwatch.GetTimestamp() : 0;
            var click = agent.Respond(
                CompanionEvent.Click,
                now,
                random);
            if (measurePerformance)
            {
                clickLatencies[clickIndex] = Stopwatch.GetElapsedTime(clickStarted).TotalMilliseconds;
                allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeClick;
            }
            Assert.True(
                click.ShouldDisplayText,
                $"Click became silent at index {clickIndex}; history={historyBefore.Count}, scene={click.SceneId}.");
            Assert.NotNull(click.SourceLine);
            Assert.True(click.SourceLine!.Enabled);
            Assert.NotEqual("intentional_silence", click.SceneId);
            Assert.NotEqual(previousLineId, click.SourceLine!.Id);

            var scene = SceneCatalog.All.Single(item => item.Id == click.SceneId);
            var previousSemantic = historyBefore.LastOrDefault(entry => entry.SemanticGroup == scene.SemanticGroup);
            var semanticWasCoolingDown = previousSemantic is not null
                                         && SceneHistory.Elapsed(now, previousSemantic.PlayedAt) < scene.SemanticCooldown;
            var previousLine = historyBefore.LastOrDefault(entry => entry.DialogueLineId == click.SourceLine.Id);
            var lineWasCoolingDown = previousLine is not null
                                     && SceneHistory.Elapsed(now, previousLine.PlayedAt) < click.SourceLine.Cooldown;
            var dailyCount = historyBefore.Count(entry =>
                entry.DialogueLineId == click.SourceLine.Id
                && (entry.PlayedLocalDate ?? DateOnly.FromDateTime(entry.PlayedAt)) == DateOnly.FromDateTime(now));

            if (clickIndex == 129)
            {
                Assert.False(semanticWasCoolingDown);
                Assert.False(lineWasCoolingDown);
                Assert.True(dailyCount < click.SourceLine.MaxPerDay);
            }

            usedDeepFallback |= semanticWasCoolingDown
                                || lineWasCoolingDown
                                || dailyCount >= click.SourceLine.MaxPerDay;
            previousLineId = click.SourceLine.Id;
            observedHistory.Record(scene, now, click.SourceLine);
        }

        stopwatch?.Stop();
        var playback = observedHistory.Entries.ToArray();
        var automatic = agent.Respond(
            CompanionEvent.Automatic,
            start.AddSeconds(clickCount * 10),
            random);

        return new RepeatedClickRun(
            playback,
            usedDeepFallback,
            automatic.ShouldDisplayText,
            automatic.SceneId,
            stopwatch?.Elapsed ?? TimeSpan.Zero,
            allocatedBytes,
            clickLatencies);
    }

    private static void AssertPlaybackRules(RepeatedClickRun run)
    {
        var playback = run.Playback;
        var seasoningRatio = playback.Count(entry => entry.WasSeasoning is true) / (double)playback.Length;
        var drySharpRatio = playback.Count(entry => entry.WasDrySharp) / (double)playback.Length;
        var openingRepeatRatio = RecentSurfaceRepeatRatio(playback, entry => entry.SurfaceOpening);
        var endingRepeatRatio = RecentSurfaceRepeatRatio(playback, entry => entry.SurfaceEnding);
        var templateRepeatRatio = RecentSurfaceRepeatRatio(playback, entry => entry.SurfaceTemplate);

        Assert.True(run.UsedDeepFallback);
        Assert.InRange(
            seasoningRatio,
            PersonaContractGenerated.SeasoningPlaybackMinimum,
            PersonaContractGenerated.SeasoningPlaybackMaximum);
        Assert.InRange(
            drySharpRatio,
            PersonaContractGenerated.DrySharpPlaybackMinimum,
            PersonaContractGenerated.DrySharpPlaybackMaximum);
        Assert.InRange(openingRepeatRatio, 0, 0.01);
        Assert.InRange(endingRepeatRatio, 0, 0.01);
        Assert.InRange(templateRepeatRatio, 0, 0.01);
        Assert.All(
            Enumerable.Range(0, playback.Length),
            index => Assert.True(
                playback.Skip(Math.Max(0, index - 19)).Take(Math.Min(20, index + 1))
                    .Count(entry => entry.WasSeasoning is true) <= PersonaContractGenerated.SeasoningRecentMaximum));
        Assert.All(
            Enumerable.Range(0, playback.Length),
            index => Assert.True(
                playback.Skip(Math.Max(0, index - 19)).Take(Math.Min(20, index + 1))
                    .Count(entry => entry.WasDrySharp) <= PersonaContractGenerated.DrySharpRecentMaximum));
        Assert.All(
            Enumerable.Range(0, playback.Length),
            index => Assert.True(
                playback.Skip(Math.Max(0, index - 49)).Take(Math.Min(50, index + 1))
                    .Count(entry => entry.WasDrySharp) <= SceneHistory.DrySharpPlaybackMaximum));
        Assert.True(run.AutomaticShouldDisplay);
        Assert.NotEqual("intentional_silence", run.AutomaticSceneId);
    }

    private sealed record RepeatedClickRun(
        SceneHistoryEntry[] Playback,
        bool UsedDeepFallback,
        bool AutomaticShouldDisplay,
        string AutomaticSceneId,
        TimeSpan Elapsed,
        long AllocatedBytes,
        double[] ClickLatencies);

    private static double RecentSurfaceRepeatRatio(
        IReadOnlyList<SceneHistoryEntry> entries,
        Func<SceneHistoryEntry, string> key)
    {
        var repeats = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var current = key(entries[index]);
            if (current.Length == 0)
            {
                continue;
            }
            if (entries
                .Skip(Math.Max(0, index - SurfaceExposure.RecentWindow))
                .Take(Math.Min(SurfaceExposure.RecentWindow, index))
                .Any(entry => key(entry) == current))
            {
                repeats++;
            }
        }
        return entries.Count == 0 ? 0 : repeats / (double)entries.Count;
    }

    private static (OfflineCompanionAgent Agent, DateTime Target, string PreviousText)
        CreateSafeFeedbackPressure()
    {
        var start = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var fresh = new OfflineCompanionAgent();
        var startup = fresh.Respond(CompanionEvent.Startup, start, new Random(260700));
        Assert.True(startup.ShouldDisplayText);
        Assert.NotNull(startup.SourceLine);

        var snapshot = fresh.CreateSnapshot();
        var history = new SceneHistory();
        history.Restore(snapshot.History);
        var scenes = SceneCatalog.PersonaScenes;
        for (var index = 0; index < scenes.Count; index++)
        {
            var playedAt = start.AddTicks(
                TimeSpan.TicksPerMillisecond * (1 + 800L * (index + 1) / (scenes.Count + 1)));
            history.Record(scenes[index], playedAt, scenes[index].Lines[0]);
        }

        var pressured = snapshot with { History = history.Entries.ToArray() };
        return (
            new OfflineCompanionAgent(pressured),
            start.AddSeconds(1),
            history.Entries[^1].Variant);
    }

    private static void AssertValidatedSafeReply(AgentReply reply, string previousText)
    {
        Assert.True(reply.ShouldDisplayText);
        Assert.NotEqual("intentional_silence", reply.SceneId);
        Assert.NotNull(reply.SourceLine);
        var source = PersonaCorpus.All.Single(line => line.Id == reply.SourceLine!.Id);
        Assert.Same(source, reply.SourceLine);
        var scene = SceneCatalog.PersonaScenes.Single(item => item.Id == reply.SceneId);
        Assert.True(reply.SourceLine.Enabled
                    && !reply.SourceLine.RequiresReply
                    && !reply.SourceLine.HasSeasoningMarker
                    && scene.StoryArcId is null
                    && scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
                    && scene.Tone != "dry_sharp"
                    && scene.OutputMode != DialogueOutputMode.UserDirect);
        Assert.Equal(reply.SourceLine.Text, reply.Text);
        Assert.NotEqual(previousText, reply.Text);
    }

    private static void AssertEasterEggRecentQuota(
        IReadOnlyList<SceneHistoryEntry> playback,
        int seed)
    {
        for (var index = 0; index < playback.Count; index++)
        {
            var windowStart = Math.Max(
                0,
                index - PersonaContractGenerated.EasterEggRecentWindow + 1);
            var easterEggCount = playback
                .Skip(windowStart)
                .Take(index - windowStart + 1)
                .Count(entry => entry.CategoryGroup == DialogueCategoryGroup.EasterEgg);
            Assert.True(
                easterEggCount <= PersonaContractGenerated.EasterEggRecentMaximum,
                $"seed={seed} output={index} EasterEggs={easterEggCount} "
                + $"in recent {PersonaContractGenerated.EasterEggRecentWindow}");
        }
    }

    [Fact]
    public async Task Respond_RestoredLegacyAbsorbingStateStillAnswersConsecutiveClicks()
    {
        var random = new Random(20260724);
        var start = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local);
        var original = new OfflineCompanionAgent();
        for (var clickIndex = 0; clickIndex < 129; clickIndex++)
        {
            var now = start.AddSeconds(clickIndex * 10);
            var historyBefore = new SceneHistory();
            historyBefore.Restore(original.CreateSnapshot().History);
            var reply = original.Respond(CompanionEvent.Click, now, random);

            Assert.True(reply.ShouldDisplayText);
            var scene = SceneCatalog.All.Single(item => item.Id == reply.SceneId);
            Assert.False(historyBefore.IsSemanticGroupCoolingDown(scene, now));
            Assert.True(historyBefore.MeetsAdjacencyAndRecentQuotas(scene));
            Assert.Contains(reply.SourceLine, historyBefore.EligibleLines(scene, now));
        }

        var healthy = original.CreateSnapshot();
        var legacyAbsorbingState = healthy with { TurnCount = 318 };
        Assert.Equal(129, legacyAbsorbingState.History.Count);
        Assert.Equal(64, legacyAbsorbingState.RecentLines.Count);
        Assert.True(legacyAbsorbingState.TurnCount > legacyAbsorbingState.History.Count);

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CompanionDesktopPet-click-liveness-{Guid.NewGuid():N}");
        try
        {
            var memory = new AgentMemoryService(directory);
            await memory.SaveAsync(legacyAbsorbingState);
            var loaded = Assert.IsType<AgentMemorySnapshot>(await memory.LoadAsync());
            var restored = new OfflineCompanionAgent(loaded);

            for (var clickIndex = 0; clickIndex < 20; clickIndex++)
            {
                var reply = restored.Respond(
                    CompanionEvent.Click,
                    start.AddSeconds((129 + clickIndex) * 10),
                    random);

                Assert.True(reply.ShouldDisplayText);
                Assert.NotNull(reply.SourceLine);
                Assert.True(reply.SourceLine!.Enabled);
                var restoredSnapshot = restored.CreateSnapshot();
                Assert.Equal(130 + clickIndex, restoredSnapshot.History.Count);
                Assert.Equal(reply.SourceLine.Id, restoredSnapshot.History[^1].DialogueLineId);
                Assert.Equal(reply.SceneId, restoredSnapshot.History[^1].SceneId);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
