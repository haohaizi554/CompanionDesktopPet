using System.Diagnostics;
using System.IO;

namespace CompanionDesktopPet.Services;

public sealed record AgentReply(
    string Text,
    DialogueCategory Category,
    DialogueTreeKind Tree,
    CompanionEvent Trigger,
    string SceneId = "unknown",
    SceneExpression Expression = SceneExpression.Direct,
    string AnimationCue = "none",
    bool ShouldDisplayText = true,
    DialogueLine? SourceLine = null,
    string SemanticGroup = "");

internal interface ICompanionDialogueAgent
{
    DateTime? NextStoryDueAt { get; }

    AgentMemorySnapshot CreateSnapshot();

    AgentReply Respond(
        CompanionEvent trigger,
        DateTime localTime,
        Random random,
        FullscreenSnapshot fullscreen);
}

public sealed class OfflineCompanionAgent : ICompanionDialogueAgent
{
    public const int RecentMemoryLimit = 64;

    private readonly object _sync = new();
    private readonly Queue<string> _recentLines = new(RecentMemoryLimit);
    private readonly HashSet<string> _usedThisSession = new(StringComparer.Ordinal);
    private readonly SceneHistory _history = new();
    private readonly SceneScheduler _scheduler = new();
    private CharacterState? _state;
    private DialogueCategory? _lastCategory;
    private int _turnCount;

    public OfflineCompanionAgent(DialogueCategory? initialCategory = null) => _lastCategory = initialCategory;

    public OfflineCompanionAgent(AgentMemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _state = snapshot.State.Clone();
        _lastCategory = snapshot.LastCategory;
        _turnCount = snapshot.TurnCount;
        _history.Restore(snapshot.History);
        foreach (var line in snapshot.RecentLines.TakeLast(RecentMemoryLimit))
        {
            Remember(line);
        }
    }

    public int TurnCount
    {
        get
        {
            lock (_sync)
            {
                return _turnCount;
            }
        }
    }

    public IReadOnlyList<string> RecentLines
    {
        get
        {
            lock (_sync)
            {
                return _recentLines.ToArray();
            }
        }
    }

    public DateTime? NextStoryDueAt
    {
        get
        {
            lock (_sync)
            {
                var stories = _state?.ActiveStories;
                return stories is { Count: > 0 }
                    ? stories.Min(story => story.DueAt)
                    : null;
            }
        }
    }

