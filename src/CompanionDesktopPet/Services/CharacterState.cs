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

public sealed record StoryProgress(string ArcId, int NodeIndex, DateTime DueAt);

public sealed class CharacterState
{
    public double Energy { get; set; }

    public double Sociability { get; set; }

    public double Boredom { get; set; }

    public PetMood Mood { get; set; }

    public PetActivity Activity { get; set; }

    public DateTime InstalledAt { get; set; }

    public DateTime LastUpdatedAt { get; set; }

    public int AttachmentDays { get; set; }

    public List<StoryProgress> ActiveStories { get; set; } = [];

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

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
