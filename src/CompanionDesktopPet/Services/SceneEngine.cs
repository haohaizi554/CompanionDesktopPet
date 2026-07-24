using System.Text.Json.Serialization;

namespace CompanionDesktopPet.Services;

public enum SceneExpression
{
    ActionOnly,
    SelfTalk,
    Ambient,
    Direct
}

public sealed record SceneDefinition(
    string Id,
    string SemanticGroup,
    SceneExpression Expression,
    DialogueTreeKind Tree,
    DialogueCategory Category,
    IReadOnlySet<CompanionEvent> Triggers,
    int Priority,
    TimeSpan Cooldown,
    int InterruptionCost,
    string AnimationCue,
    IReadOnlyList<string> Variants,
    IReadOnlyList<DialogueLine> Lines,
    DialogueCategoryGroup CategoryGroup,
    DialogueOutputMode OutputMode,
    DialogueTrigger DialogueTrigger,
    IReadOnlyList<string> RequiredContext,
    TimeSpan SemanticCooldown,
    int MaxPerDay,
    double Weight,
    double EnergyDelta = 0,
    double SociabilityDelta = 0,
    double BoredomDelta = -0.03,
    string? StoryArcId = null,
    int StoryNode = -1);

public sealed record SceneContext(
    CompanionEvent Trigger,
    DateTime Now,
    CharacterState State,
    bool? IsFullscreen = null,
    TimeSpan UserIdle = default,
    DialogueTreeKind? PreferredTree = null,
    DialogueCategory? PreviousCategory = null);

public sealed record SceneHistoryEntry(
    [property: JsonRequired] string SceneId,
    [property: JsonRequired] string SemanticGroup,
    [property: JsonRequired] DateTime PlayedAt,
    [property: JsonRequired] string Variant,
    [property: JsonRequired] string DialogueLineId = "",
    [property: JsonRequired] DialogueCategory Category = DialogueCategory.CharacterLife,
    [property: JsonRequired] DialogueCategoryGroup CategoryGroup = DialogueCategoryGroup.CharacterLife,
    [property: JsonRequired] DialogueOutputMode OutputMode = DialogueOutputMode.SelfTalk,
    [property: JsonRequired] DialogueTrigger DialogueTrigger = DialogueTrigger.Any,
    [property: JsonRequired] int InterruptionCost = 0,
    [property: JsonRequired] DateOnly? PlayedLocalDate = null);

public sealed class SceneHistory
{
    public const int TechnicalRecentWindow = 5;
    public const int TechnicalRecentMaximum = 2;
    public const int UserDirectRecentWindow = 10;
    public const int UserDirectRecentMaximum = 2;
    public const int EasterEggRecentWindow = 50;
    public const int EasterEggRecentMaximum = 1;

    private readonly List<SceneHistoryEntry> _entries = [];

    public IReadOnlyList<SceneHistoryEntry> Entries => _entries;

