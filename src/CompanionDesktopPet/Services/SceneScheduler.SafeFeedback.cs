using System.IO;

namespace CompanionDesktopPet.Services;

internal sealed record SafeFeedbackSelection(SceneDefinition Scene, DialogueLine Line);

public sealed partial class SceneScheduler
{
    private const int DaytimeSafeCapacity = 144;
    private const int EveningSafeCapacity = 30;
    private const int LateNightAndDawnSafeCapacity = 14;
    private const int FullscreenSafeCapacity = 24;

    internal SafeFeedbackSelection? SelectSafeFeedback(
        IReadOnlyList<SceneDefinition> scenes,
        SceneContext context,
        SceneHistory history,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(random);

        var contextTokens = ContextTokens(context);
        var previousText = history.Entries.LastOrDefault()?.Variant;
        var strict = SelectSafeFeedbackLayer(
            scenes,
            context,
            contextTokens,
            history,
            random,
            previousText,
            retainCooldownsAndAdjacency: true);
        return strict ?? SelectSafeFeedbackLayer(
            scenes,
            context,
            contextTokens,
            history,
            random,
            previousText,
            retainCooldownsAndAdjacency: false);
    }

    internal static void ValidateSafeFeedbackCoverage(IReadOnlyList<SceneDefinition> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var scheduler = new SceneScheduler();
        var dates = new[]
        {
            new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Local)
        };
        bool?[] observations = [null, false, true];
        var dayHours = new[] { 10, 20, 2, 5 };
        var directTriggers = Enum.GetValues<CompanionEvent>()
            .Where(DialogueEventPolicy.IsDirectFeedback)
            .ToArray();

