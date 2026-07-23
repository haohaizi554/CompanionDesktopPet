namespace CompanionDesktopPet.Services;

public sealed record AgentReply(
    string Text,
    DialogueCategory Category,
    DialogueTreeKind Tree,
    CompanionEvent Trigger,
    string SceneId = "legacy",
    SceneExpression Expression = SceneExpression.Direct,
    string AnimationCue = "none",
    bool ShouldDisplayText = true,
    DialogueLine? SourceLine = null,
    string SemanticGroup = "");

public sealed class OfflineCompanionAgent
{
    public const int RecentMemoryLimit = 64;

    private readonly Queue<string> _recentLines = new(RecentMemoryLimit);
    private readonly HashSet<string> _usedThisSession = new(StringComparer.Ordinal);
    private readonly SceneHistory _history = new();
    private readonly SceneScheduler _scheduler = new();
    private CharacterState? _state;
    private DialogueCategory? _lastCategory;

    public OfflineCompanionAgent(DialogueCategory? initialCategory = null) => _lastCategory = initialCategory;

    public OfflineCompanionAgent(AgentMemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _state = snapshot.State;
        _lastCategory = snapshot.LastCategory;
        TurnCount = snapshot.TurnCount;
        _history.Restore(snapshot.History);
        foreach (var line in snapshot.RecentLines.TakeLast(RecentMemoryLimit))
        {
            Remember(line);
        }
    }

    public int TurnCount { get; private set; }

    public IReadOnlyList<string> RecentLines => _recentLines.ToArray();

    public CharacterState? State => _state;

    public SceneHistory History => _history;

    public AgentMemorySnapshot CreateSnapshot()
    {
        _state ??= CharacterState.Create(DateTime.Now);
        return new AgentMemorySnapshot(
            _state,
            _history.Entries.ToArray(),
            TurnCount,
            _lastCategory,
            RecentLines);
    }

    public AgentReply Respond(CompanionEvent trigger, DateTime localTime, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _state ??= CharacterState.Create(localTime);
        _state.AdvanceTo(localTime);

        var preferredTree = DialogueForest.SelectTree(trigger, _lastCategory, TurnCount, random);
        var context = new SceneContext(
            trigger,
            localTime,
            _state,
            PreferredTree: preferredTree.Kind,
            PreviousCategory: _lastCategory);
        var scene = _scheduler.Select(context, _history, random);
        TurnCount++;
        if (scene is null)
        {
            if (trigger == CompanionEvent.StoryTimerDue)
            {
                DeferDueStory(localTime);
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

        var eligible = _history.EligibleLines(scene, localTime);
        var unused = eligible.Where(line => !_usedThisSession.Contains(line.Text)).ToArray();
        var line = WeightedChoice(unused.Length > 0 ? unused : eligible, random);
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
        _state.ActiveStories.Remove(due);
        _state.ActiveStories.Add(due with { DueAt = _history.NextEligibleAt(scene, now) });
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

        _state!.ActiveStories.RemoveAll(story => story.ArcId == scene.StoryArcId);
        var arc = StoryArcCatalog.All.Single(item => item.Id == scene.StoryArcId);
        if (scene.StoryNode >= arc.Nodes.Count - 1)
        {
            return;
        }

        var delay = scene.StoryNode == 0
            ? TimeSpan.FromHours(4 + random.NextDouble() * 4)
            : TimeSpan.FromHours(12 + random.NextDouble() * 18);
        _state.ActiveStories.Add(new StoryProgress(scene.StoryArcId, scene.StoryNode + 1, now + delay));
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
