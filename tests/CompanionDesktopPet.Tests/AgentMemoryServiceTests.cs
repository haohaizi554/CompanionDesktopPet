using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class AgentMemoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsValidatedSnapshot()
    {
        var expected = CreateValidSnapshot();
        var service = new AgentMemoryService(_directory);

        await service.SaveAsync(expected);
        var loaded = await service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(expected.State.Energy, loaded!.State.Energy);
        Assert.Equal(expected.State.Mood, loaded.State.Mood);
        Assert.Equal(expected.State.Activity, loaded.State.Activity);
        Assert.Equal(expected.State.ActiveStories, loaded.State.ActiveStories);
        Assert.Equal(expected.History, loaded.History);
        Assert.Equal(expected.TurnCount, loaded.TurnCount);
        Assert.Equal(expected.LastCategory, loaded.LastCategory);
        Assert.Equal(expected.RecentLines, loaded.RecentLines);
        Assert.False(File.Exists(Path.Combine(_directory, "agent-memory.json.tmp")));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsFreshSnapshot()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var expected = new AgentMemorySnapshot(
            CharacterState.Create(now),
            History: [],
            TurnCount: 0,
            LastCategory: null,
            RecentLines: []);
        var service = new AgentMemoryService(_directory);

        await service.SaveAsync(expected);
        var loaded = await service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.History);
        Assert.Empty(loaded.RecentLines);
        Assert.Equal(0, loaded.TurnCount);
        Assert.Null(loaded.LastCategory);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsEmptyHistoryAfterSilentTurn()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var expected = new AgentMemorySnapshot(
            CharacterState.Create(now),
            History: [],
            TurnCount: 1,
            LastCategory: DialogueCategory.Python,
            RecentLines: []);
        var service = new AgentMemoryService(_directory);

        await service.SaveAsync(expected);
        var loaded = await service.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.History);
        Assert.Empty(loaded.RecentLines);
        Assert.Equal(1, loaded.TurnCount);
        Assert.Equal(DialogueCategory.Python, loaded.LastCategory);
    }

    [Fact]
    public async Task Load_MalformedMemoryFallsBackToNoSnapshot()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "agent-memory.json"),
            "{bad json");

        Assert.Null(await new AgentMemoryService(_directory).LoadAsync());
    }

    [Fact]
    public async Task Load_MissingOrUnknownMembersFallsBackToNoSnapshot()
    {
        await AssertRejectedAsync(
            root => root.Remove("State"),
            root => root["State"]!.AsObject().Remove("Energy"),
            root => root.Remove("History"),
            root => root.Remove("RecentLines"),
            root => root["FutureField"] = 1);
    }

    [Fact]
    public async Task Load_NumericOrUnknownEnumsFallsBackToNoSnapshot()
    {
        await AssertRejectedAsync(
            root => root["State"]!["Mood"] = 0,
            root => root["State"]!["Activity"] = "Dancing",
            root => root["LastCategory"] = 0,
            root => root["History"]![0]!["OutputMode"] = 99);
    }

    [Fact]
    public async Task Load_OutOfRangeStateOrTurnCountFallsBackToNoSnapshot()
    {
        await AssertRejectedAsync(
            root => root["State"]!["Energy"] = 1.01,
            root => root["State"]!["Sociability"] = -0.01,
            root => root["State"]!["Boredom"] = "NaN",
            root => root["State"]!["AttachmentDays"] = 0,
            root => root["State"]!["LastUpdatedAt"] = "2026-07-20T00:00:00",
            root => root["TurnCount"] = -1);
    }

    [Fact]
    public async Task Load_InvalidHistoryOrRecentLinesFallsBackToNoSnapshot()
    {
        await AssertRejectedAsync(
            root => root["RecentLines"]![0] = " ",
            root => root["History"]![0]!["SceneId"] = "",
            root => root["History"]![0]!["SceneId"] = "missing-scene",
            root => root["History"]![0]!["SemanticGroup"] = "wrong-group",
            root => root["History"]![0]!["Variant"] = "wrong variant",
            root => root["History"]![0]!["DialogueLineId"] = "missing-line",
            root => root["History"]![0]!["InterruptionCost"] = 6,
            root => root["History"]![0]!["PlayedAt"] = "0001-01-01T00:00:00",
            root => root["History"]![0]!["PlayedLocalDate"] = null);
    }

    [Fact]
    public async Task Load_InvalidOrDuplicateStoriesFallsBackToNoSnapshot()
    {
        await AssertRejectedAsync(
            root => root["State"]!["ActiveStories"]![0]!["ArcId"] = "missing-story",
            root => root["State"]!["ActiveStories"]![0]!["NodeIndex"] = 0,
            root => root["State"]!["ActiveStories"]![0]!["NodeIndex"] = 999,
            root =>
            {
                var stories = root["State"]!["ActiveStories"]!.AsArray();
                stories.Add(stories[0]!.DeepClone());
            },
            root => root["State"]!["ActiveStories"]![0]!["DueAt"] = "0001-01-01T00:00:00");
    }

    [Fact]
    public async Task DeferredWarmupLoad_UsesOnlyStructuralSafetyBeforeTheBackgroundCatalogGate()
    {
        var now = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local);
        var snapshot = new AgentMemorySnapshot(
            CharacterState.Create(now),
            [
                new SceneHistoryEntry(
                    "retired-scene",
                    "retired.semantic",
                    now,
                    "旧版本留下的一句话。",
                    "retired-line",
                    DialogueCategory.CharacterLife,
                    DialogueCategoryGroup.CharacterLife,
                    DialogueOutputMode.SelfTalk,
                    DialogueTrigger.Any,
                    0,
                    DateOnly.FromDateTime(now))
            ],
            1,
            DialogueCategory.CharacterLife,
            ["旧版本留下的一句话。"]);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "agent-memory.json"),
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
            }));
        var service = new AgentMemoryService(_directory);

        var deferred = await service.LoadForDeferredWarmupAsync();

        Assert.NotNull(deferred);
        Assert.Equal("retired-scene", Assert.Single(deferred!.History).SceneId);
        Assert.Null(await service.LoadAsync());
    }

    [Fact]
    public async Task DeferredWarmupLoad_RejectsStructurallyBrokenMemoryImmediately()
    {
        var now = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local);
        var snapshot = new AgentMemorySnapshot(
            CharacterState.Create(now),
            [],
            0,
            null,
            []);
        snapshot.State.Energy = 2;
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "agent-memory.json"),
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
            }));

        Assert.Null(await new AgentMemoryService(_directory).LoadForDeferredWarmupAsync());
    }

    [Fact]
    public void AgentSnapshot_RestoresTurnAndStoryContinuity()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var original = new OfflineCompanionAgent(DialogueCategory.Python);
        original.Respond(CompanionEvent.Click, now, new Random(1));
        var snapshot = original.CreateSnapshot();

        var restored = new OfflineCompanionAgent(snapshot);

        Assert.Equal(original.TurnCount, restored.TurnCount);
        Assert.Equal(original.RecentLines, restored.RecentLines);
        Assert.Equal(original.History.Entries.Count, restored.History.Entries.Count);
    }

    private async Task AssertRejectedAsync(params Action<JsonObject>[] mutations)
    {
        foreach (var mutation in mutations)
        {
            var loaded = await LoadMutatedSnapshotAsync(mutation);
            Assert.Null(loaded);
        }
    }

    private async Task<AgentMemorySnapshot?> LoadMutatedSnapshotAsync(
        Action<JsonObject> mutation)
    {
        var service = new AgentMemoryService(_directory);
        await service.SaveAsync(CreateValidSnapshot());
        var path = Path.Combine(_directory, "agent-memory.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        mutation(root);
        await File.WriteAllTextAsync(path, root.ToJsonString());
        return await service.LoadAsync();
    }

    private static AgentMemorySnapshot CreateValidSnapshot()
    {
        var now = new DateTime(2026, 7, 22, 15, 0, 0);
        var state = CharacterState.Create(now);
        state.Activity = PetActivity.Reading;
        var arc = StoryArcCatalog.All[0];
        state.ActiveStories.Add(new StoryProgress(arc.Id, 1, now.AddHours(5)));

        var scene = SceneCatalog.All.First(item => item.StoryArcId is null);
        var line = scene.Lines[0];
        var history = new SceneHistoryEntry(
            scene.Id,
            line.SemanticGroup,
            now,
            line.Text,
            line.Id,
            line.Category,
            line.CategoryGroup,
            line.OutputMode,
            line.Trigger,
            line.InterruptionCost,
            DateOnly.FromDateTime(now));

        return new AgentMemorySnapshot(
            state,
            [history],
            TurnCount: 12,
            LastCategory: line.Category,
            RecentLines: [line.Text]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
