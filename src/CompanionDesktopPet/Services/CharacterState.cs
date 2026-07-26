using System.Text.Json.Serialization;

namespace CompanionDesktopPet.Services;

public enum PetMood
{
    Calm,
    Focused,
    Playful,
    Sleepy,
    Quiet
}

public enum PetActivity
{
    Idling,
    Reading,
    PracticingEnglish,
    SortingThings,
    Drawing,
    Cooking,
    BuildingGadget,
    WritingDiary,
    LookingOutside,
    Sleeping
}

public sealed record StoryProgress(
    [property: JsonRequired] string ArcId,
    [property: JsonRequired] int NodeIndex,
    [property: JsonRequired] DateTime DueAt);

public sealed class CharacterState
{
    private readonly object _activeStoriesSync = new();
    private List<StoryProgress> _activeStories = [];

    [JsonRequired]
    public double Energy { get; set; }

    [JsonRequired]
    public double Sociability { get; set; }

    [JsonRequired]
    public double Boredom { get; set; }

    [JsonRequired]
    public PetMood Mood { get; set; }

    [JsonRequired]
    public PetActivity Activity { get; set; }

    [JsonRequired]
    public DateTime InstalledAt { get; set; }

    [JsonRequired]
    public DateTime LastUpdatedAt { get; set; }

    [JsonRequired]
    public int AttachmentDays { get; set; }

    [JsonRequired]
    public IReadOnlyList<StoryProgress> ActiveStories
    {
        get
        {
            lock (_activeStoriesSync)
            {
                return Array.AsReadOnly(_activeStories.ToArray());
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var copied = value.ToArray();
            lock (_activeStoriesSync)
            {
                _activeStories = [.. copied];
            }
        }
    }

    public static CharacterState Create(DateTime now) => new()
    {
        Energy = 0.72,
        Sociability = 0.42,
        Boredom = 0.36,
        Mood = PetMood.Calm,
        Activity = PetActivity.Idling,
        InstalledAt = now,
        LastUpdatedAt = now,
        AttachmentDays = 1
    };

    public void AdvanceTo(DateTime now)
    {
        if (now <= LastUpdatedAt)
        {
            AttachmentDays = Math.Max(1, (now.Date - InstalledAt.Date).Days + 1);
            return;
        }

        var hours = Math.Min((now - LastUpdatedAt).TotalHours, 72);
        if (Activity == PetActivity.Sleeping)
        {
            Energy = Clamp(Energy + (hours * 0.085));
            Boredom = Clamp(Boredom - (hours * 0.025));
            Sociability = Clamp(Sociability + (hours * 0.015));
        }
        else
        {
            Energy = Clamp(Energy - (hours * 0.035));
            Boredom = Clamp(Boredom + (hours * 0.045));
            Sociability = Clamp(Sociability + (hours * 0.008));
        }

        Mood = Energy switch
        {
            < 0.22 => PetMood.Sleepy,
            _ when Boredom > 0.76 => PetMood.Playful,
            _ when Sociability < 0.25 => PetMood.Quiet,
            _ when Energy > 0.66 => PetMood.Focused,
            _ => PetMood.Calm
        };
        LastUpdatedAt = now;
        AttachmentDays = Math.Max(1, (now.Date - InstalledAt.Date).Days + 1);
    }

    public void ApplyScene(SceneDefinition scene)
    {
        Energy = Clamp(Energy + scene.EnergyDelta);
        Sociability = Clamp(Sociability + scene.SociabilityDelta);
        Boredom = Clamp(Boredom + scene.BoredomDelta);
    }

    internal void AddActiveStory(StoryProgress story)
    {
        ArgumentNullException.ThrowIfNull(story);
        lock (_activeStoriesSync)
        {
            _activeStories.Add(story);
        }
    }

    internal bool RemoveActiveStory(StoryProgress story)
    {
        ArgumentNullException.ThrowIfNull(story);
        lock (_activeStoriesSync)
        {
            return _activeStories.Remove(story);
        }
    }

    internal int RemoveActiveStories(Predicate<StoryProgress> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_activeStoriesSync)
        {
            return _activeStories.RemoveAll(predicate);
        }
    }

    internal CharacterState Clone() => new()
    {
        Energy = Energy,
        Sociability = Sociability,
        Boredom = Boredom,
        Mood = Mood,
        Activity = Activity,
        InstalledAt = InstalledAt,
        LastUpdatedAt = LastUpdatedAt,
        AttachmentDays = AttachmentDays,
        ActiveStories = ActiveStories
    };

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