    public void Record(SceneDefinition scene, DateTime playedAt, DialogueLine line)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(line);
        _entries.Add(new SceneHistoryEntry(
            scene.Id,
            line.SemanticGroup,
            playedAt,
            line.Text,
            line.Id,
            line.Category,
            line.CategoryGroup,
            line.OutputMode,
            line.Trigger,
            line.InterruptionCost,
            DateOnly.FromDateTime(playedAt)));
        Trim();
    }

    public void Record(SceneDefinition scene, DateTime playedAt, string variant)
    {
        var line = scene.Lines.FirstOrDefault(item => item.Text == variant) ?? scene.Lines[0];
        Record(scene, playedAt, line);
    }

    public void Restore(IEnumerable<SceneHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(entry => entry.PlayedAt).TakeLast(2_000));
    }

    public DateTime? LastSceneAt => _entries.Count == 0 ? null : _entries[^1].PlayedAt;

    public bool IsSceneCoolingDown(SceneDefinition scene, DateTime now) =>
        _entries.LastOrDefault(entry => entry.SceneId == scene.Id) is { } previous
        && Elapsed(now, previous.PlayedAt) < scene.Cooldown;

    public bool IsLineCoolingDown(DialogueLine line, DateTime now) =>
        _entries.LastOrDefault(entry => entry.DialogueLineId == line.Id) is { } previous
        && Elapsed(now, previous.PlayedAt) < line.Cooldown;

    public bool IsSemanticGroupCoolingDown(SceneDefinition scene, DateTime now) =>
        _entries.LastOrDefault(entry => entry.SemanticGroup == scene.SemanticGroup) is { } previous
        && Elapsed(now, previous.PlayedAt) < scene.SemanticCooldown;

    public bool IsBelowDailyMaximum(DialogueLine line, DateTime now)
    {
        var localDate = DateOnly.FromDateTime(now);
        return _entries.Count(entry =>
                   entry.DialogueLineId == line.Id
                   && (entry.PlayedLocalDate ?? DateOnly.FromDateTime(entry.PlayedAt)) == localDate)
               < line.MaxPerDay;
    }

    public bool MeetsAdjacencyAndRecentQuotas(SceneDefinition scene)
    {
        var last = _entries.LastOrDefault();
        if (last is not null
            && DialogueForest.BlockAdjacentCategoryGroups.Contains(scene.CategoryGroup)
            && last.CategoryGroup == scene.CategoryGroup)
        {
            return false;
        }

        if (scene.CategoryGroup == DialogueCategoryGroup.Technical
            && CandidateWindowCount(TechnicalRecentWindow,
                entry => entry.CategoryGroup == DialogueCategoryGroup.Technical) > TechnicalRecentMaximum)
        {
            return false;
        }

        if (scene.CategoryGroup == DialogueCategoryGroup.EasterEgg
            && CandidateWindowCount(EasterEggRecentWindow,
                entry => entry.CategoryGroup == DialogueCategoryGroup.EasterEgg) > EasterEggRecentMaximum)
        {
            return false;
        }

        return scene.OutputMode != DialogueOutputMode.UserDirect
               || CandidateWindowCount(UserDirectRecentWindow,
                   entry => entry.OutputMode == DialogueOutputMode.UserDirect) <= UserDirectRecentMaximum;
    }

    public IReadOnlyList<DialogueLine> EligibleLines(SceneDefinition scene, DateTime now) =>
        scene.Lines
            .Where(line => !IsLineCoolingDown(line, now) && IsBelowDailyMaximum(line, now))
            .ToArray();

    public DateTime NextEligibleAt(SceneDefinition scene, DateTime now)
    {
        var next = now.AddHours(1);
        if (LastSceneAt is { } lastScene)
        {
            next = Later(next, lastScene.AddMinutes(InterruptionBudget.CostIntervalsMinutes[scene.InterruptionCost]));
        }

        if (_entries.LastOrDefault(entry => entry.SemanticGroup == scene.SemanticGroup) is { } semantic)
        {
            next = Later(next, semantic.PlayedAt + scene.SemanticCooldown);
        }

        var lineTimes = scene.Lines.Select(line =>
        {
            var candidate = now;
            if (_entries.LastOrDefault(entry => entry.DialogueLineId == line.Id) is { } previous)
            {
                candidate = Later(candidate, previous.PlayedAt + line.Cooldown);
            }
            if (!IsBelowDailyMaximum(line, now))
            {
                candidate = Later(candidate, now.Date.AddDays(1));
            }
            return candidate;
        });
        return Later(next, lineTimes.Min());
    }

    public static TimeSpan Elapsed(DateTime now, DateTime then)
    {
        if (now.Kind != DateTimeKind.Unspecified && then.Kind != DateTimeKind.Unspecified)
        {
            return now.ToUniversalTime() - then.ToUniversalTime();
        }

        return now - then;
    }

    private int CandidateWindowCount(int window, Func<SceneHistoryEntry, bool> predicate) =>
        _entries.TakeLast(Math.Max(0, window - 1)).Count(predicate) + 1;

    private static DateTime Later(DateTime left, DateTime right) => left >= right ? left : right;

    private void Trim()
    {
        if (_entries.Count > 2_000)
        {
            _entries.RemoveRange(0, _entries.Count - 2_000);
        }
    }
}

