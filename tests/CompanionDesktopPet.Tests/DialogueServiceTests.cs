using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueServiceTests
{
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
}
