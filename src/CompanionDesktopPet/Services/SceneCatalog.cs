using System.Diagnostics;
using System.IO;

namespace CompanionDesktopPet.Services;

internal sealed record SceneCatalogLoadResult(
    IReadOnlyList<SceneDefinition> Scenes,
    Exception? Failure);

public static class SceneCatalog
{
    private static readonly Lazy<SceneCatalogLoadResult> PersonaSceneSnapshot = new(
        () => LoadPersonaScenes(
            () => PersonaCorpus.All,
            () => FallbackDialogueCatalog.All,
            ValidatePublishedScenes,
            ReportPersonaLoadFailure),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlySet<string>> DrySharpGroups = new(
        () => PersonaScenes
            .Where(scene => scene.Tone == "dry_sharp")
            .Select(scene => scene.SemanticGroup)
            .ToHashSet(StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<SceneDefinition> PersonaScenes => LoadPublishedPersonaScenes().Scenes;

    public static IReadOnlySet<string> DrySharpSemanticGroups => DrySharpGroups.Value;

    internal static Exception? PersonaLoadFailure => LoadPublishedPersonaScenes().Failure;

    // Keep the fallback snapshot as one atomic value: callers that need startup
    // readiness must observe the same materialized catalog and failure record.
    internal static SceneCatalogLoadResult LoadPublishedPersonaScenes() => PersonaSceneSnapshot.Value;

    private static readonly Lazy<IReadOnlyList<SceneDefinition>> AllScenes = new(() =>
        [.. PersonaScenes, .. StoryArcCatalog.All.SelectMany(arc => arc.Nodes)]);

    public static IReadOnlyList<SceneDefinition> All => AllScenes.Value;

    internal static SceneCatalogLoadResult LoadPersonaScenes(
        Func<IReadOnlyList<DialogueLine>> primaryLoader,
        Func<IReadOnlyList<DialogueLine>> fallbackLoader,
        Action<IReadOnlyList<SceneDefinition>>? validatePrimary = null,
        Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(primaryLoader);
        ArgumentNullException.ThrowIfNull(fallbackLoader);
        try
        {
            var scenes = BuildPersonaScenes(primaryLoader());
            validatePrimary?.Invoke(scenes);
            return new SceneCatalogLoadResult(scenes, null);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            try
            {
                reportFailure?.Invoke(exception);
            }
            catch (Exception reportingFailure) when (!IsFatalException(reportingFailure))
            {
                // Diagnostics are never allowed to interrupt the safe fallback path.
            }

            return new SceneCatalogLoadResult(BuildPersonaScenes(fallbackLoader()), exception);
        }
    }

    internal static IReadOnlyList<SceneDefinition> BuildPersonaScenes(IReadOnlyList<DialogueLine> lines) =>
        lines
            .GroupBy(line => line.SemanticGroup, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateScene($"persona:{group.Key}", group.ToArray()))
            .ToArray();

    private static void ValidatePublishedScenes(IReadOnlyList<SceneDefinition> scenes)
    {
        if (scenes.Count != PersonaContractGenerated.SemanticSceneCount)
        {
            throw new InvalidDataException(
                $"Enabled v2 persona corpus must contain exactly {PersonaContractGenerated.SemanticSceneCount} semantic scenes, found {scenes.Count}.");
        }

        SceneScheduler.ValidateSafeFeedbackCoverage(scenes);
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    private static void ReportPersonaLoadFailure(Exception exception)
    {
        try
        {
            Trace.TraceError(
                "Persona corpus failed validation; using the built-in degraded dialogue catalog: {0}",
                exception);
        }
        catch (Exception reportingFailure) when (!IsFatalException(reportingFailure))
        {
            // Diagnostics must never disable the built-in startup fallback.
        }
    }

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
                              || line.Category != first.Category
                              || line.CategoryGroup != first.CategoryGroup
                              || line.OutputMode != first.OutputMode
                              || line.Trigger != first.Trigger
                              || !line.RequiredContext.SequenceEqual(first.RequiredContext)
                              || line.Cooldown != first.Cooldown
                              || line.SemanticCooldown != first.SemanticCooldown
                              || line.MaxPerDay != first.MaxPerDay
                              || line.InterruptionCost != first.InterruptionCost
                              || line.Weight != first.Weight
                              || line.Tone != first.Tone
                              || line.RequiresReply != first.RequiresReply
                              || line.Enabled != first.Enabled))
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
            first.Tone,
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