public static class InterruptionBudget
{
    public const int MinimumIntervalMinutes = 8;
    public const int MaximumOutputsPerHour = 2;
    public const int LateNightMaximumOutputsPerHour = 1;

    public static IReadOnlyDictionary<int, int> CostIntervalsMinutes { get; } =
        new Dictionary<int, int> { [0] = 8, [1] = 12, [2] = 16, [3] = 24, [4] = 40, [5] = 60 };

    public static bool CanPlay(SceneDefinition scene, DateTime now, SceneHistory history, bool? isFullscreen)
    {
        var last = history.LastSceneAt;
        if (last is { } lastAt)
        {
            var elapsed = SceneHistory.Elapsed(now, lastAt);
            if (elapsed < TimeSpan.FromMinutes(CostIntervalsMinutes[scene.InterruptionCost]))
            {
                return false;
            }

            if (isFullscreen is true && elapsed < TimeSpan.FromHours(2))
            {
                return false;
            }
        }

        var recentHour = history.Entries.Where(entry =>
        {
            var elapsed = SceneHistory.Elapsed(now, entry.PlayedAt);
            return elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromHours(1);
        }).ToArray();
        if (recentHour.Length >= MaximumOutputsPerHour)
        {
            return false;
        }

        return DayPart(now) != DialogueTrigger.LateNight
               || recentHour.Count(entry => DayPart(entry.PlayedAt) == DialogueTrigger.LateNight)
               < LateNightMaximumOutputsPerHour;
    }

    public static bool CanInterrupt(
        CompanionEvent trigger,
        DateTime now,
        SceneHistory history,
        bool isFullscreen)
    {
        var last = history.LastSceneAt;
        if (last is { } lastAt && SceneHistory.Elapsed(now, lastAt) < TimeSpan.FromMinutes(MinimumIntervalMinutes))
        {
            return false;
        }

        return history.Entries.Count(entry =>
        {
            var elapsed = SceneHistory.Elapsed(now, entry.PlayedAt);
            return elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromHours(1);
        }) < MaximumOutputsPerHour;
    }

    internal static DialogueTrigger DayPart(DateTime now) => TemporalDialogueService.GetDialogueTrigger(now);
}

internal sealed record ClickFallbackSelection(
    SceneDefinition Scene,
    DialogueLine? ReusedLine = null);

public sealed class SceneScheduler
{
    public SceneDefinition? Select(
        SceneContext context,
        SceneHistory history,
        Random random,
        bool bypassInterruptionBudget = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(random);

        if (context.Trigger == CompanionEvent.StoryTimerDue)
        {
            var due = context.State.ActiveStories
                .Where(story => story.DueAt <= context.Now)
                .OrderBy(story => story.DueAt)
                .FirstOrDefault();
            if (due is not null)
            {
                var storyScene = StoryArcCatalog.All.Single(arc => arc.Id == due.ArcId).Nodes[due.NodeIndex];
                return CanSelect(storyScene, context, history, ignoreTrigger: true) ? storyScene : null;
            }
        }

        var candidates = AvailableScenes(context)
            .Where(scene => CanSelect(
                scene,
                context,
                history,
                ignoreTrigger: false,
                bypassInterruptionBudget))
            .Select(scene => Score(scene, history))
            .ToArray();
        return ChooseBest(candidates, random);
    }

