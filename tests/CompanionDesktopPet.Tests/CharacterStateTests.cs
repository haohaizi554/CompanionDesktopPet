using System.Text.Json;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class CharacterStateTests
{
    private static readonly DateTime InstalledAt = new(2026, 7, 20, 9, 0, 0);

    [Fact]
    public void AdvanceTo_IgnoresNonForwardTimeAndCapsElapsedEffectsAtSeventyTwoHours()
    {
        var state = CharacterState.Create(InstalledAt);

        state.AdvanceTo(InstalledAt.AddHours(-1));
        Assert.Equal(InstalledAt, state.LastUpdatedAt);

        var later = InstalledAt.AddHours(100);
        state.AdvanceTo(later);

        Assert.Equal(later, state.LastUpdatedAt);
        Assert.Equal(0, state.Energy, 8);
        Assert.Equal(1, state.Boredom, 8);
        Assert.Equal(0.996, state.Sociability, 8);
        Assert.Equal(5, state.AttachmentDays);
    }

    [Fact]
    public void ApplyScene_AppliesDeltasAndClampsEveryMeter()
    {
        var state = CharacterState.Create(InstalledAt);
        var scene = SceneCatalog.PersonaScenes[0] with
        {
            EnergyDelta = 1,
            SociabilityDelta = -1,
            BoredomDelta = 1
        };

        state.ApplyScene(scene);

        Assert.Equal(1, state.Energy);
        Assert.Equal(0, state.Sociability);
        Assert.Equal(1, state.Boredom);
    }

    [Fact]
    public void Clone_CopiesStoriesIntoAnIndependentCollection()
    {
        var state = CharacterState.Create(InstalledAt);
        state.AddActiveStory(new StoryProgress("arc", 1, InstalledAt.AddHours(2)));

        var clone = state.Clone();
        clone.AddActiveStory(new StoryProgress("clone-only", 2, InstalledAt.AddHours(3)));

        Assert.Single(state.ActiveStories);
        Assert.Equal(2, clone.ActiveStories.Count);
        Assert.NotSame(state.ActiveStories, clone.ActiveStories);
    }

    [Fact]
    public void ActiveStories_DefensivelyCopiesIncomingAndReturnedCollections()
    {
        var incoming = new List<StoryProgress>
        {
            new("arc", 1, InstalledAt.AddHours(2))
        };
        var state = CharacterState.Create(InstalledAt);
        state.ActiveStories = incoming;

        incoming.Clear();
        var returned = Assert.IsAssignableFrom<IList<StoryProgress>>(state.ActiveStories);

        Assert.Single(returned);
        Assert.Throws<NotSupportedException>(() => returned.Clear());
        Assert.Single(state.ActiveStories);
    }

    [Fact]
    public void JsonRoundTrip_PreservesStateAndStories()
    {
        var state = CharacterState.Create(InstalledAt);
        state.Activity = PetActivity.Reading;
        state.Mood = PetMood.Focused;
        state.ActiveStories =
        [
            new StoryProgress("arc", 3, InstalledAt.AddDays(1))
        ];

        var restored = JsonSerializer.Deserialize<CharacterState>(
            JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(state.Energy, restored.Energy);
        Assert.Equal(state.Sociability, restored.Sociability);
        Assert.Equal(state.Boredom, restored.Boredom);
        Assert.Equal(state.Mood, restored.Mood);
        Assert.Equal(state.Activity, restored.Activity);
        Assert.Equal(state.InstalledAt, restored.InstalledAt);
        Assert.Equal(state.LastUpdatedAt, restored.LastUpdatedAt);
        Assert.Equal(state.AttachmentDays, restored.AttachmentDays);
        Assert.Equal(state.ActiveStories, restored.ActiveStories);
    }
}
