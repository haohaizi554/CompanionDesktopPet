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
    private readonly object _sync = new();
    private double _energy;
    private double _sociability;
    private double _boredom;
    private PetMood _mood;
    private PetActivity _activity;
    private DateTime _installedAt;
    private DateTime _lastUpdatedAt;
    private int _attachmentDays;
    private List<StoryProgress> _activeStories = [];

    [JsonRequired]
    public double Energy
    {
        get
        {
            lock (_sync)
            {
                return _energy;
            }
        }
        set
        {
            lock (_sync)
            {
                _energy = value;
            }
        }
    }

    [JsonRequired]
    public double Sociability
    {
        get
        {
            lock (_sync)
            {
                return _sociability;
            }
        }
        set
        {
            lock (_sync)
            {
                _sociability = value;
            }
        }
    }

    [JsonRequired]
    public double Boredom
    {
        get
        {
            lock (_sync)
            {
                return _boredom;
            }
        }
        set
        {
            lock (_sync)
            {
                _boredom = value;
            }
        }
    }

    [JsonRequired]
    public PetMood Mood
    {
        get
        {
            lock (_sync)
            {
                return _mood;
            }
        }
        set
        {
            lock (_sync)
            {
                _mood = value;
            }
        }
    }

    [JsonRequired]
    public PetActivity Activity
    {
        get
        {
            lock (_sync)
            {
                return _activity;
            }
        }
        set
        {
            lock (_sync)
            {
                _activity = value;
            }
        }
    }

    [JsonRequired]
    public DateTime InstalledAt
    {
        get
        {
            lock (_sync)
            {
                return _installedAt;
            }
        }
        set
        {
            lock (_sync)
            {
                _installedAt = value;
            }
        }
    }

    [JsonRequired]
    public DateTime LastUpdatedAt
    {
        get
        {
            lock (_sync)
            {
                return _lastUpdatedAt;
            }
        }
        set
        {
            lock (_sync)
            {
                _lastUpdatedAt = value;
            }
        }
    }

    [JsonRequired]
    public int AttachmentDays
    {
        get
        {
            lock (_sync)
            {
                return _attachmentDays;
            }
        }
        set
        {
            lock (_sync)
            {
                _attachmentDays = value;
            }
        }
    }

    [JsonRequired]
    public IReadOnlyList<StoryProgress> ActiveStories
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(_activeStories.ToArray());
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var copied = value.ToArray();
            lock (_sync)
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
        lock (_sync)
        {
            if (now <= _lastUpdatedAt)
            {
                _attachmentDays = Math.Max(1, (now.Date - _installedAt.Date).Days + 1);
                return;
            }

            var hours = Math.Min((now - _lastUpdatedAt).TotalHours, 72);
            if (_activity == PetActivity.Sleeping)
            {
                _energy = Clamp(_energy + (hours * 0.085));
                _boredom = Clamp(_boredom - (hours * 0.025));
                _sociability = Clamp(_sociability + (hours * 0.015));
            }
            else
            {
                _energy = Clamp(_energy - (hours * 0.035));
                _boredom = Clamp(_boredom + (hours * 0.045));
                _sociability = Clamp(_sociability + (hours * 0.008));
            }

            _mood = _energy switch
            {
                < 0.22 => PetMood.Sleepy,
                _ when _boredom > 0.76 => PetMood.Playful,
                _ when _sociability < 0.25 => PetMood.Quiet,
                _ when _energy > 0.66 => PetMood.Focused,
                _ => PetMood.Calm
            };
            _lastUpdatedAt = now;
            _attachmentDays = Math.Max(1, (now.Date - _installedAt.Date).Days + 1);
        }
    }

    public void ApplyScene(SceneDefinition scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        lock (_sync)
        {
            _energy = Clamp(_energy + scene.EnergyDelta);
            _sociability = Clamp(_sociability + scene.SociabilityDelta);
            _boredom = Clamp(_boredom + scene.BoredomDelta);
        }
    }

    internal void AddActiveStory(StoryProgress story)
    {
        ArgumentNullException.ThrowIfNull(story);
        lock (_sync)
        {
            _activeStories.Add(story);
        }
    }

    internal bool RemoveActiveStory(StoryProgress story)
    {
        ArgumentNullException.ThrowIfNull(story);
        lock (_sync)
        {
            return _activeStories.Remove(story);
        }
    }

    internal int RemoveActiveStories(Predicate<StoryProgress> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_sync)
        {
            return _activeStories.RemoveAll(predicate);
        }
    }

    internal CharacterState Clone()
    {
        lock (_sync)
        {
            return new CharacterState
            {
                Energy = _energy,
                Sociability = _sociability,
                Boredom = _boredom,
                Mood = _mood,
                Activity = _activity,
                InstalledAt = _installedAt,
                LastUpdatedAt = _lastUpdatedAt,
                AttachmentDays = _attachmentDays,
                ActiveStories = _activeStories.ToArray()
            };
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