    internal ClickFallbackSelection? SelectClickFallback(
        SceneContext context,
        SceneHistory history,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(random);

        if (context.Trigger != CompanionEvent.Click)
        {
            return null;
        }

        var quotaRelaxed = AvailableScenes(context)
            .Where(scene => TriggerAndContextMatch(scene, context, history))
            .Where(scene => !history.IsSemanticGroupCoolingDown(scene, context.Now))
            .Where(scene => history.EligibleLines(scene, context.Now).Count > 0)
            .Select(scene => Score(scene, history))
            .ToArray();
        if (ChooseBest(quotaRelaxed, random) is { } quotaRelaxedScene)
        {
            return new ClickFallbackSelection(quotaRelaxedScene);
        }

        var reusableScenes = AvailableScenes(context)
            .Where(scene => TriggerAndContextMatch(scene, context, history))
            .Where(scene => scene.Lines.Any(line => line.Enabled))
            .ToArray();
        if (reusableScenes.Length == 0)
        {
            return null;
        }

        var lastLineId = history.Entries.LastOrDefault()?.DialogueLineId;
        var nonRepeatingScenes = reusableScenes
            .Where(scene => scene.Lines.Any(line => line.Enabled && line.Id != lastLineId))
            .ToArray();
        var reusableScene = ChooseBest(
            (nonRepeatingScenes.Length > 0 ? nonRepeatingScenes : reusableScenes)
                .Select(scene => Score(scene, history))
                .ToArray(),
            random);
        if (reusableScene is null)
        {
            return null;
        }

        return new ClickFallbackSelection(
            reusableScene,
            SelectLeastRecentlyUsedLine(reusableScene, history, random));
    }

    private static SceneDefinition? ChooseBest(IReadOnlyList<ScoredScene> candidates, Random random)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestBand = candidates.Max(candidate => candidate.Band);
        var band = candidates.Where(candidate => candidate.Band == bestBand).OrderBy(candidate => candidate.Scene.Id).ToArray();
        var roll = random.NextDouble() * band.Sum(candidate => candidate.Scene.Weight);
        foreach (var candidate in band)
        {
            roll -= candidate.Scene.Weight;
            if (roll <= 0)
            {
                return candidate.Scene;
            }
        }

