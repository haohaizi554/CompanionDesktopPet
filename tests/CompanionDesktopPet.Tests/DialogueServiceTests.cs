using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueServiceTests
{
    [Fact]
    public async Task GetReply_ForwardsObservedAndEffectiveFullscreenStateUnchanged()
    {
        var agent = new RecordingAgent();
        var service = DialogueService.CreateDeferred(agentFactory: _ => agent);
        Assert.True(await service.WarmupAsync());
        var fullscreen = new FullscreenSnapshot(null, true);

        var reply = service.GetReply(
            CompanionEvent.Automatic,
            new DateTime(2026, 7, 26, 15, 0, 0, DateTimeKind.Local),
            new Random(20260726),
            fullscreen);

        Assert.Equal(fullscreen, agent.LastFullscreen);
        Assert.Equal("recorded:automatic", reply.SceneId);
    }

    [Theory]
    [InlineData(CompanionEvent.Startup)]
    [InlineData(CompanionEvent.Click)]
    [InlineData(CompanionEvent.Automatic)]
    public void GetReply_ReturnsEnabledV2TextWithProvenance(CompanionEvent trigger)
    {
        var service = new DialogueService();

        var reply = service.GetReply(
            trigger,
            new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Local),
            new Random(1000 + (int)trigger));

        Assert.True(reply.ShouldDisplayText);
        var source = Assert.IsType<DialogueLine>(reply.SourceLine);
        Assert.True(source.Enabled);
        Assert.Equal(source.Text, reply.Text);
        Assert.Equal(source.SemanticGroup, reply.SemanticGroup);
        Assert.Contains(source, PersonaCorpus.All);
    }

    [Fact]
    public void DialogueService_ExposesNoLegacyGreetingOrPhrasePath()
    {
        Assert.Null(typeof(DialogueService).GetMethod("GetGreeting"));
        Assert.Null(typeof(DialogueService).GetMethod("GetNextPhrase"));
    }

    private sealed class RecordingAgent : ICompanionDialogueAgent
    {
        public DateTime? NextStoryDueAt => null;

        public FullscreenSnapshot? LastFullscreen { get; private set; }

        public AgentMemorySnapshot CreateSnapshot() => new OfflineCompanionAgent().CreateSnapshot();

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen)
        {
            LastFullscreen = fullscreen;
            return new AgentReply(
                "recorded",
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "recorded:automatic");
        }
    }
}