        foreach (var date in dates)
        {
            foreach (var observed in observations)
            {
                foreach (var hour in dayHours)
                {
                    var now = date.AddHours(hour);
                    var context = RuntimeContext(CompanionEvent.Automatic, now, observed);
                    scheduler.RequireTwoSafeLines(scenes, context);
                    foreach (var trigger in directTriggers)
                    {
                        scheduler.RequireTwoSafeLines(
                            scenes,
                            RuntimeContext(trigger, now, observed));
                    }
                }

                var bands = new[]
                {
                    new SafeCapacityBand(
                        "Daytime",
                        DaytimeSafeCapacity,
                        scheduler.SafeLinesForContexts(
                            scenes,
                            [RuntimeContext(CompanionEvent.Automatic, date.AddHours(10), observed)])
                            .ToArray()),
                    new SafeCapacityBand(
                        "Evening",
                        EveningSafeCapacity,
                        scheduler.SafeLinesForContexts(
                            scenes,
                            [RuntimeContext(CompanionEvent.Automatic, date.AddHours(20), observed)])
                            .ToArray()),
                    new SafeCapacityBand(
                        "LateNight+Dawn",
                        LateNightAndDawnSafeCapacity,
                        scheduler.SafeLinesForContexts(
                            scenes,
                            [
                                RuntimeContext(CompanionEvent.Automatic, date.AddHours(2), observed),
                                RuntimeContext(CompanionEvent.Automatic, date.AddHours(5), observed)
                            ])
                            .ToArray())
                };

                foreach (var band in bands)
                {
                    RequireCapacity(
                        band.Lines,
                        band.Required,
                        $"{band.Name} on {date:yyyy-MM-dd} "
                        + $"with fullscreen={FormatObserved(observed)}");
                }

                if (observed is not true)
                {
                    RequireSharedDailyCapacity(
                        bands,
                        $"{date:yyyy-MM-dd} with fullscreen={FormatObserved(observed)}");
                }
            }

            RequireCapacity(
                scheduler.SafeLinesForContexts(
                    scenes,
                    dayHours.Select(hour =>
                        RuntimeContext(CompanionEvent.Automatic, date.AddHours(hour), observed: true))),
                FullscreenSafeCapacity,
                $"Fullscreen full day on {date:yyyy-MM-dd}");
        }
    }

    private SafeFeedbackSelection? SelectSafeFeedbackLayer(
        IReadOnlyList<SceneDefinition> scenes,
        SceneContext context,
        IReadOnlySet<string> contextTokens,
        SceneHistory history,
        Random random,
        string? previousText,
        bool retainCooldownsAndAdjacency)
    {
        var recent = RecentHistoryProfile.Create(history);
        var candidates = scenes
            .Where(scene => IsSafeFeedbackScene(scene)
                            && TriggerAndContextMatch(scene, context, contextTokens, history))
            .Where(scene => !retainCooldownsAndAdjacency
                            || (!history.IsSemanticGroupCoolingDown(scene, context.Now)
                                && history.MeetsAdjacencyAndRecentQuotas(scene)))
            .Where(scene => SafeLines(
                scene,
                context.Now,
                history,
                previousText,
                retainCooldownsAndAdjacency).Count > 0)
            .Select(scene => Score(scene, recent))
            .ToList();

        while (candidates.Count > 0)
        {
            var scene = ChooseBest(candidates, random);
            if (scene is null)
            {
                return null;
            }

            var lines = SafeLines(
                scene,
                context.Now,
                history,
                previousText,
                retainCooldownsAndAdjacency);
            if (lines.Count > 0)
            {
                return new SafeFeedbackSelection(scene, ChooseUnusedOrLeastRecent(lines, history, random));
            }
            candidates.RemoveAll(candidate => candidate.Scene.Id == scene.Id);
        }

        return null;
    }

    private static IReadOnlyList<DialogueLine> SafeLines(
        SceneDefinition scene,
        DateTime now,
        SceneHistory history,
        string? previousText,
        bool retainLineCooldown) =>
        scene.Lines
            .Where(line => IsSafeFeedbackLine(scene, line)
                           && line.Text != previousText
                           && history.IsBelowDailyMaximum(line, now)
                           && (!retainLineCooldown || !history.IsLineCoolingDown(line, now)))
            .ToArray();

    private static DialogueLine ChooseUnusedOrLeastRecent(
        IReadOnlyList<DialogueLine> lines,
        SceneHistory history,
        Random random)
    {
        var lastPlayedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var entry in history.Entries)
        {
            if (!string.IsNullOrEmpty(entry.DialogueLineId))
            {
                lastPlayedAt[entry.DialogueLineId] = entry.PlayedAt;
            }
        }

        var unused = lines.Where(line => !lastPlayedAt.ContainsKey(line.Id)).ToArray();
        if (unused.Length > 0)
        {
            return WeightedLineChoice(history.PreferSurfaceExposure(unused), random);
        }

        var oldest = lines.Min(line => lastPlayedAt.GetValueOrDefault(line.Id, DateTime.MinValue));
        var leastRecent = lines
            .Where(line => lastPlayedAt.GetValueOrDefault(line.Id, DateTime.MinValue) == oldest)
            .ToArray();
        return WeightedLineChoice(history.PreferSurfaceExposure(leastRecent), random);
    }

    private void RequireTwoSafeLines(IReadOnlyList<SceneDefinition> scenes, SceneContext context)
    {
        var safe = SafeLinesForContexts(scenes, [context])
            .Select(line => line.Text)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();
        if (safe < 2)
        {
            throw new InvalidDataException(
                $"Safe-feedback coverage requires at least two distinct lines for "
                + $"{context.Trigger} at {context.Now:yyyy-MM-dd HH:mm} "
                + $"with fullscreen={FormatObserved(context.IsFullscreen)}; found {safe}.");
        }
    }

    private IEnumerable<DialogueLine> SafeLinesForContexts(
        IReadOnlyList<SceneDefinition> scenes,
        IEnumerable<SceneContext> contexts)
    {
        foreach (var context in contexts)
        {
            var history = CoverageHistory(context);
            var contextTokens = ContextTokens(context);
            foreach (var scene in scenes)
            {
                if (!IsSafeFeedbackScene(scene)
                    || !TriggerAndContextMatch(scene, context, contextTokens, history))
                {
                    continue;
                }
                foreach (var line in scene.Lines.Where(line =>
                             IsSafeFeedbackLine(scene, line) && line.MaxPerDay > 0))
                {
                    yield return line;
                }
            }
        }
    }

    private static SceneHistory CoverageHistory(SceneContext context)
    {
        var history = new SceneHistory();
        if (context.Trigger == CompanionEvent.Automatic)
        {
            history.Restore([
                new SceneHistoryEntry(
                    "safe-feedback-coverage-pressure",
                    "safe-feedback.coverage-pressure",
                    context.Now.AddMinutes(-1),
                    "safe-feedback coverage pressure")
            ]);
        }
        return history;
    }

    private static void RequireCapacity(
        IEnumerable<DialogueLine> lines,
        int required,
        string scenario)
    {
        var capacity = DistinctLineCapacity(lines);
        if (capacity < required)
        {
            throw new InvalidDataException(
                $"Safe-feedback capacity for {scenario} must be at least {required}; found {capacity}.");
        }
    }

    private static void RequireSharedDailyCapacity(
        IReadOnlyList<SafeCapacityBand> bands,
        string scenario)
    {
        for (var mask = 1; mask < 1 << bands.Count; mask++)
        {
            var selected = bands
                .Where((_, index) => (mask & 1 << index) != 0)
                .ToArray();
            var required = selected.Sum(band => band.Required);
            var capacity = DistinctLineCapacity(selected.SelectMany(band => band.Lines));
            if (capacity < required)
            {
                var bandNames = string.Join(" + ", selected.Select(band => band.Name));
                throw new InvalidDataException(
                    $"Safe-feedback shared daily capacity for {scenario} across {bandNames} "
                    + $"must be at least {required}; found {capacity}.");
            }
        }
    }

    private static int DistinctLineCapacity(IEnumerable<DialogueLine> lines) =>
        lines
            .GroupBy(line => line.Id, StringComparer.Ordinal)
            .Sum(group => group.Max(line => line.MaxPerDay));

    private static bool IsSafeFeedbackScene(SceneDefinition scene) =>
        scene.StoryArcId is null
        && scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
        && scene.Tone != "dry_sharp"
        && scene.OutputMode != DialogueOutputMode.UserDirect;

    private static bool IsSafeFeedbackLine(SceneDefinition scene, DialogueLine line) =>
        line.Enabled
        && !line.RequiresReply
        && !line.HasSeasoningMarker
        && scene.StoryArcId is null
        && scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
        && scene.Tone != "dry_sharp"
        && scene.OutputMode != DialogueOutputMode.UserDirect;

    private static SceneContext RuntimeContext(CompanionEvent trigger, DateTime now, bool? observed) =>
        new(
            trigger,
            now,
            CharacterState.Create(now),
            IsFullscreen: observed,
            EffectiveFullscreen: observed is true);

    private static string FormatObserved(bool? observed) => observed?.ToString() ?? "unknown";

    private sealed record SafeCapacityBand(
        string Name,
        int Required,
        IReadOnlyList<DialogueLine> Lines);
}
