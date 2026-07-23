using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class OfflineCompanionAgentTests
{
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
        }
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

        var entries = agent.History.Entries;
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
        state.ActiveStories.Add(new StoryProgress(arc.Id, 1, now.AddMinutes(-1)));
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
        var entry = Assert.Single(restored.History.Entries);
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
        state.ActiveStories.Add(new StoryProgress(arc.Id, 1, now));
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
        Assert.True(Assert.Single(agent.State!.ActiveStories).DueAt > now);
    }
}
