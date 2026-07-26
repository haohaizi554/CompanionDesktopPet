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
    string Tone,
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
    DialogueCategory? PreviousCategory = null,
    bool EffectiveFullscreen = false);

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
    [property: JsonRequired] DateOnly? PlayedLocalDate = null,
    bool WasDrySharp = false,
    bool? WasSeasoning = null,
    string SurfaceOpening = "",
    string SurfaceEnding = "",
    string SurfaceTemplate = "");

public sealed class SceneHistory
{
    public const int TechnicalRecentWindow = 5;
    public const int TechnicalRecentMaximum = 2;
    public const int UserDirectRecentWindow = 10;
    public const int UserDirectRecentMaximum = 2;
    public const int EasterEggRecentWindow = PersonaContractGenerated.EasterEggRecentWindow;
    public const int EasterEggRecentMaximum = PersonaContractGenerated.EasterEggRecentMaximum;
    public const int DrySharpRecentWindow = PersonaContractGenerated.DrySharpRecentWindow;
    public const int DrySharpRecentMaximum = PersonaContractGenerated.DrySharpRecentMaximum;
    public const int DrySharpPlaybackWindow = 50;
    public const int DrySharpPlaybackMaximum =
        (int)(PersonaContractGenerated.DrySharpPlaybackMaximum * DrySharpPlaybackWindow);
    public const int SeasoningRecentWindow = PersonaContractGenerated.SeasoningRecentWindow;
    public const int SeasoningRecentMaximum = PersonaContractGenerated.SeasoningRecentMaximum;

    private readonly List<SceneHistoryEntry> _entries = [];
    private readonly Dictionary<string, SceneHistoryEntry> _lastBySceneId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneHistoryEntry> _lastBySemanticGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneHistoryEntry> _lastByLineId = new(StringComparer.Ordinal);
    private readonly Dictionary<(string LineId, DateOnly Date), int> _dailyCounts = [];
    private readonly Dictionary<string, HashSet<string>> _seenLineIdsBySemanticGroup = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<SceneHistoryEntry> _entriesView;

    public SceneHistory() => _entriesView = _entries.AsReadOnly();

    public IReadOnlyList<SceneHistoryEntry> Entries => _entriesView;