    public AgentMemorySnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            _state ??= CharacterState.Create(DateTime.Now);
            return new AgentMemorySnapshot(
                _state.Clone(),
                _history.Entries,
                _turnCount,
                _lastCategory,
                _recentLines.ToArray());
        }
    }

    internal void WarmUp()
    {
        _ = SceneCatalog.All.Count;
        if (SceneCatalog.PersonaLoadFailure is { } failure)
        {
            throw new InvalidDataException(
                "The validated v2 persona corpus is unavailable; degraded dialogue cannot report ready.",
                failure);
        }
    }

    public AgentReply Respond(CompanionEvent trigger, DateTime localTime, Random random) =>
        RespondCore(trigger, localTime, random, default);

    internal AgentReply RespondWithContext(
        CompanionEvent trigger,
        DateTime localTime,
        Random random,
        FullscreenSnapshot fullscreen) =>
        RespondCore(trigger, localTime, random, fullscreen);

    AgentReply ICompanionDialogueAgent.Respond(
        CompanionEvent trigger,
        DateTime localTime,
        Random random,
        FullscreenSnapshot fullscreen) =>
        RespondCore(trigger, localTime, random, fullscreen);

    private AgentReply RespondCore(
        CompanionEvent trigger,
        DateTime localTime,
        Random random,
        FullscreenSnapshot fullscreen)
    {
        ArgumentNullException.ThrowIfNull(random);
        lock (_sync)
        {
            return RespondCoreLocked(trigger, localTime, random, fullscreen);
        }
    }

    private AgentReply RespondCoreLocked(
        CompanionEvent trigger,
        DateTime localTime,
        Random random,
        FullscreenSnapshot fullscreen)
    {
        _state ??= CharacterState.Create(localTime);
        _state.AdvanceTo(localTime);

        var preferredTree = DialogueForest.SelectTree(trigger, _lastCategory, _turnCount, random);
        var context = new SceneContext(
            trigger,
            localTime,
            _state,
            IsFullscreen: fullscreen.Observed,
            PreferredTree: preferredTree.Kind,
            PreviousCategory: _lastCategory,
            EffectiveFullscreen: fullscreen.EffectiveQuietMode);
        var bypassBudget = DialogueEventPolicy.BypassesInterruptionBudget(trigger);
        var scene = _scheduler.Select(
            context,
            _history,
            random,
            bypassInterruptionBudget: bypassBudget);
        SafeFeedbackSelection? safe = null;
        if (scene is null && bypassBudget)
        {
            safe = _scheduler.SelectSafeFeedback(SceneCatalog.PersonaScenes, context, _history, random);
            scene = safe?.Scene;
        }
        _turnCount++;
        if (scene is null)
        {
            if (trigger == CompanionEvent.StoryTimerDue)
            {
                DeferDueStory(localTime);
            }

            if (trigger == CompanionEvent.Automatic)
            {
                TraceAutomaticSafeFeedbackContractFailure(localTime, fullscreen);
            }

            return new AgentReply(
                string.Empty,
                _lastCategory ?? DialogueCategory.CharacterLife,
                preferredTree.Kind,
                trigger,
                SceneId: "intentional_silence",
                Expression: SceneExpression.ActionOnly,
                AnimationCue: "none",
                ShouldDisplayText: false);
        }

        var line = safe?.Line ?? SelectEligibleLine(scene, localTime, random);
        _history.Record(scene, localTime, line);
        _state.ApplyScene(scene);
        UpdateActivity(line.CategoryGroup, line.Category);
        UpdateStoryProgress(scene, localTime, random);
        Remember(line.Text);
        _lastCategory = line.Category;
        return new AgentReply(
            line.Text,
            line.Category,
            scene.Tree,
            trigger,
            scene.Id,
            scene.Expression,
            scene.AnimationCue,
            ShouldDisplayText: true,
            SourceLine: line,
            SemanticGroup: line.SemanticGroup);
    }

    private DialogueLine SelectEligibleLine(SceneDefinition scene, DateTime localTime, Random random)
    {
        var eligible = _history.EligibleLines(scene, localTime);
        var diverse = _history.PreferSurfaceExposure(eligible);
        var unused = diverse.Where(line => !_usedThisSession.Contains(line.Text)).ToArray();
        return WeightedChoice(unused.Length > 0 ? unused : diverse, random);
    }

    private static void TraceAutomaticSafeFeedbackContractFailure(
        DateTime localTime,
        FullscreenSnapshot fullscreen)
    {
        try
        {
            Trace.TraceError(
                "Validated safe-feedback contract returned no Automatic line at {0:o}; observed fullscreen={1}, effective quiet={2}.",
                localTime,
                fullscreen.Observed?.ToString() ?? "unknown",
                fullscreen.EffectiveQuietMode);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
                                          and not StackOverflowException
                                          and not AccessViolationException)
        {
            // A nonfatal diagnostic must not turn intentional silence into a retry loop or crash.
        }
    }

    private static DialogueLine WeightedChoice(IReadOnlyList<DialogueLine> source, Random random)
    {
        if (source.Count == 0)
        {
            throw new InvalidOperationException("The scheduler selected a scene without an eligible v2 line.");
        }

        var roll = random.NextDouble() * source.Sum(line => line.Weight);
        foreach (var line in source)
        {
            roll -= line.Weight;
            if (roll <= 0)
            {
                return line;
            }
        }

        return source[^1];
    }

    private void DeferDueStory(DateTime now)
    {
        var due = _state!.ActiveStories
            .Where(story => story.DueAt <= now)
            .OrderBy(story => story.DueAt)
            .FirstOrDefault();
        if (due is null)
        {
            return;
        }

        var scene = StoryArcCatalog.All.Single(arc => arc.Id == due.ArcId).Nodes[due.NodeIndex];
        _state.RemoveActiveStory(due);
        _state.AddActiveStory(due with { DueAt = _history.NextEligibleAt(scene, now) });
    }

    private void UpdateActivity(DialogueCategoryGroup group, DialogueCategory category)
    {
        _state!.Activity = (group, category) switch
        {
            (DialogueCategoryGroup.Growth, DialogueCategory.EnglishPractice) => PetActivity.PracticingEnglish,
            (DialogueCategoryGroup.Growth, _) => PetActivity.Reading,
            (DialogueCategoryGroup.Career, _) => PetActivity.Reading,
            (DialogueCategoryGroup.CharacterLife, DialogueCategory.DressesHobbies) => PetActivity.SortingThings,
            (DialogueCategoryGroup.CharacterLife, _) => PetActivity.WritingDiary,
            (DialogueCategoryGroup.EmotionalReflection, _) => PetActivity.LookingOutside,
            (DialogueCategoryGroup.SystemAmbient, _) => PetActivity.LookingOutside,
            (DialogueCategoryGroup.Technical, DialogueCategory.Architecture or DialogueCategory.Systems) => PetActivity.BuildingGadget,
            _ => _state.Activity
        };
    }

    private void UpdateStoryProgress(SceneDefinition scene, DateTime now, Random random)
    {
        if (scene.StoryArcId is null)
        {
            return;
        }

        _state!.RemoveActiveStories(story => story.ArcId == scene.StoryArcId);
        var arc = StoryArcCatalog.All.Single(item => item.Id == scene.StoryArcId);
        if (scene.StoryNode >= arc.Nodes.Count - 1)
        {
            return;
        }

        var delay = scene.StoryNode == 0
            ? TimeSpan.FromHours(4 + random.NextDouble() * 4)
            : TimeSpan.FromHours(12 + random.NextDouble() * 18);
        _state.AddActiveStory(new StoryProgress(scene.StoryArcId, scene.StoryNode + 1, now + delay));
    }

    private void Remember(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _usedThisSession.Add(text);
        _recentLines.Enqueue(text);
        while (_recentLines.Count > RecentMemoryLimit)
        {
            _recentLines.Dequeue();
        }
    }
}
