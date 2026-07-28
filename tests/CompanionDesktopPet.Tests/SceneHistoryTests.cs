using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SceneHistoryTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 10, 0, 0);

    [Fact]
    public void Record_IndexesSceneSemanticGroupLineAndDailyMaximum()
    {
        var (scene, line) = CreateScene(maxPerDay: 1);
        var history = new SceneHistory();

        history.Record(scene, Now, line);

        Assert.Equal(Now, history.LastSceneAt);
        Assert.True(history.IsSceneCoolingDown(scene, Now.AddMinutes(1)));
        Assert.True(history.IsSemanticGroupCoolingDown(scene, Now.AddMinutes(1)));
        Assert.True(history.IsLineCoolingDown(line, Now.AddMinutes(1)));
        Assert.False(history.IsBelowDailyMaximum(line, Now));
    }

    [Fact]
    public void MeetsAdjacencyAndRecentQuotas_RejectsAdjacentBlockedCategoryGroup()
    {
        var (scene, line) = CreateScene();
        var history = new SceneHistory();
        history.Record(scene, Now, line);

        Assert.False(history.MeetsAdjacencyAndRecentQuotas(scene));
    }

    [Fact]
    public void Restore_TakesAnOrderedDetachedSnapshotAndRebuildsIndexes()
    {
        var entries = new List<SceneHistoryEntry>
        {
            Entry("later", Now.AddMinutes(2)),
            Entry("earlier", Now)
        };
        var history = new SceneHistory();

        history.Restore(entries);
        entries.Clear();

        Assert.Equal(["earlier", "later"], history.Entries.Select(entry => entry.SceneId));
        Assert.Equal(Now.AddMinutes(2), history.LastSceneAt);
        var snapshot = Assert.IsType<SceneHistoryEntry[]>(history.Entries);
        snapshot[0] = Entry("mutated", Now.AddHours(1));
        Assert.Equal("earlier", history.Entries[0].SceneId);
    }

    [Fact]
    public void EmptyHistory_HasNeutralBoundaries()
    {
        var history = new SceneHistory();

        Assert.Null(history.LastSceneAt);
        Assert.Null(history.LastEntry);
        Assert.Empty(history.SnapshotRecentEntries(10));
        Assert.Equal((0, 0), history.CountOutputsInPreviousHour(Now));
    }

    [Fact]
    public void Restore_KeepsOnlyTheNewestTwoThousandEntries()
    {
        var history = new SceneHistory();
        var entries = Enumerable.Range(0, 2_001)
            .Select(index => Entry($"scene-{index}", Now.AddMinutes(index)))
            .ToArray();

        history.Restore(entries);

        Assert.Equal(2_000, history.Entries.Count);
        Assert.Equal("scene-1", history.Entries[0].SceneId);
        Assert.Equal("scene-2000", history.Entries[^1].SceneId);
    }

    private static (SceneDefinition Scene, DialogueLine Line) CreateScene(int maxPerDay = 3)
    {
        var basis = SceneCatalog.PersonaScenes[0];
        var line = basis.Lines[0] with
        {
            Id = "history-line",
            SemanticGroup = "history.group",
            CategoryGroup = DialogueCategoryGroup.Technical,
            CooldownHours = 1,
            MaxPerDay = maxPerDay
        };
        var scene = basis with
        {
            Id = "history-scene",
            SemanticGroup = "history.group",
            CategoryGroup = DialogueCategoryGroup.Technical,
            Cooldown = TimeSpan.FromHours(1),
            SemanticCooldown = TimeSpan.FromHours(1),
            Lines = [line]
        };
        return (scene, line);
    }

    private static SceneHistoryEntry Entry(string sceneId, DateTime playedAt) =>
        new(sceneId, "history.group", playedAt, sceneId);
}
