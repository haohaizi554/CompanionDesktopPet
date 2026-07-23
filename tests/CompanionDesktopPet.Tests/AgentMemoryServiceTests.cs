using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class AgentMemoryServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsStateHistoryAndRecentMemory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 7, 22, 15, 0, 0);
            var state = CharacterState.Create(now);
            state.Activity = PetActivity.Reading;
            state.ActiveStories.Add(new StoryProgress("lost_button", 1, now.AddHours(5)));
            var snapshot = new AgentMemorySnapshot(
                state,
                [new SceneHistoryEntry("scene", "group", now, "line")],
                TurnCount: 12,
                LastCategory: DialogueCategory.Python,
                RecentLines: ["line"]);
            var service = new AgentMemoryService(directory);

            await service.SaveAsync(snapshot);
            var loaded = await service.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(PetActivity.Reading, loaded!.State.Activity);
            Assert.Single(loaded.State.ActiveStories);
            Assert.Single(loaded.History);
            Assert.Equal(12, loaded.TurnCount);
            Assert.Equal(DialogueCategory.Python, loaded.LastCategory);
            Assert.Equal(["line"], loaded.RecentLines);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Load_MalformedMemoryFallsBackToNoSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "agent-memory.json"), "{bad json");
        try
        {
            var loaded = await new AgentMemoryService(directory).LoadAsync();

            Assert.Null(loaded);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
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
}
