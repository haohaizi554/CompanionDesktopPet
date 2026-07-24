namespace CompanionDesktopPet.Services;

public static class SceneCatalog
{
    public static IReadOnlyList<SceneDefinition> PersonaScenes { get; } = BuildPersonaScenes();

    private static readonly Lazy<IReadOnlyList<SceneDefinition>> AllScenes = new(() =>
        [.. PersonaScenes, .. StoryArcCatalog.All.SelectMany(arc => arc.Nodes)]);

    public static IReadOnlyList<SceneDefinition> All => AllScenes.Value;

    private static IReadOnlyList<SceneDefinition> BuildPersonaScenes() =>
        PersonaCorpus.All
            .GroupBy(line => line.SemanticGroup, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateScene($"persona:{group.Key}", group.ToArray()))
            .ToArray();

    internal static SceneDefinition CreateScene(
        string id,
        IReadOnlyList<DialogueLine> source,
        string? storyArcId = null,
        int storyNode = -1)
    {
        if (source.Count == 0)
        {
            throw new ArgumentException("A scene needs at least one v2 line.", nameof(source));
        }

        var lines = source.OrderBy(line => line.Id, StringComparer.Ordinal).ToArray();
        var first = lines[0];
        if (lines.Any(line => line.SemanticGroup != first.SemanticGroup
                              || line.CategoryGroup != first.CategoryGroup
                              || line.OutputMode != first.OutputMode
                              || line.Trigger != first.Trigger
                              || !line.RequiredContext.SequenceEqual(first.RequiredContext)
                              || line.Cooldown != first.Cooldown
                              || line.SemanticCooldown != first.SemanticCooldown
                              || line.MaxPerDay != first.MaxPerDay
                              || line.InterruptionCost != first.InterruptionCost
                              || line.Weight != first.Weight))
        {
            throw new InvalidOperationException($"Semantic group '{first.SemanticGroup}' has inconsistent runtime metadata.");
        }

        var expression = first.OutputMode switch
        {
            DialogueOutputMode.SelfTalk => SceneExpression.SelfTalk,
            DialogueOutputMode.Ambient or DialogueOutputMode.SystemObserve => SceneExpression.Ambient,
            DialogueOutputMode.UserDirect => SceneExpression.Direct,
            _ => throw new ArgumentOutOfRangeException()
        };
        return new SceneDefinition(
            id,
            first.SemanticGroup,
            expression,
            DialogueForest.GetTreeForGroup(first.CategoryGroup).Kind,
            first.Category,
            storyArcId is null ? TriggersFor(first.Trigger) : StoryTriggers(storyNode),
            Priority: (int)Math.Round(DialogueForest.CategoryGroupWeights[first.CategoryGroup] * 100),
            Cooldown: first.Cooldown,
            InterruptionCost: first.InterruptionCost,
            AnimationCue: "none",
            Variants: lines.Select(line => line.Text).ToArray(),
            Lines: lines,
            CategoryGroup: first.CategoryGroup,
            OutputMode: first.OutputMode,
            DialogueTrigger: first.Trigger,
            RequiredContext: first.RequiredContext,
            SemanticCooldown: first.SemanticCooldown,
            MaxPerDay: first.MaxPerDay,
            Weight: first.Weight,
            EnergyDelta: -0.012,
            SociabilityDelta: expression == SceneExpression.Direct ? -0.025 : 0.005,
            BoredomDelta: -0.04,
            StoryArcId: storyArcId,
            StoryNode: storyNode);
    }

    private static IReadOnlySet<CompanionEvent> StoryTriggers(int node) => new HashSet<CompanionEvent>
    {
        node == 0 ? CompanionEvent.Automatic : CompanionEvent.StoryTimerDue,
        node == 0 ? CompanionEvent.ClockTick : CompanionEvent.StoryTimerDue
    };

    private static IReadOnlySet<CompanionEvent> TriggersFor(DialogueTrigger trigger) => trigger switch
    {
        DialogueTrigger.AppStart => new HashSet<CompanionEvent> { CompanionEvent.Startup },
        DialogueTrigger.DayChanged => new HashSet<CompanionEvent> { CompanionEvent.DayChanged },
        DialogueTrigger.IdleReturn => new HashSet<CompanionEvent> { CompanionEvent.IdleReturned },
        DialogueTrigger.StoryTimer => new HashSet<CompanionEvent> { CompanionEvent.StoryTimerDue },
        DialogueTrigger.Morning or DialogueTrigger.Noon or DialogueTrigger.Afternoon
            or DialogueTrigger.Evening or DialogueTrigger.LateNight
            or DialogueTrigger.Weekday or DialogueTrigger.Weekend
            or DialogueTrigger.Holiday or DialogueTrigger.Anniversary
            or DialogueTrigger.LongSilence => new HashSet<CompanionEvent>
            {
                CompanionEvent.Startup,
                CompanionEvent.Automatic,
                CompanionEvent.ClockTick,
                CompanionEvent.DayChanged
            },
        DialogueTrigger.IdeForeground or DialogueTrigger.LongActive => new HashSet<CompanionEvent>
        {
            CompanionEvent.Automatic,
            CompanionEvent.AnimationResumed
        },
        _ => Enum.GetValues<CompanionEvent>().ToHashSet()
    };
}