        return band[^1].Scene;
    }

    private static DialogueLine SelectLeastRecentlyUsedLine(
        SceneDefinition scene,
        SceneHistory history,
        Random random)
    {
        var enabled = scene.Lines.Where(line => line.Enabled).ToArray();
        var lastLineId = history.Entries.LastOrDefault()?.DialogueLineId;
        var nonRepeating = enabled.Where(line => line.Id != lastLineId).ToArray();
        var candidates = nonRepeating.Length > 0 ? nonRepeating : enabled;
        var lastPlayedAt = candidates.ToDictionary(
            line => line.Id,
            line => history.Entries.LastOrDefault(entry => entry.DialogueLineId == line.Id)?.PlayedAt
                    ?? DateTime.MinValue);
        var oldest = lastPlayedAt.Values.Min();
        var leastRecentlyUsed = candidates.Where(line => lastPlayedAt[line.Id] == oldest).ToArray();

        var roll = random.NextDouble() * leastRecentlyUsed.Sum(line => line.Weight);
        foreach (var line in leastRecentlyUsed)
        {
            roll -= line.Weight;
            if (roll <= 0)
            {
                return line;
            }
        }

        return leastRecentlyUsed[^1];
    }

    private static IEnumerable<SceneDefinition> AvailableScenes(SceneContext context) =>
        SceneCatalog.All.Where(scene =>
            scene.StoryArcId is null || (scene.StoryNode == 0 && context.State.ActiveStories.Count == 0));

    private static bool TriggerAndContextMatch(
        SceneDefinition scene,
        SceneContext context,
        SceneHistory history) =>
        scene.Triggers.Contains(context.Trigger)
        && TriggerMatches(scene, context, history)
        && ContextMatches(scene, context);

    private static bool CanSelect(
        SceneDefinition scene,
        SceneContext context,
        SceneHistory history,
        bool ignoreTrigger,
        bool bypassInterruptionBudget = false) =>
        (ignoreTrigger
            ? ContextMatches(scene, context)
            : TriggerAndContextMatch(scene, context, history))
        && !history.IsSemanticGroupCoolingDown(scene, context.Now)
        && history.MeetsAdjacencyAndRecentQuotas(scene)
        && history.EligibleLines(scene, context.Now).Count > 0
        && (bypassInterruptionBudget
            || InterruptionBudget.CanPlay(scene, context.Now, history, context.IsFullscreen));

    private static bool TriggerMatches(SceneDefinition scene, SceneContext context, SceneHistory history)
    {
        var trigger = scene.DialogueTrigger;
        return trigger switch
        {
            DialogueTrigger.Any => true,
            DialogueTrigger.AppStart => context.Trigger == CompanionEvent.Startup,
            DialogueTrigger.DayChanged => context.Trigger == CompanionEvent.DayChanged,
            DialogueTrigger.Morning or DialogueTrigger.Noon or DialogueTrigger.Afternoon
                or DialogueTrigger.Evening or DialogueTrigger.LateNight => InterruptionBudget.DayPart(context.Now) == trigger,
            DialogueTrigger.Weekday => context.Now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday,
            DialogueTrigger.Weekend => context.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            DialogueTrigger.Holiday => TemporalDialogueService.GetFestivals(context.Now).Count > 0,
            DialogueTrigger.Anniversary => context.State.AttachmentDays > 1,
            DialogueTrigger.LongSilence => history.LastSceneAt is null
                || SceneHistory.Elapsed(context.Now, history.LastSceneAt.Value) >= TimeSpan.FromMinutes(180),
            DialogueTrigger.IdleReturn => context.Trigger == CompanionEvent.IdleReturned,
            DialogueTrigger.StoryTimer => context.Trigger == CompanionEvent.StoryTimerDue,
            _ => false
        };
    }

    private static bool ContextMatches(SceneDefinition scene, SceneContext context)
    {
        if (scene.RequiredContext.Count == 1 && scene.RequiredContext[0] == "none")
        {
            return true;
        }

        var tokens = ContextTokens(context);
        return scene.RequiredContext.All(tokens.Contains);
    }

    private static HashSet<string> ContextTokens(SceneContext context)
    {
        var now = context.Now;
        var tokens = new HashSet<string>(StringComparer.Ordinal)
        {
            now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "day:weekend" : "day:weekday",
            TemporalDialogueService.GetTimeContextToken(now),
            $"season:{(now.Month is >= 3 and <= 5 ? "spring" : now.Month is >= 6 and <= 8 ? "summer" : now.Month is >= 9 and <= 11 ? "autumn" : "winter")}"
        };
        if (context.Trigger == CompanionEvent.Startup) tokens.Add("app_started");
        if (context.Trigger == CompanionEvent.IdleReturned) tokens.Add("idle_return");
        if (context.IsFullscreen is false) tokens.Add("not_fullscreen");
        if (TemporalDialogueService.GetFestivals(now).Count > 0)
        {
            tokens.Add("holiday");
            tokens.Add("date:holiday");
        }
        if (context.State.AttachmentDays > 1) tokens.Add("anniversary");
        if (now.Day == 1 || now.AddDays(1).Month != now.Month) tokens.Add("date:month_boundary");
        return tokens;
    }

    private static ScoredScene Score(SceneDefinition scene, SceneHistory history)
    {
        var recent = history.Entries.TakeLast(50).ToArray();
        var total = recent.Length;
        var groupObserved = total == 0 ? 0 : recent.Count(entry => entry.CategoryGroup == scene.CategoryGroup) / (double)total;
        var modeObserved = total == 0 ? 0 : recent.Count(entry => entry.OutputMode == scene.OutputMode) / (double)total;
        var categoryObserved = total == 0 ? 0 : recent.Count(entry => entry.Category == scene.Category) / (double)total;
        var score = (DialogueForest.CategoryGroupWeights[scene.CategoryGroup] - groupObserved) * 100
                    + (DialogueForest.OutputModeTargets[scene.OutputMode] - modeObserved) * 35
                    + scene.Weight * 0.5
                    - scene.InterruptionCost * 0.75
                    - categoryObserved * 5;
        return new ScoredScene(scene, score, (int)Math.Floor(score));
    }

    private sealed record ScoredScene(SceneDefinition Scene, double Score, int Band);
}