    public void Record(SceneDefinition scene, DateTime playedAt, DialogueLine line)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(line);
        var surface = line.SurfaceExposureProfile;
        var entry = new SceneHistoryEntry(
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
            DateOnly.FromDateTime(playedAt),
            line.Tone == "dry_sharp",
            line.HasSeasoningMarker,
            surface.Opening,
            surface.Ending,
            surface.Template);
        _entries.Add(entry);
        Index(entry);
        if (Trim())
        {
            RebuildIndexes();
        }
    }

    public void Record(SceneDefinition scene, DateTime playedAt, string variant)
    {
        var line = scene.Lines.FirstOrDefault(item => item.Text == variant) ?? scene.Lines[0];
        Record(scene, playedAt, line);
    }

    public void Restore(IEnumerable<SceneHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var restored = entries
            .OrderBy(entry => entry.PlayedAt)
            .TakeLast(2_000)
            .ToArray();
        _entries.Clear();
        _entries.AddRange(restored);
        RebuildIndexes();
    }

    public DateTime? LastSceneAt => _entries.Count == 0 ? null : _entries[^1].PlayedAt;

    public bool IsSceneCoolingDown(SceneDefinition scene, DateTime now) =>
        _lastBySceneId.GetValueOrDefault(scene.Id) is { } previous
        && Elapsed(now, previous.PlayedAt) < scene.Cooldown;

    public bool IsLineCoolingDown(DialogueLine line, DateTime now) =>
        _lastByLineId.GetValueOrDefault(line.Id) is { } previous
        && Elapsed(now, previous.PlayedAt) < line.Cooldown;

    internal bool TryGetLastPlayedAt(string lineId, out DateTime playedAt)
    {
        if (_lastByLineId.TryGetValue(lineId, out var previous))
        {
            playedAt = previous.PlayedAt;
            return true;
        }

        playedAt = default;
        return false;
    }

    public bool IsSemanticGroupCoolingDown(SceneDefinition scene, DateTime now) =>
        _lastBySemanticGroup.GetValueOrDefault(scene.SemanticGroup) is { } previous
        && Elapsed(now, previous.PlayedAt) < scene.SemanticCooldown;

    public bool IsBelowDailyMaximum(DialogueLine line, DateTime now)
    {
        var localDate = DateOnly.FromDateTime(now);
        return _dailyCounts.GetValueOrDefault((line.Id, localDate)) < line.MaxPerDay;
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

        if (!MeetsRareRecentQuotas(scene))
        {
            return false;
        }

        return scene.OutputMode != DialogueOutputMode.UserDirect
               || CandidateWindowCount(UserDirectRecentWindow,
                   entry => entry.OutputMode == DialogueOutputMode.UserDirect) <= UserDirectRecentMaximum;
    }

    public bool MeetsRareRecentQuotas(SceneDefinition scene)
    {
        if (scene.CategoryGroup == DialogueCategoryGroup.EasterEgg
            && CandidateWindowCount(EasterEggRecentWindow,
                entry => entry.CategoryGroup == DialogueCategoryGroup.EasterEgg) > EasterEggRecentMaximum)
        {
            return false;
        }

        return scene.Tone != "dry_sharp"
               || (CandidateWindowCount(DrySharpRecentWindow, IsDrySharpEntry)
                   <= DrySharpRecentMaximum
                   && CandidateWindowCount(DrySharpPlaybackWindow, IsDrySharpEntry)
                   <= DrySharpPlaybackMaximum);
    }

    public IReadOnlyList<DialogueLine> EligibleLines(SceneDefinition scene, DateTime now) =>
        scene.Lines
            .Where(line => MeetsLineExposureQuota(line)
                           && !IsLineCoolingDown(line, now)
                           && IsBelowDailyMaximum(line, now))
            .ToArray();

    public bool MeetsLineExposureQuota(DialogueLine line) =>
        !line.HasSeasoningMarker
        || CandidateWindowCount(
            SeasoningRecentWindow,
            IsSeasoningEntry) <= SeasoningRecentMaximum;

    public IReadOnlyList<DialogueLine> PreferSurfaceExposure(IReadOnlyList<DialogueLine> lines)
    {
        if (lines.Count == 0)
        {
            return lines;
        }

        var recentStart = Math.Max(0, _entries.Count - SurfaceExposure.RecentWindow);
        var diverse = new List<DialogueLine>(lines.Count);
        var minimumConflicts = int.MaxValue;
        foreach (var line in lines)
        {
            var profile = line.SurfaceExposureProfile;
            var openingConflict = false;
            var endingConflict = false;
            var templateConflict = false;
            for (var index = recentStart; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                openingConflict |= profile.Opening.Length > 0
                                   && entry.SurfaceOpening == profile.Opening;
                endingConflict |= profile.Ending.Length > 0
                                  && entry.SurfaceEnding == profile.Ending;
                templateConflict |= profile.Template.Length > 0
                                    && entry.SurfaceTemplate == profile.Template;
            }
            var conflicts = Convert.ToInt32(openingConflict)
                            + Convert.ToInt32(endingConflict)
                            + Convert.ToInt32(templateConflict);
            if (conflicts < minimumConflicts)
            {
                diverse.Clear();
                minimumConflicts = conflicts;
            }
            if (conflicts == minimumConflicts)
            {
                diverse.Add(line);
            }
        }

        var scoreStart = Math.Max(0, _entries.Count - 50);
        var seasoningCount = 0;
        for (var index = scoreStart; index < _entries.Count; index++)
        {
            seasoningCount += Convert.ToInt32(IsSeasoningEntry(_entries[index]));
        }
        var scoreCount = _entries.Count - scoreStart;
        var observed = scoreCount == 0 ? 0 : seasoningCount / (double)scoreCount;
        var target = (PersonaContractGenerated.SeasoningPlaybackMinimum
                      + PersonaContractGenerated.SeasoningPlaybackMaximum) / 2;
        var seasoning = new List<DialogueLine>(diverse.Count);
        var neutral = new List<DialogueLine>(diverse.Count);
        foreach (var line in diverse)
        {
            (line.HasSeasoningMarker ? seasoning : neutral).Add(line);
        }
        if (observed < target && seasoning.Count > 0)
        {
            return seasoning.ToArray();
        }
        if (observed > target && neutral.Count > 0)
        {
            return neutral.ToArray();
        }
        return diverse.ToArray();
    }

    public bool HasEligibleLine(SceneDefinition scene, DateTime now)
    {
        if (scene.Lines.Count == 0 || !scene.Lines[0].Enabled)
        {
            return false;
        }

        _seenLineIdsBySemanticGroup.TryGetValue(scene.SemanticGroup, out var seen);
        foreach (var line in scene.Lines)
        {
            if (!MeetsLineExposureQuota(line))
            {
                continue;
            }
            if (seen is null || !seen.Contains(line.Id))
            {
                return true;
            }
            if (!IsLineCoolingDown(line, now) && IsBelowDailyMaximum(line, now))
            {
                return true;
            }
        }
        return false;
    }

    public DateTime NextEligibleAt(SceneDefinition scene, DateTime now)
    {
        var next = now.AddHours(1);
        if (LastSceneAt is { } lastScene)
        {
            next = Later(next, lastScene.AddMinutes(InterruptionBudget.CostIntervalsMinutes[scene.InterruptionCost]));
        }

        if (_lastBySemanticGroup.GetValueOrDefault(scene.SemanticGroup) is { } semantic)
        {
            next = Later(next, semantic.PlayedAt + scene.SemanticCooldown);
        }

        var lineTimes = scene.Lines.Select(line =>
        {
            var candidate = now;
            if (_lastByLineId.GetValueOrDefault(line.Id) is { } previous)
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

    private static bool IsDrySharpEntry(SceneHistoryEntry entry) =>
        entry.WasDrySharp
        || SceneCatalog.DrySharpSemanticGroups.Contains(entry.SemanticGroup);

    private static bool IsSeasoningEntry(SceneHistoryEntry entry) =>
        entry.WasSeasoning
        ?? PersonaContractGenerated.ContainsSeasoningMarker(entry.Variant);

    private static DateTime Later(DateTime left, DateTime right) => left >= right ? left : right;

    private void Index(SceneHistoryEntry entry)
    {
        _lastBySceneId[entry.SceneId] = entry;
        _lastBySemanticGroup[entry.SemanticGroup] = entry;
        if (!string.IsNullOrEmpty(entry.DialogueLineId))
        {
            _lastByLineId[entry.DialogueLineId] = entry;
            var localDate = entry.PlayedLocalDate ?? DateOnly.FromDateTime(entry.PlayedAt);
            var dailyKey = (entry.DialogueLineId, localDate);
            _dailyCounts[dailyKey] = _dailyCounts.GetValueOrDefault(dailyKey) + 1;
            if (!_seenLineIdsBySemanticGroup.TryGetValue(entry.SemanticGroup, out var lineIds))
            {
                lineIds = new HashSet<string>(StringComparer.Ordinal);
                _seenLineIdsBySemanticGroup[entry.SemanticGroup] = lineIds;
            }
            lineIds.Add(entry.DialogueLineId);
        }
    }

    private void RebuildIndexes()
    {
        _lastBySceneId.Clear();
        _lastBySemanticGroup.Clear();
        _lastByLineId.Clear();
        _dailyCounts.Clear();
        _seenLineIdsBySemanticGroup.Clear();
        foreach (var entry in _entries)
        {
            Index(entry);
        }
    }

    private bool Trim()
    {
        if (_entries.Count > 2_000)
        {
            _entries.RemoveRange(0, _entries.Count - 2_000);
            return true;
        }

        return false;
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

public sealed partial class SceneScheduler
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
        var contextTokens = ContextTokens(context);

        if (context.Trigger == CompanionEvent.StoryTimerDue)
        {
            var due = context.State.ActiveStories
                .Where(story => story.DueAt <= context.Now)
                .OrderBy(story => story.DueAt)
                .FirstOrDefault();
            if (due is not null)
            {
                var storyScene = StoryArcCatalog.All.Single(arc => arc.Id == due.ArcId).Nodes[due.NodeIndex];
                return CanSelect(storyScene, context, contextTokens, history, ignoreTrigger: true)
                       && history.HasEligibleLine(storyScene, context.Now)
                    ? storyScene
                    : null;
            }
        }

        var recent = RecentHistoryProfile.Create(history);
        var candidates = AvailableScenes(context)
            .Where(scene => CanSelect(
                scene,
                context,
                contextTokens,
                history,
                ignoreTrigger: false,
                bypassInterruptionBudget))
            .Select(scene => Score(scene, recent))
            .ToArray();
        return ChooseBestWithEligibleLine(candidates, context.Now, history, random);
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

        var contextTokens = ContextTokens(context);
        var recent = RecentHistoryProfile.Create(history);
        var quotaRelaxed = AvailableScenes(context)
            .Where(scene => TriggerAndContextMatch(scene, context, contextTokens, history))
            .Where(scene => !history.IsSemanticGroupCoolingDown(scene, context.Now))
            .Where(history.MeetsRareRecentQuotas)
            .Select(scene => Score(scene, recent))
            .ToArray();
        if (ChooseBestWithEligibleLine(quotaRelaxed, context.Now, history, random) is { } quotaRelaxedScene)
        {
            return new ClickFallbackSelection(quotaRelaxedScene);
        }

        var reusableScenes = AvailableScenes(context)
            .Where(scene => TriggerAndContextMatch(scene, context, contextTokens, history))
            .Where(history.MeetsRareRecentQuotas)
            .Where(scene => scene.Lines.Any(line =>
                line.Enabled && history.MeetsLineExposureQuota(line)))
            .ToArray();
        if (reusableScenes.Length == 0)
        {
            return null;
        }

        return SelectReusableClickFallback(reusableScenes, history, random);
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

    private static SceneDefinition? ChooseBestWithEligibleLine(
        IReadOnlyList<ScoredScene> candidates,
        DateTime now,
        SceneHistory history,
        Random random)
    {
        var remaining = candidates.ToList();
        while (remaining.Count > 0)
        {
            var selected = ChooseBest(remaining, random);
            if (selected is null)
            {
                return null;
            }
            if (history.HasEligibleLine(selected, now))
            {
                return selected;
            }
            remaining.RemoveAll(candidate => candidate.Scene.Id == selected.Id);
        }

        return null;
    }

    internal ClickFallbackSelection? SelectReusableClickFallback(
        IReadOnlyList<SceneDefinition> scenes,
        SceneHistory history,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(random);

        var quotaEligibleScenes = scenes.Where(history.MeetsRareRecentQuotas).ToArray();
        if (quotaEligibleScenes.Length == 0)
        {
            return null;
        }

        var lastLineId = history.Entries.LastOrDefault()?.DialogueLineId;
        var recent = RecentHistoryProfile.Create(history);
        var hasNonRepeatingLine = quotaEligibleScenes.Any(scene =>
            scene.Lines.Any(line =>
                line.Enabled
                && history.MeetsLineExposureQuota(line)
                && line.Id != lastLineId));
        var lastPlayedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var entry in history.Entries)
        {
            lastPlayedAt[entry.DialogueLineId] = entry.PlayedAt;
        }

        var scenesWithUnusedLines = quotaEligibleScenes
            .Where(scene => scene.Lines.Any(line =>
                line.Enabled
                && history.MeetsLineExposureQuota(line)
                && (!hasNonRepeatingLine || line.Id != lastLineId)
                && !lastPlayedAt.ContainsKey(line.Id)))
            .Select(scene => Score(scene, recent))
            .ToArray();
        if (ChooseBest(scenesWithUnusedLines, random) is { } unusedScene)
        {
            var unusedLines = unusedScene.Lines
                .Where(line =>
                    line.Enabled
                    && history.MeetsLineExposureQuota(line)
                    && (!hasNonRepeatingLine || line.Id != lastLineId)
                    && !lastPlayedAt.ContainsKey(line.Id))
                .ToArray();
            return new ClickFallbackSelection(
                unusedScene,
                WeightedLineChoice(history.PreferSurfaceExposure(unusedLines), random));
        }

        var reusableScenes = quotaEligibleScenes
            .Where(scene => scene.Lines.Any(line =>
                line.Enabled
                && history.MeetsLineExposureQuota(line)
                && (!hasNonRepeatingLine || line.Id != lastLineId)))
            .Select(scene => Score(scene, recent))
            .ToArray();
        var selectedScene = ChooseBest(reusableScenes, random);
        if (selectedScene is null)
        {
            return null;
        }

        var oldest = selectedScene.Lines
            .Where(line =>
                line.Enabled
                && history.MeetsLineExposureQuota(line)
                && (!hasNonRepeatingLine || line.Id != lastLineId))
            .Min(line => lastPlayedAt.GetValueOrDefault(line.Id, DateTime.MinValue));
        var leastRecentlyUsed = selectedScene.Lines
            .Where(line =>
                line.Enabled
                && history.MeetsLineExposureQuota(line)
                && (!hasNonRepeatingLine || line.Id != lastLineId)
                && lastPlayedAt.GetValueOrDefault(line.Id, DateTime.MinValue) == oldest)
            .ToArray();

        return new ClickFallbackSelection(
            selectedScene,
            WeightedLineChoice(history.PreferSurfaceExposure(leastRecentlyUsed), random));
    }

    private static DialogueLine WeightedLineChoice(
        IReadOnlyList<DialogueLine> lines,
        Random random)
    {
        var roll = random.NextDouble() * lines.Sum(line => line.Weight);
        foreach (var line in lines)
        {
            roll -= line.Weight;
            if (roll <= 0)
            {
                return line;
            }
        }

        return lines[^1];
    }

    internal static IEnumerable<SceneDefinition> AvailableScenes(SceneContext context) =>
        SceneCatalog.All.Where(scene => scene.StoryArcId is not null
            ? scene.StoryNode == 0 && context.State.ActiveStories.Count == 0
            : !StoryArcCatalog.ReservedPersonaSemanticGroups.Contains(scene.SemanticGroup));

    private static bool TriggerAndContextMatch(
        SceneDefinition scene,
        SceneContext context,
        IReadOnlySet<string> contextTokens,
        SceneHistory history) =>
        scene.Triggers.Contains(context.Trigger)
        && TriggerMatches(scene, context, history)
        && ContextMatches(scene, contextTokens);

    private static bool CanSelect(
        SceneDefinition scene,
        SceneContext context,
        IReadOnlySet<string> contextTokens,
        SceneHistory history,
        bool ignoreTrigger,
        bool bypassInterruptionBudget = false) =>
        (ignoreTrigger
            ? ContextMatches(scene, contextTokens)
            : TriggerAndContextMatch(scene, context, contextTokens, history))
        && !history.IsSemanticGroupCoolingDown(scene, context.Now)
        && history.MeetsAdjacencyAndRecentQuotas(scene)
        && (bypassInterruptionBudget
            || InterruptionBudget.CanPlay(scene, context.Now, history, context.EffectiveFullscreen));

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

    private static bool ContextMatches(SceneDefinition scene, IReadOnlySet<string> tokens)
    {
        if (scene.RequiredContext.Count == 1 && scene.RequiredContext[0] == "none")
        {
            return true;
        }

        return scene.RequiredContext.All(tokens.Contains);
    }

    internal static HashSet<string> ContextTokens(SceneContext context)
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

    private static ScoredScene Score(SceneDefinition scene, RecentHistoryProfile recent)
    {
        var total = recent.Total;
        var groupObserved = total == 0
            ? 0
            : recent.CategoryGroups.GetValueOrDefault(scene.CategoryGroup) / (double)total;
        var modeObserved = total == 0
            ? 0
            : recent.OutputModes.GetValueOrDefault(scene.OutputMode) / (double)total;
        var categoryObserved = total == 0
            ? 0
            : recent.Categories.GetValueOrDefault(scene.Category) / (double)total;
        var drySharpObserved = total == 0 ? 0 : recent.DrySharpCount / (double)total;
        var drySharpDeficit = PersonaContractGenerated.DrySharpPlaybackTarget - drySharpObserved;
        var drySharpBonus = scene.Tone == "dry_sharp" ? drySharpDeficit * 200 : 0;
        var weightBonus = scene.Weight * 0.5;
        var score = (DialogueForest.CategoryGroupWeights[scene.CategoryGroup] - groupObserved) * 100
                    + (DialogueForest.OutputModeTargets[scene.OutputMode] - modeObserved) * 35
                    + weightBonus
                    - scene.InterruptionCost * 0.75
                    - categoryObserved * 5
                    + drySharpBonus;
        return new ScoredScene(scene, score, (int)Math.Floor(score - weightBonus));
    }

    private sealed record RecentHistoryProfile(
        int Total,
        IReadOnlyDictionary<DialogueCategoryGroup, int> CategoryGroups,
        IReadOnlyDictionary<DialogueOutputMode, int> OutputModes,
        IReadOnlyDictionary<DialogueCategory, int> Categories,
        int DrySharpCount)
    {
        public static RecentHistoryProfile Create(SceneHistory history)
        {
            var categoryGroups = new Dictionary<DialogueCategoryGroup, int>();
            var outputModes = new Dictionary<DialogueOutputMode, int>();
            var categories = new Dictionary<DialogueCategory, int>();
            var recent = history.Entries.TakeLast(50).ToArray();
            var drySharpCount = 0;
            foreach (var entry in recent)
            {
                categoryGroups[entry.CategoryGroup] = categoryGroups.GetValueOrDefault(entry.CategoryGroup) + 1;
                outputModes[entry.OutputMode] = outputModes.GetValueOrDefault(entry.OutputMode) + 1;
                categories[entry.Category] = categories.GetValueOrDefault(entry.Category) + 1;
                if (entry.WasDrySharp
                    || SceneCatalog.DrySharpSemanticGroups.Contains(entry.SemanticGroup))
                {
                    drySharpCount++;
                }
            }
            return new RecentHistoryProfile(
                recent.Length,
                categoryGroups,
                outputModes,
                categories,
                drySharpCount);
        }
    }

    private sealed record ScoredScene(SceneDefinition Scene, double Score, int Band);
}
