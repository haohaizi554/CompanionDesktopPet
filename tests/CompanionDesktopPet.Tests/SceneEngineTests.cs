using System.Diagnostics;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class SceneEngineTests
{
    [Fact]
    public void Catalog_BuildsExactlyOnePersonaScenePerSemanticGroup()
    {
        var expectedGroups = PersonaCorpus.All.Select(line => line.SemanticGroup).Distinct().Order().ToArray();
        var actualGroups = SceneCatalog.PersonaScenes.Select(scene => scene.SemanticGroup).Order().ToArray();

        Assert.Equal(expectedGroups, actualGroups);
        Assert.Equal(actualGroups.Length, SceneCatalog.PersonaScenes.Select(scene => scene.Id).Distinct().Count());
        Assert.DoesNotContain(SceneCatalog.PersonaScenes, scene => scene.Variants.Count == 0);
    }

    [Fact]
    public void Catalog_MapsEverySceneFromItsV2LinesWithoutSyntheticText()
    {
        var enabledById = PersonaCorpus.All.ToDictionary(line => line.Id);

        Assert.All(SceneCatalog.All, scene =>
        {
            Assert.NotEmpty(scene.Lines);
            Assert.Equal(scene.Lines.Select(line => line.Text), scene.Variants);
            Assert.All(scene.Lines, line =>
            {
                Assert.True(enabledById.TryGetValue(line.Id, out var enabled));
                Assert.Same(enabled, line);
                Assert.Equal(scene.SemanticGroup, line.SemanticGroup);
                Assert.Equal(scene.Category, line.Category);
                Assert.Equal(scene.CategoryGroup, line.CategoryGroup);
                Assert.Equal(scene.OutputMode, line.OutputMode);
                Assert.Equal(scene.Tone, line.Tone);
            });
            Assert.Equal(scene.Lines[0].Cooldown, scene.Cooldown);
            Assert.Equal(scene.Lines[0].SemanticCooldown, scene.SemanticCooldown);
            Assert.Equal(scene.Lines[0].InterruptionCost, scene.InterruptionCost);
        });
    }

    [Fact]
    public void Catalog_MapsTheFourOutputModesDirectly()
    {
        Assert.All(SceneCatalog.PersonaScenes, scene => Assert.Equal(
            scene.OutputMode switch
            {
                DialogueOutputMode.SelfTalk => SceneExpression.SelfTalk,
                DialogueOutputMode.Ambient => SceneExpression.Ambient,
                DialogueOutputMode.UserDirect => SceneExpression.Direct,
                DialogueOutputMode.SystemObserve => SceneExpression.Ambient,
                _ => throw new ArgumentOutOfRangeException()
            },
            scene.Expression));
    }

    [Fact]
    public void History_UsesTheFullSemanticCooldownAndAllowsTheExactBoundary()
    {
        var scene = SceneCatalog.PersonaScenes.First();
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        history.Record(scene, now - scene.SemanticCooldown + TimeSpan.FromMinutes(1), scene.Lines[0]);

        Assert.True(history.IsSemanticGroupCoolingDown(scene, now));
        Assert.False(history.IsSemanticGroupCoolingDown(scene, now + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void History_RestoreFromItsOwnEntriesPreservesEntriesAndIndexes()
    {
        var scene = SceneCatalog.PersonaScenes.First();
        var line = scene.Lines[0];
        var playedAt = new DateTime(2026, 7, 25, 15, 0, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        history.Record(scene, playedAt, line);

        history.Restore(history.Entries);

        Assert.Equal(line.Id, Assert.Single(history.Entries).DialogueLineId);
        Assert.True(history.IsLineCoolingDown(line, playedAt.AddSeconds(1)));
    }

    [Fact]
    public void History_EntriesExposeAReadOnlyFacade()
    {
        var history = new SceneHistory();

        Assert.IsNotType<List<SceneHistoryEntry>>(history.Entries);
        Assert.False(history.Entries is ICollection<SceneHistoryEntry> { IsReadOnly: false });
    }

    [Fact]
    public void History_EnforcesIdDailyMaximumUsingThePlaybackLocalDate()
    {
        var scene = SceneCatalog.PersonaScenes.First();
        var line = scene.Lines[0];
        var firstLocalTime = new DateTime(2026, 7, 22, 23, 58, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        for (var index = 0; index < line.MaxPerDay; index++)
        {
            history.Record(scene, firstLocalTime.AddSeconds(index), line);
        }

        Assert.False(history.IsBelowDailyMaximum(line, firstLocalTime.AddMinutes(1)));
        Assert.True(history.IsBelowDailyMaximum(line, firstLocalTime.Date.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public void History_BlocksAdjacentSpecialGroupsAndCandidateAwareRecentQuotas()
    {
        var technical = SceneCatalog.PersonaScenes
            .Where(scene => scene.CategoryGroup == DialogueCategoryGroup.Technical)
            .Take(3)
            .ToArray();
        var life = SceneCatalog.PersonaScenes.First(scene => scene.CategoryGroup == DialogueCategoryGroup.CharacterLife);
        var now = new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        history.Record(technical[0], now.AddHours(-8), technical[0].Lines[0]);

        Assert.False(history.MeetsAdjacencyAndRecentQuotas(technical[1]));
        Assert.True(history.MeetsAdjacencyAndRecentQuotas(life));

        history.Record(life, now.AddHours(-6), life.Lines[0]);
        history.Record(technical[1], now.AddHours(-4), technical[1].Lines[0]);
        history.Record(life, now.AddHours(-2), life.Lines[0]);
        Assert.False(history.MeetsAdjacencyAndRecentQuotas(technical[2]));
    }

    [Fact]
    public void History_EasterEggQuotaImplementsTenPercentAndBlocksAdjacency()
    {
        Assert.Equal(10, SceneHistory.EasterEggRecentWindow);
        Assert.Equal(1, SceneHistory.EasterEggRecentMaximum);
        Assert.Contains(DialogueCategoryGroup.EasterEgg, DialogueForest.BlockAdjacentCategoryGroups);
    }

    [Fact]
    public void History_DrySharpQuotaUsesSemanticGroupsAndReleasesAtWindowBoundary()
    {
        Assert.Equal(20, SceneHistory.DrySharpRecentWindow);
        Assert.Equal(1, SceneHistory.DrySharpRecentMaximum);
        var dry = SceneCatalog.PersonaScenes.Where(scene => scene.Tone == "dry_sharp").Take(2).ToArray();
        var fillers = SceneCatalog.PersonaScenes
            .Where(scene => scene.CategoryGroup == DialogueCategoryGroup.CharacterLife && scene.Tone != "dry_sharp")
            .Take(19)
            .ToArray();
        Assert.Equal(2, dry.Length);
        Assert.Equal(19, fillers.Length);

        var history = new SceneHistory();
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        history.Record(dry[0], now.AddHours(-40), dry[0].Lines[0]);
        for (var index = 0; index < 18; index++)
        {
            history.Record(fillers[index], now.AddHours(-38 + index * 2), fillers[index].Lines[0]);
        }

        Assert.False(history.MeetsAdjacencyAndRecentQuotas(dry[1]));
        history.Record(fillers[18], now.AddHours(-1), fillers[18].Lines[0]);
        Assert.True(history.MeetsAdjacencyAndRecentQuotas(dry[1]));
    }

    [Fact]
    public void History_DrySharpQuotaSurvivesSceneRemovalOrRetoningAfterRestore()
    {
        const string retiredSemanticGroup = "retired.dry.scene";
        Assert.DoesNotContain(retiredSemanticGroup, SceneCatalog.DrySharpSemanticGroups);
        var candidate = SceneCatalog.PersonaScenes.First(scene => scene.Tone == "dry_sharp");
        var filler = SceneCatalog.PersonaScenes.First(scene =>
            scene.CategoryGroup == DialogueCategoryGroup.CharacterLife && scene.Tone != "dry_sharp");
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        var entries = new List<SceneHistoryEntry>
        {
            new(
                "retired-scene-id",
                retiredSemanticGroup,
                now.AddHours(-40),
                "retired variant",
                "retired-line-id",
                DialogueCategory.Career,
                DialogueCategoryGroup.Career,
                DialogueOutputMode.SelfTalk,
                DialogueTrigger.Any,
                1,
                DateOnly.FromDateTime(now),
                WasDrySharp: true)
        };
        entries.AddRange(Enumerable.Range(0, 18).Select(index => new SceneHistoryEntry(
            $"filler-{index}",
            $"filler.{index}",
            now.AddHours(-38 + index * 2),
            $"filler {index}",
            $"filler-line-{index}",
            filler.Category,
            filler.CategoryGroup,
            filler.OutputMode,
            filler.DialogueTrigger,
            filler.InterruptionCost,
            DateOnly.FromDateTime(now),
            WasDrySharp: false)));
        var history = new SceneHistory();
        history.Restore(entries);

        Assert.False(history.MeetsAdjacencyAndRecentQuotas(candidate));
    }

    [Fact]
    public void History_SeasoningQuotaFiltersTheSurfaceVariantAndReleasesAtWindowBoundary()
    {
        var basis = PersonaCorpus.All.First(line => line.CategoryGroup == DialogueCategoryGroup.CharacterLife);
        var spicy = basis with { Id = basis.Id + ".spicy", Text = "哈？这行先收一收。" };
        var neutral = basis with { Id = basis.Id + ".neutral", Text = "这行先安静地收一收。" };
        Assert.True(spicy.HasSeasoningMarker);
        Assert.False(neutral.HasSeasoningMarker);
        var scene = SceneCatalog.CreateScene("seasoning-candidate", [spicy, neutral]);
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        var entries = new List<SceneHistoryEntry>
        {
            new(
                "retired-seasoning",
                "retired.seasoning",
                now.AddHours(-40),
                "哈？旧句。",
                "retired-seasoning-line",
                basis.Category,
                basis.CategoryGroup,
                basis.OutputMode,
                basis.Trigger,
                basis.InterruptionCost,
                DateOnly.FromDateTime(now),
                WasSeasoning: true)
        };
        entries.AddRange(Enumerable.Range(0, 18).Select(index => new SceneHistoryEntry(
            $"neutral-{index}",
            $"neutral.{index}",
            now.AddHours(-38 + index * 2),
            $"neutral {index}",
            $"neutral-line-{index}",
            basis.Category,
            basis.CategoryGroup,
            basis.OutputMode,
            basis.Trigger,
            basis.InterruptionCost,
            DateOnly.FromDateTime(now),
            WasSeasoning: false)));
        var history = new SceneHistory();
        history.Restore(entries);

        Assert.Equal([neutral.Id], history.EligibleLines(scene, now).Select(line => line.Id));

        history.Restore([
            .. entries,
            new SceneHistoryEntry(
                "neutral-boundary",
                "neutral.boundary",
                now.AddMinutes(-1),
                "neutral boundary",
                "neutral-boundary-line",
                basis.Category,
                basis.CategoryGroup,
                basis.OutputMode,
                basis.Trigger,
                basis.InterruptionCost,
                DateOnly.FromDateTime(now),
                WasSeasoning: false)
        ]);
        Assert.Contains(spicy, history.EligibleLines(scene, now));
    }

    [Fact]
    public void History_SurfaceStageAvoidsRecentOpeningEndingAndTemplateBeforeFallback()
    {
        var basis = PersonaCorpus.All.First(line => line.CategoryGroup == DialogueCategoryGroup.CharacterLife);
        var played = basis with { Id = basis.Id + ".played", Text = "今天窗边有一点很轻的风。" };
        var repeated = basis with { Id = basis.Id + ".repeated", Text = "今天窗边换成了慢慢的雨。" };
        var fresh = basis with { Id = basis.Id + ".fresh", Text = "午后书页翻过一小段晴光。" };
        var playedScene = SceneCatalog.CreateScene("surface-played", [played]);
        var history = new SceneHistory();
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        history.Record(playedScene, now.AddHours(-2), played);

        var preferred = history.PreferSurfaceExposure([repeated, fresh]);

        Assert.Equal(fresh.Id, Assert.Single(preferred).Id);
        var persisted = Assert.Single(history.Entries);
        Assert.False(string.IsNullOrEmpty(persisted.SurfaceOpening));
        Assert.False(string.IsNullOrEmpty(persisted.SurfaceEnding));
        Assert.False(string.IsNullOrEmpty(persisted.SurfaceTemplate));
    }

    [Fact]
    public void History_LegacySnapshotInfersSeasoningFromPersistedVariantText()
    {
        var basis = PersonaCorpus.All.First(line => line.CategoryGroup == DialogueCategoryGroup.CharacterLife);
        var spicy = basis with { Id = basis.Id + ".legacy-spicy", Text = "哈？这句也先限流。" };
        var neutral = basis with { Id = basis.Id + ".legacy-neutral", Text = "这句安静地先限流。" };
        var scene = SceneCatalog.CreateScene("legacy-seasoning-candidate", [spicy, neutral]);
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        var entries = Enumerable.Range(0, 19)
            .Select(index => new SceneHistoryEntry(
                $"legacy-{index}",
                $"legacy.{index}",
                now.AddHours(-38 + index * 2),
                index == 0 ? "哈？旧快照没有新字段。" : $"旧快照中性句 {index}",
                $"legacy-line-{index}",
                basis.Category,
                basis.CategoryGroup,
                basis.OutputMode,
                basis.Trigger,
                basis.InterruptionCost,
                DateOnly.FromDateTime(now)))
            .ToArray();
        var history = new SceneHistory();
        history.Restore(entries);

        Assert.Equal([neutral.Id], history.EligibleLines(scene, now).Select(line => line.Id));
    }

    [Fact]
    public void ClickDeepFallbackPreservesEasterEggAndDrySharpRareQuotas()
    {
        var dry = SceneCatalog.PersonaScenes.Where(scene => scene.Tone == "dry_sharp").Take(2).ToArray();
        var easter = SceneCatalog.PersonaScenes
            .Where(scene => scene.CategoryGroup == DialogueCategoryGroup.EasterEgg)
            .Take(2)
            .ToArray();
        var fillers = SceneCatalog.PersonaScenes
            .Where(scene => scene.CategoryGroup == DialogueCategoryGroup.CharacterLife && scene.Tone != "dry_sharp")
            .Take(18)
            .ToArray();
        Assert.Equal(2, dry.Length);
        Assert.Equal(2, easter.Length);
        Assert.Equal(18, fillers.Length);

        var history = new SceneHistory();
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        history.Record(dry[0], now.AddHours(-40), dry[0].Lines[0]);
        for (var index = 0; index < 9; index++)
        {
            history.Record(fillers[index], now.AddHours(-38 + index * 2), fillers[index].Lines[0]);
        }
        history.Record(easter[0], now.AddHours(-18), easter[0].Lines[0]);
        for (var index = 9; index < 17; index++)
        {
            history.Record(fillers[index], now.AddHours(-16 + (index - 9) * 2), fillers[index].Lines[0]);
        }

        var selection = new SceneScheduler().SelectReusableClickFallback(
            [dry[1], easter[1], fillers[17]],
            history,
            new Random(20260724));

        Assert.NotNull(selection);
        Assert.Equal(fillers[17].Id, selection!.Scene.Id);
    }

    [Fact]
    public void SceneWeight_IsSemanticWeightAndDoesNotGrowWithVariantCount()
    {
        var line = PersonaCorpus.All.First(item => item.CategoryGroup == DialogueCategoryGroup.Technical);
        var one = SceneCatalog.CreateScene("one", [line]);
        var second = line with { Id = line.Id + ".second", Text = line.Text + " 第二种说法。" };
        var two = SceneCatalog.CreateScene("two", [line, second]);

        Assert.Equal(one.Weight, two.Weight);
        Assert.Equal(line.Weight, two.Weight);
    }

    [Fact]
    public void Scheduler_WeightCannotMovePeersAcrossTheScoreBandFloor()
    {
        var basis = PersonaCorpus.All.First(line =>
            line.CategoryGroup == DialogueCategoryGroup.CharacterLife
            && line.OutputMode == DialogueOutputMode.SelfTalk
            && line.InterruptionCost == 0
            && line.Tone != "dry_sharp"
            && line.Weight == 1);
        var lightLine = basis with
        {
            Id = basis.Id + ".score-band-light",
            SemanticGroup = basis.SemanticGroup + ".score-band-light",
            Weight = 0.1,
            Text = basis.Text + " light"
        };
        var heavyLine = basis with
        {
            Id = basis.Id + ".score-band-heavy",
            SemanticGroup = basis.SemanticGroup + ".score-band-heavy",
            Text = basis.Text + " heavy"
        };
        var light = SceneCatalog.CreateScene("a-score-band-light", [lightLine]);
        var heavy = SceneCatalog.CreateScene("z-score-band-heavy", [heavyLine]);

        var selection = new SceneScheduler().SelectReusableClickFallback(
            [light, heavy],
            new SceneHistory(),
            new Random(14));

        Assert.NotNull(selection);
        Assert.Equal(light.Id, selection!.Scene.Id);
    }

    [Fact]
    public void SceneWeight_RejectsInconsistentSemanticVariants()
    {
        var line = PersonaCorpus.All.First(item => item.CategoryGroup == DialogueCategoryGroup.Technical);
        var inconsistent = line with
        {
            Id = line.Id + ".bad-weight",
            Text = line.Text + " 权重不一致。",
            Weight = line.Weight / 2
        };

        Assert.Throws<InvalidOperationException>(() =>
            SceneCatalog.CreateScene("bad", [line, inconsistent]));
    }

    [Fact]
    public void Catalog_RejectsCategoryToneAndSafetyFlagDriftWithinASemanticScene()
    {
        var line = PersonaCorpus.All.First(item => item.CategoryGroup == DialogueCategoryGroup.Technical);
        var changes = new[]
        {
            line with { Category = Enum.GetValues<DialogueCategory>().First(value => value != line.Category) },
            line with { Tone = line.Tone == "dry" ? "gentle" : "dry" },
            line with { RequiresReply = !line.RequiresReply },
            line with { Enabled = !line.Enabled }
        };

        foreach (var changed in changes)
        {
            var inconsistent = changed with
            {
                Id = line.Id + ".inconsistent." + changed.GetHashCode(),
                Text = line.Text + " metadata drift"
            };
            Assert.Throws<InvalidOperationException>(() =>
                SceneCatalog.CreateScene("bad", [line, inconsistent]));
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Catalog_RebuildsExpandedRuntimeInventoryWithinDesktopStartupBudget()
    {
        var corpus = PersonaCorpus.All;
        var before = RetainedMemoryMeasurement.Snapshot();
        var stopwatch = Stopwatch.StartNew();

        var scenes = SceneCatalog.BuildPersonaScenes(corpus);

        stopwatch.Stop();
        var retainedBytes = RetainedMemoryMeasurement.Snapshot() - before;
        Assert.Equal(corpus.Select(line => line.SemanticGroup).Distinct().Count(), scenes.Count);
        Assert.Equal(corpus.Count, scenes.Sum(scene => scene.Lines.Count));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), stopwatch.Elapsed.ToString());
        Assert.True(
            retainedBytes >= 0,
            $"retained-memory measurement was invalid: {retainedBytes:N0} bytes");
        Assert.True(retainedBytes < 128L * 1024 * 1024, $"retained bytes: {retainedBytes:N0}");
        GC.KeepAlive(corpus);
        GC.KeepAlive(scenes);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ClickSelection_WithMaximumRetainedHistoryStaysWithinInteractiveBudget()
    {
        var scenes = SceneCatalog.PersonaScenes
            .Where(scene => scene.Triggers.Contains(CompanionEvent.Click))
            .Where(scene => scene.CategoryGroup != DialogueCategoryGroup.EasterEgg && scene.Tone != "dry_sharp")
            .Take(24)
            .ToArray();
        Assert.Equal(24, scenes.Length);
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        for (var index = 0; index < 2_000; index++)
        {
            var scene = scenes[index % scenes.Length];
            var line = scene.Lines[index % scene.Lines.Count];
            history.Record(scene, now.AddMinutes(-2_100 + index), line);
        }
        var context = new SceneContext(CompanionEvent.Click, now, CharacterState.Create(now));
        var scheduler = new SceneScheduler();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 20; index++)
        {
            Assert.NotNull(scheduler.Select(context, history, new Random(index), bypassInterruptionBudget: true));
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public void StoryArcs_UseOnlyEnabledV2Lines()
    {
        var enabledIds = PersonaCorpus.All.Select(line => line.Id).ToHashSet(StringComparer.Ordinal);

        Assert.True(StoryArcCatalog.All.Count >= 10);
        Assert.All(StoryArcCatalog.All, arc =>
        {
            Assert.True(arc.Nodes.Count >= 3);
            Assert.All(arc.Nodes.SelectMany(node => node.Lines), line => Assert.Contains(line.Id, enabledIds));
        });
    }

    [Fact]
    public void StoryArcs_ReserveTheirSourcePersonaScenesFromOrdinaryCandidates()
    {
        var now = new DateTime(2026, 7, 25, 10, 0, 0, DateTimeKind.Local);
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));
        var candidates = SceneScheduler.AvailableScenes(context).ToArray();
        var ordinaryGroups = candidates
            .Where(scene => scene.StoryArcId is null)
            .Select(scene => scene.SemanticGroup)
            .ToHashSet(StringComparer.Ordinal);
        var storyStarts = candidates
            .Where(scene => scene.StoryNode == 0)
            .ToArray();

        Assert.NotEmpty(StoryArcCatalog.ReservedPersonaSemanticGroups);
        Assert.Empty(ordinaryGroups.Intersect(StoryArcCatalog.ReservedPersonaSemanticGroups));
        Assert.Equal(StoryArcCatalog.All.Count, storyStarts.Length);
        Assert.All(storyStarts, scene =>
            Assert.Contains(scene.SemanticGroup, StoryArcCatalog.ReservedPersonaSemanticGroups));
    }

    [Fact]
    public void InterruptionBudget_UsesCandidateCostAndLateNightLimit()
    {
        var now = new DateTime(2026, 7, 22, 23, 30, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        var scene = SceneCatalog.PersonaScenes.First(item => item.InterruptionCost >= 1);
        history.Record(scene, now.AddMinutes(-20), scene.Lines[0]);

        Assert.False(InterruptionBudget.CanPlay(scene, now, history, isFullscreen: false));
        Assert.True(InterruptionBudget.CanPlay(scene, now.AddHours(1), history, isFullscreen: false));
    }

    [Fact]
    public void SceneContext_LeavesFullscreenUnknownByDefault()
    {
        var property = typeof(SceneContext).GetProperty(nameof(SceneContext.IsFullscreen));
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now));

        Assert.Equal(typeof(bool?), property!.PropertyType);
        Assert.Null(property.GetValue(context));
        Assert.DoesNotContain("not_fullscreen", ContextTokens(context));
    }

    [Fact]
    public void SceneContext_AddsNotFullscreenOnlyForAnExplicitFalseSignal()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now),
            IsFullscreen: false);

        Assert.Contains("not_fullscreen", ContextTokens(context));
    }

    [Fact]
    public void EffectiveFullscreen_ActivatesFullscreenBudgetWithoutInventingRawTokens()
    {
        var now = new DateTime(2026, 7, 26, 15, 0, 0, DateTimeKind.Local);
        var recentScene = SceneCatalog.PersonaScenes.First(scene => scene.Lines.Count > 0);
        var history = new SceneHistory();
        history.Record(recentScene, now.AddMinutes(-90), recentScene.Lines[0]);
        var activeContext = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now));
        var quietContext = activeContext with { EffectiveFullscreen = true };

        Assert.DoesNotContain("not_fullscreen", ContextTokens(quietContext));
        Assert.NotNull(new SceneScheduler().Select(activeContext, history, new Random(2605)));
        Assert.Null(new SceneScheduler().Select(quietContext, history, new Random(2605)));
    }

    [Theory]
    [InlineData(CompanionEvent.Click)]
    [InlineData(CompanionEvent.DragReleased)]
    [InlineData(CompanionEvent.AnimationPaused)]
    [InlineData(CompanionEvent.AnimationResumed)]
    [InlineData(CompanionEvent.SizeChanged)]
    [InlineData(CompanionEvent.PositionRestored)]
    public void DirectFeedbackEventsBypassInterruptionBudget(CompanionEvent trigger)
    {
        var now = new DateTime(2026, 7, 26, 15, 0, 0);
        var history = CreateRecentBudgetHistory(now);
        var context = new SceneContext(trigger, now, CharacterState.Create(now));

        var selected = new SceneScheduler().Select(
            context,
            history,
            new Random(2607),
            DialogueEventPolicy.BypassesInterruptionBudget(trigger));

        Assert.NotNull(selected);
    }

    [Theory]
    [InlineData(CompanionEvent.ClockTick)]
    [InlineData(CompanionEvent.DayChanged)]
    [InlineData(CompanionEvent.IdleReturned)]
    [InlineData(CompanionEvent.StoryTimerDue)]
    public void EventOutputsRemainBudgeted(CompanionEvent trigger)
    {
        var now = new DateTime(2026, 7, 26, 15, 0, 0);
        var history = CreateRecentBudgetHistory(now);
        var context = new SceneContext(trigger, now, CharacterState.Create(now));

        Assert.Null(new SceneScheduler().Select(context, history, new Random(2608)));
    }

    [Fact]
    public void AutomaticBypassesLegacyHourlyLateNightAndFullscreenBudgets()
    {
        var now = new DateTime(2026, 7, 26, 23, 30, 0, DateTimeKind.Local);
        var history = CreateRecentBudgetHistory(now);
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now),
            EffectiveFullscreen: true);

        var selected = new SceneScheduler().Select(
            context,
            history,
            new Random(2609),
            DialogueEventPolicy.BypassesInterruptionBudget(CompanionEvent.Automatic));

        Assert.NotNull(selected);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void SceneContext_UsesTheDawnTriggerAndOnlyTheDawnTimeToken(int hour)
    {
        var now = new DateTime(2026, 7, 22, hour, 30, 0);
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now));
        var tokens = ContextTokens(context);

        Assert.Equal(DialogueTrigger.LateNight, DayPart(now));
        Assert.Contains("time:dawn", tokens);
        Assert.DoesNotContain("time:morning", tokens);
        Assert.DoesNotContain("time:late_night", tokens);
    }

    [Theory]
    [InlineData(3, "time:late_night")]
    [InlineData(4, "time:dawn")]
    [InlineData(6, "time:morning")]
    [InlineData(11, "time:noon")]
    [InlineData(14, "time:afternoon")]
    [InlineData(18, "time:evening")]
    [InlineData(23, "time:late_night")]
    public void SceneContext_UsesExactlyOneCanonicalTimeToken(int hour, string expected)
    {
        var now = new DateTime(2026, 7, 22, hour, 30, 0);
        var context = new SceneContext(
            CompanionEvent.Automatic,
            now,
            CharacterState.Create(now));

        var timeToken = Assert.Single(
            ContextTokens(context),
            token => token.StartsWith("time:", StringComparison.Ordinal));

        Assert.Equal(expected, timeToken);
    }

    [Fact]
    public void ClickFallback_SelectsTheSceneBeforeApplyingLineRecencyWithinThatScene()
    {
        var scenes = SceneCatalog.PersonaScenes
            .Where(scene => scene.Triggers.Contains(CompanionEvent.Click))
            .Where(scene => scene.Lines.All(line => line.Enabled))
            .Where(scene => scene.CategoryGroup != DialogueCategoryGroup.EasterEgg && scene.Tone != "dry_sharp")
            .OrderBy(scene => scene.Lines.Count)
            .Take(2)
            .ToArray();
        Assert.Equal(2, scenes.Length);

        var firstHistory = new SceneHistory();
        var secondHistory = new SceneHistory();
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        for (var index = 0; index < scenes[0].Lines.Count; index++)
        {
            firstHistory.Record(scenes[0], now.AddHours(-8).AddSeconds(index), scenes[0].Lines[index]);
            secondHistory.Record(scenes[0], now.AddHours(-1).AddSeconds(index), scenes[0].Lines[index]);
        }
        for (var index = 0; index < scenes[1].Lines.Count; index++)
        {
            firstHistory.Record(scenes[1], now.AddHours(-1).AddSeconds(index), scenes[1].Lines[index]);
            secondHistory.Record(scenes[1], now.AddHours(-8).AddSeconds(index), scenes[1].Lines[index]);
        }

        var firstSelection = new SceneScheduler().SelectReusableClickFallback(
            scenes,
            firstHistory,
            new Random(20260724));
        var secondSelection = new SceneScheduler().SelectReusableClickFallback(
            scenes,
            secondHistory,
            new Random(20260724));

        Assert.NotNull(firstSelection);
        Assert.NotNull(secondSelection);
        Assert.Equal(firstSelection!.Scene.Id, secondSelection!.Scene.Id);
        Assert.Equal(
            firstSelection.Scene.Lines[0].Id,
            firstSelection.ReusedLine!.Id);
        Assert.Equal(
            secondSelection.Scene.Lines[0].Id,
            secondSelection.ReusedLine!.Id);
    }

    [Fact]
    public void SafeFeedback_RejectsEveryUnsafePredicateAndTheImmediatelyPreviousText()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var safe = SafeFeedbackLine("safe", "safe.group", "safe feedback");
        var unsafeScenes = new[]
        {
            SceneCatalog.CreateScene("disabled", [SafeFeedbackLine("disabled", "disabled.group", "disabled") with { Enabled = false }]),
            SceneCatalog.CreateScene("reply", [SafeFeedbackLine("reply", "reply.group", "reply") with { RequiresReply = true }]),
            SceneCatalog.CreateScene("easter", [SafeFeedbackLine("easter", "easter.group", "easter", DialogueCategoryGroup.EasterEgg)]),
            SceneCatalog.CreateScene("dry-sharp", [SafeFeedbackLine("dry-sharp", "dry-sharp.group", "dry sharp") with { Tone = "dry_sharp" }]),
            SceneCatalog.CreateScene("direct", [SafeFeedbackLine("direct", "direct.group", "direct") with { OutputMode = DialogueOutputMode.UserDirect }]),
            SceneCatalog.CreateScene("seasoning", [SafeFeedbackLine("seasoning", "seasoning.group", "666")]),
            SceneCatalog.CreateScene("story", [SafeFeedbackLine("story", "story.group", "story")], "arc", 0)
        };
        var previous = SceneCatalog.CreateScene(
            "previous",
            [SafeFeedbackLine("previous", "previous.group", safe.Text)]);
        var history = new SceneHistory();
        history.Record(previous, now.AddMinutes(-1), previous.Lines[0]);
        var scheduler = new SceneScheduler();
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));

        Assert.All(unsafeScenes, scene =>
            Assert.Null(scheduler.SelectSafeFeedback([scene], context, history, new Random(1))));
        Assert.Null(scheduler.SelectSafeFeedback(
            [SceneCatalog.CreateScene("duplicate", [safe])],
            context,
            history,
            new Random(1)));

        var selection = scheduler.SelectSafeFeedback(
            [.. unsafeScenes, SceneCatalog.CreateScene("duplicate", [safe]),
                SceneCatalog.CreateScene("valid", [safe with { Id = "valid", SemanticGroup = "valid.group", Text = "different safe feedback" }])],
            context,
            history,
            new Random(1));

        Assert.NotNull(selection);
        Assert.Equal("valid", selection!.Scene.Id);
        Assert.True(selection.Line.Enabled
                    && !selection.Line.RequiresReply
                    && !selection.Line.HasSeasoningMarker
                    && selection.Scene.StoryArcId is null
                    && selection.Scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
                    && selection.Scene.Tone != "dry_sharp"
                    && selection.Scene.OutputMode != DialogueOutputMode.UserDirect);
        Assert.NotEqual(history.Entries[^1].Variant, selection.Line.Text);
    }

    [Fact]
    public void SafeFeedback_RetainsTriggerContextAndDailyCapsInBothLayers()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var history = new SceneHistory();
        var capped = SceneCatalog.CreateScene(
            "capped",
            [SafeFeedbackLine("capped", "capped.group", "capped", maxPerDay: 1)]);
        history.Record(capped, now.AddHours(-1), capped.Lines[0]);
        var wrongTrigger = SceneCatalog.CreateScene(
            "wrong-trigger",
            [SafeFeedbackLine("wrong-trigger", "wrong-trigger.group", "wrong trigger") with
            {
                Trigger = DialogueTrigger.AppStart
            }]);
        var wrongContext = SceneCatalog.CreateScene(
            "wrong-context",
            [SafeFeedbackLine("wrong-context", "wrong-context.group", "wrong context") with
            {
                RequiredContext = ["not_fullscreen"]
            }]);
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));

        var selection = new SceneScheduler().SelectSafeFeedback(
            [capped, wrongTrigger, wrongContext],
            context,
            history,
            new Random(2));

        Assert.Null(selection);
    }

    [Fact]
    public void SafeFeedback_FirstLayerRetainsSemanticAndLineCooldownWhenAnOrdinarySafeAlternativeExists()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var semanticCooling = SceneCatalog.CreateScene(
            "semantic-cooling",
            [SafeFeedbackLine("semantic-cooling", "semantic-cooling.group", "semantic cooling", maxPerDay: 5) with
            {
                CooldownHours = 0
            }]);
        var lineCooling = SceneCatalog.CreateScene(
            "line-cooling",
            [SafeFeedbackLine("line-cooling", "line-cooling.group", "line cooling", maxPerDay: 5) with
            {
                SemanticCooldownHours = 0
            }]);
        var ordinary = SceneCatalog.CreateScene(
            "ordinary",
            [SafeFeedbackLine("ordinary", "ordinary.group", "ordinary", maxPerDay: 5) with
            {
                CooldownHours = 0,
                SemanticCooldownHours = 0
            }]);
        var history = new SceneHistory();
        history.Record(semanticCooling, now.AddDays(-1).AddMinutes(-1), semanticCooling.Lines[0]);
        history.Record(lineCooling, now.AddDays(-1), lineCooling.Lines[0]);
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));

        for (var seed = 0; seed < 32; seed++)
        {
            var selection = new SceneScheduler().SelectSafeFeedback(
                [semanticCooling, lineCooling, ordinary],
                context,
                history,
                new Random(seed));

            Assert.NotNull(selection);
            Assert.Equal(ordinary.Id, selection!.Scene.Id);
        }
    }

    [Fact]
    public void SafeFeedback_SecondLayerRelaxesOrdinaryAdjacencyButNotSafety()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var previous = SceneCatalog.CreateScene(
            "technical-previous",
            [SafeFeedbackLine("technical-previous", "technical.previous", "technical previous", DialogueCategoryGroup.Technical)]);
        var candidate = SceneCatalog.CreateScene(
            "technical-candidate",
            [SafeFeedbackLine("technical-candidate", "technical.candidate", "technical candidate", DialogueCategoryGroup.Technical)]);
        var history = new SceneHistory();
        history.Record(previous, now.AddDays(-1), previous.Lines[0]);
        Assert.False(history.MeetsAdjacencyAndRecentQuotas(candidate));

        var selection = new SceneScheduler().SelectSafeFeedback(
            [candidate],
            new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now)),
            history,
            new Random(3));

        Assert.NotNull(selection);
        Assert.Equal(candidate.Id, selection!.Scene.Id);
        Assert.Equal(candidate.Lines[0].Id, selection.Line.Id);
    }

    [Fact]
    public void SafeFeedback_PrefersUnusedThenLeastRecentLineWithinTheSelectedScene()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var lines = new[]
        {
            SafeFeedbackLine("oldest", "line-choice.group", "oldest", maxPerDay: 5),
            SafeFeedbackLine("middle", "line-choice.group", "middle", maxPerDay: 5),
            SafeFeedbackLine("unused", "line-choice.group", "unused", maxPerDay: 5)
        };
        var scene = SceneCatalog.CreateScene("line-choice", lines);
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));
        var historyWithUnused = new SceneHistory();
        historyWithUnused.Record(scene, now.AddDays(-1).AddHours(-2), lines[0]);
        historyWithUnused.Record(scene, now.AddDays(-1).AddHours(-1), lines[1]);

        var unused = new SceneScheduler().SelectSafeFeedback(
            [scene], context, historyWithUnused, new Random(4));

        Assert.NotNull(unused);
        Assert.Equal(lines[2].Id, unused!.Line.Id);

        var allUsed = new SceneHistory();
        allUsed.Record(scene, now.AddDays(-1).AddHours(-3), lines[0]);
        allUsed.Record(scene, now.AddDays(-1).AddHours(-2), lines[1]);
        allUsed.Record(scene, now.AddDays(-1).AddHours(-1), lines[2]);

        var leastRecent = new SceneScheduler().SelectSafeFeedback(
            [scene], context, allUsed, new Random(4));

        Assert.NotNull(leastRecent);
        Assert.Equal(lines[0].Id, leastRecent!.Line.Id);
    }

    [Fact]
    public void SafeFeedback_SelectsTheSceneBeforeApplyingLineRecencyWithinThatScene()
    {
        var now = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var first = SceneCatalog.CreateScene("a-first", [
            SafeFeedbackLine("a-first-1", "first.group", "first one", maxPerDay: 5),
            SafeFeedbackLine("a-first-2", "first.group", "first two", maxPerDay: 5)]);
        var second = SceneCatalog.CreateScene("z-second", [
            SafeFeedbackLine("z-second-1", "second.group", "second one", maxPerDay: 5),
            SafeFeedbackLine("z-second-2", "second.group", "second two", maxPerDay: 5)]);
        var firstOld = SafeFeedbackHistory(now, first, second);
        var secondOld = SafeFeedbackHistory(now, second, first);
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));

        var selectionA = new SceneScheduler().SelectSafeFeedback(
            [first, second], context, firstOld, new Random(14));
        var selectionB = new SceneScheduler().SelectSafeFeedback(
            [first, second], context, secondOld, new Random(14));

        Assert.NotNull(selectionA);
        Assert.NotNull(selectionB);
        Assert.Equal(selectionA!.Scene.Id, selectionB!.Scene.Id);
        Assert.Contains(selectionA.Line, selectionA.Scene.Lines);
        Assert.Contains(selectionB.Line, selectionB.Scene.Lines);
    }

    private static IReadOnlySet<string> ContextTokens(SceneContext context)
        => SceneScheduler.ContextTokens(context);

    private static SceneHistory CreateRecentBudgetHistory(DateTime now)
    {
        var history = new SceneHistory();
        var scenes = SceneCatalog.PersonaScenes
            .Where(scene => scene.Lines.Count > 0)
            .Take(InterruptionBudget.MaximumOutputsPerHour)
            .ToArray();
        for (var index = 0; index < scenes.Length; index++)
        {
            history.Record(scenes[index], now.AddMinutes(-2 - index), scenes[index].Lines[0]);
        }

        return history;
    }

    private static DialogueLine SafeFeedbackLine(
        string id,
        string semanticGroup,
        string text,
        DialogueCategoryGroup categoryGroup = DialogueCategoryGroup.CharacterLife,
        int maxPerDay = 1)
    {
        var basis = PersonaCorpus.All.First(line =>
            line.Enabled
            && !line.RequiresReply
            && !line.HasSeasoningMarker
            && line.CategoryGroup == DialogueCategoryGroup.CharacterLife
            && line.Tone != "dry_sharp"
            && line.OutputMode != DialogueOutputMode.UserDirect
            && line.Trigger == DialogueTrigger.Any
            && line.RequiredContext.SequenceEqual(["none"]));
        return basis with
        {
            Id = id,
            TopicId = id + ".topic",
            SemanticGroup = semanticGroup,
            CategoryGroup = categoryGroup,
            Text = text,
            MaxPerDay = maxPerDay,
            CooldownHours = 120,
            SemanticCooldownHours = 120,
            RequiresReply = false,
            Enabled = true
        };
    }

    private static SceneHistory SafeFeedbackHistory(
        DateTime now,
        SceneDefinition olderScene,
        SceneDefinition newerScene)
    {
        var history = new SceneHistory();
        for (var index = 0; index < olderScene.Lines.Count; index++)
        {
            history.Record(olderScene, now.AddDays(-1).AddHours(-3).AddSeconds(index), olderScene.Lines[index]);
        }
        for (var index = 0; index < newerScene.Lines.Count; index++)
        {
            history.Record(newerScene, now.AddDays(-1).AddHours(-1).AddSeconds(index), newerScene.Lines[index]);
        }
        return history;
    }

    private static DialogueTrigger DayPart(DateTime now)
        => InterruptionBudget.DayPart(now);
}
