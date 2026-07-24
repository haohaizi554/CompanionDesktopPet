using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

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
                Assert.Equal(scene.CategoryGroup, line.CategoryGroup);
                Assert.Equal(scene.OutputMode, line.OutputMode);
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
    public void ClickFallback_ChoosesTheGlobalLeastRecentlyUsedEnabledLineBeforeScoringItsScene()
    {
        var scenes = SceneCatalog.PersonaScenes
            .Where(scene => scene.Triggers.Contains(CompanionEvent.Click))
            .Where(scene => scene.Lines.All(line => line.Enabled))
            .OrderBy(scene => scene.Lines.Count)
            .Take(2)
            .ToArray();
        Assert.Equal(2, scenes.Length);

        var history = new SceneHistory();
        var now = new DateTime(2026, 7, 24, 15, 0, 0, DateTimeKind.Local);
        for (var index = 0; index < scenes[0].Lines.Count; index++)
        {
            history.Record(scenes[0], now.AddHours(-8).AddSeconds(index), scenes[0].Lines[index]);
        }
        for (var index = 0; index < scenes[1].Lines.Count; index++)
        {
            history.Record(scenes[1], now.AddHours(-1).AddSeconds(index), scenes[1].Lines[index]);
        }

        var selection = new SceneScheduler().SelectReusableClickFallback(
            scenes,
            history,
            new Random(20260724));

        Assert.NotNull(selection);
        Assert.Equal(scenes[0].Id, selection!.Scene.Id);
        Assert.Equal(scenes[0].Lines[0].Id, selection.ReusedLine!.Id);
    }

    private static IReadOnlySet<string> ContextTokens(SceneContext context)
    {
        var method = typeof(SceneScheduler).GetMethod(
            "ContextTokens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IReadOnlySet<string>>(method!.Invoke(null, [context]));
    }

    private static DialogueTrigger DayPart(DateTime now)
    {
        var method = typeof(InterruptionBudget).GetMethod(
            "DayPart",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<DialogueTrigger>(method!.Invoke(null, [now]));
    }
}
