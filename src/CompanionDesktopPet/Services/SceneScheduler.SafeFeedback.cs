using System.IO;

namespace CompanionDesktopPet.Services;

internal sealed record SafeFeedbackSelection(SceneDefinition Scene, DialogueLine Line);

public sealed partial class SceneScheduler
{
    private static readonly SafeFeedbackCoveragePeriod[] CoveragePeriods =
    [
        new("Dawn", 5, "time:dawn", NonFullscreenRequired: 4, FullscreenRequired: 2),
        new("Morning", 10, "time:morning", NonFullscreenRequired: 60, FullscreenRequired: 5),
        new("Noon", 11, "time:noon", NonFullscreenRequired: 36, FullscreenRequired: 3),
        new("Afternoon", 14, "time:afternoon", NonFullscreenRequired: 48, FullscreenRequired: 4),
        new("Evening", 20, "time:evening", NonFullscreenRequired: 30, FullscreenRequired: 5),
        new("LateNight", 2, "time:late_night", NonFullscreenRequired: 10, FullscreenRequired: 5)
    ];

    private static readonly SafeFeedbackCoverageDate[] CoverageDates =
    [
        new(new DateTime(2200, 3, 3), "spring", IsWeekend: false, IsHoliday: false),
        new(new DateTime(2201, 3, 7), "spring", IsWeekend: true, IsHoliday: false),
        new(new DateTime(2026, 3, 3), "spring", IsWeekend: false, IsHoliday: true),
        new(new DateTime(2200, 3, 8), "spring", IsWeekend: true, IsHoliday: true),
        new(new DateTime(2200, 6, 2), "summer", IsWeekend: false, IsHoliday: false),
        new(new DateTime(2200, 6, 7), "summer", IsWeekend: true, IsHoliday: false),
        new(new DateTime(2026, 6, 19), "summer", IsWeekend: false, IsHoliday: true),
        new(new DateTime(2027, 8, 8), "summer", IsWeekend: true, IsHoliday: true),
        new(new DateTime(2200, 9, 2), "autumn", IsWeekend: false, IsHoliday: false),
        new(new DateTime(2200, 9, 6), "autumn", IsWeekend: true, IsHoliday: false),
        new(new DateTime(2200, 9, 10), "autumn", IsWeekend: false, IsHoliday: true),
        new(new DateTime(2201, 10, 24), "autumn", IsWeekend: true, IsHoliday: true),
        new(new DateTime(2200, 12, 22), "winter", IsWeekend: false, IsHoliday: false),
        new(new DateTime(2200, 12, 20), "winter", IsWeekend: true, IsHoliday: false),
        new(new DateTime(2026, 2, 17), "winter", IsWeekend: false, IsHoliday: true),
        new(new DateTime(2201, 2, 14), "winter", IsWeekend: true, IsHoliday: true)
    ];

    internal static IReadOnlyList<DateTime> SafeFeedbackCoverageDates { get; } =
        Array.AsReadOnly(CoverageDates.Select(coverage => coverage.Date).ToArray());

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
        var previousText = history.LastEntry?.Variant;
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
        var coverage = SafeFeedbackCoverageIndex.Create(scenes);
        bool?[] observations = [null, false, true];
        var directTriggers = Enum.GetValues<CompanionEvent>()
            .Where(DialogueEventPolicy.IsDirectFeedback)
            .ToArray();

        foreach (var coverageDate in ValidatedCoverageDates())
        {
            foreach (var observed in observations)
            {
                var bands = new List<SafeCapacityBand>(CoveragePeriods.Length);
                foreach (var period in CoveragePeriods)
                {
                    var now = coverageDate.Date.AddHours(period.Hour);
                    var context = RuntimeContext(CompanionEvent.Automatic, now, observed);
                    RequireCoveragePeriodToken(context, period);
                    RequireTwoSafeLines(coverage, context);
                    foreach (var trigger in directTriggers)
                    {
                        RequireTwoSafeLines(coverage, RuntimeContext(trigger, now, observed));
                    }

                    bands.Add(new SafeCapacityBand(
                        period.Name,
                        observed is true ? period.FullscreenRequired : period.NonFullscreenRequired,
                        coverage.CandidatesFor(context)));
                }

                foreach (var band in bands)
                {
                    RequireCapacity(
                        band.Candidates,
                        band.Required,
                        $"{band.Name} on {coverageDate.Date:yyyy-MM-dd} "
                        + $"with fullscreen={FormatObserved(observed)}");
                }

                RequireSharedDailyCapacity(
                    bands,
                    $"{coverageDate.Date:yyyy-MM-dd} with fullscreen={FormatObserved(observed)}");
            }
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
            .Where(scene => HasSafeLine(
                scene,
                context.Now,
                history,
                previousText,
                retainCooldownsAndAdjacency))
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

    private static bool HasSafeLine(
        SceneDefinition scene,
        DateTime now,
        SceneHistory history,
        string? previousText,
        bool retainLineCooldown) =>
        scene.Lines.Any(line =>
            IsSafeFeedbackLine(scene, line)
            && line.Text != previousText
            && history.IsBelowDailyMaximum(line, now)
            && (!retainLineCooldown || !history.IsLineCoolingDown(line, now)));

    private static DialogueLine ChooseUnusedOrLeastRecent(
        IReadOnlyList<DialogueLine> lines,
        SceneHistory history,
        Random random)
    {
        var unused = lines
            .Where(line => !history.TryGetLastPlayedAt(line.Id, out _))
            .ToArray();
        if (unused.Length > 0)
        {
            return WeightedLineChoice(history.PreferSurfaceExposure(unused), random);
        }

        var oldest = lines.Min(line =>
            history.TryGetLastPlayedAt(line.Id, out var playedAt)
                ? playedAt
                : DateTime.MinValue);
        var leastRecent = lines
            .Where(line =>
                history.TryGetLastPlayedAt(line.Id, out var playedAt)
                && playedAt == oldest)
            .ToArray();
        return WeightedLineChoice(history.PreferSurfaceExposure(leastRecent), random);
    }

    private static void RequireTwoSafeLines(
        SafeFeedbackCoverageIndex coverage,
        SceneContext context)
    {
        var safe = coverage.CandidatesFor(context).DistinctTextCountAtMostTwo();
        if (safe < 2)
        {
            throw new InvalidDataException(
                $"Safe-feedback coverage requires at least two distinct lines for "
                + $"{context.Trigger} at {context.Now:yyyy-MM-dd HH:mm} "
                + $"with fullscreen={FormatObserved(context.IsFullscreen)}; found {safe}.");
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

    private static IReadOnlyList<SafeFeedbackCoverageDate> ValidatedCoverageDates()
    {
        foreach (var coverage in CoverageDates)
        {
            var now = coverage.Date.AddHours(12);
            var context = RuntimeContext(CompanionEvent.Automatic, now, observed: null);
            var tokens = ContextTokens(context);
            var actualWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var actualHoliday = TemporalDialogueService.GetFestivals(now).Count > 0;
            if (actualWeekend != coverage.IsWeekend
                || actualHoliday != coverage.IsHoliday
                || !tokens.Contains($"season:{coverage.Season}")
                || !tokens.Contains(coverage.IsWeekend ? "day:weekend" : "day:weekday")
                || coverage.IsHoliday != tokens.Contains("holiday")
                || coverage.IsHoliday != tokens.Contains("date:holiday")
                || tokens.Contains("date:month_boundary"))
            {
                throw new InvalidOperationException(
                    $"Safe-feedback coverage date {coverage.Date:yyyy-MM-dd} no longer matches its "
                    + $"expected season/day/holiday matrix.");
            }
        }

        return CoverageDates;
    }

    private static void RequireCoveragePeriodToken(
        SceneContext context,
        SafeFeedbackCoveragePeriod period)
    {
        if (!ContextTokens(context).Contains(period.ContextToken))
        {
            throw new InvalidOperationException(
                $"Safe-feedback coverage hour {period.Hour:00}:00 must produce {period.ContextToken}.");
        }
    }

    private static void RequireCapacity(
        SafeFeedbackCoverageCandidates candidates,
        int required,
        string scenario)
    {
        var capacity = candidates.DistinctLineCapacity();
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
            var capacity = SafeFeedbackCoverageCandidates.DistinctLineCapacity(
                selected.Select(band => band.Candidates));
            if (capacity < required)
            {
                var bandNames = string.Join(" + ", selected.Select(band => band.Name));
                throw new InvalidDataException(
                    $"Safe-feedback shared daily capacity for {scenario} across {bandNames} "
                    + $"must be at least {required}; found {capacity}.");
            }
        }
    }

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
        SafeFeedbackCoverageCandidates Candidates);

    private sealed record SafeFeedbackCoveragePeriod(
        string Name,
        int Hour,
        string ContextToken,
        int NonFullscreenRequired,
        int FullscreenRequired);

    private sealed record SafeFeedbackCoverageDate(
        DateTime Date,
        string Season,
        bool IsWeekend,
        bool IsHoliday);

    private sealed class SafeFeedbackCoverageIndex
    {
        private readonly IReadOnlyList<SafeFeedbackCoverageProfile> _profiles;
        private readonly bool _lineIdsAreUnique;

        private SafeFeedbackCoverageIndex(
            IReadOnlyList<SafeFeedbackCoverageProfile> profiles,
            bool lineIdsAreUnique)
        {
            _profiles = profiles;
            _lineIdsAreUnique = lineIdsAreUnique;
        }

        public static SafeFeedbackCoverageIndex Create(IReadOnlyList<SceneDefinition> scenes)
        {
            var profiles = new Dictionary<SafeFeedbackCoverageProfileKey, SafeFeedbackCoverageProfileBuilder>();
            var seenLineIds = new HashSet<string>(StringComparer.Ordinal);
            var lineIdsAreUnique = true;

            foreach (var scene in scenes)
            {
                if (!IsSafeFeedbackScene(scene))
                {
                    continue;
                }

                var lines = scene.Lines
                    .Where(line => IsSafeFeedbackLine(scene, line) && line.MaxPerDay > 0)
                    .ToArray();
                if (lines.Length == 0)
                {
                    continue;
                }

                var key = SafeFeedbackCoverageProfileKey.Create(scene);
                if (!profiles.TryGetValue(key, out var profile))
                {
                    profile = new SafeFeedbackCoverageProfileBuilder(scene);
                    profiles.Add(key, profile);
                }

                foreach (var line in lines)
                {
                    lineIdsAreUnique &= seenLineIds.Add(line.Id);
                    profile.Add(line);
                }
            }

            return new SafeFeedbackCoverageIndex(
                profiles.Values.Select(profile => profile.Build()).ToArray(),
                lineIdsAreUnique);
        }

        public SafeFeedbackCoverageCandidates CandidatesFor(SceneContext context)
        {
            var history = CoverageHistory(context);
            var contextTokens = ContextTokens(context);
            var matching = _profiles
                .Where(profile => TriggerAndContextMatch(
                    profile.MatchScene,
                    context,
                    contextTokens,
                    history))
                .ToArray();
            return new SafeFeedbackCoverageCandidates(matching, _lineIdsAreUnique);
        }
    }

    private sealed class SafeFeedbackCoverageCandidates
    {
        private readonly IReadOnlyList<SafeFeedbackCoverageProfile> _profiles;
        private readonly bool _lineIdsAreUnique;

        public SafeFeedbackCoverageCandidates(
            IReadOnlyList<SafeFeedbackCoverageProfile> profiles,
            bool lineIdsAreUnique)
        {
            _profiles = profiles;
            _lineIdsAreUnique = lineIdsAreUnique;
        }

        public int DistinctTextCountAtMostTwo()
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in _profiles)
            {
                foreach (var text in profile.Texts)
                {
                    distinct.Add(text);
                    if (distinct.Count == 2)
                    {
                        return 2;
                    }
                }
            }

            return distinct.Count;
        }

        public int DistinctLineCapacity() =>
            DistinctLineCapacity([this]);

        public static int DistinctLineCapacity(
            IEnumerable<SafeFeedbackCoverageCandidates> candidates)
        {
            var profiles = candidates
                .SelectMany(candidate => candidate._profiles)
                .Distinct()
                .ToArray();
            var lineIdsAreUnique = candidates.All(candidate => candidate._lineIdsAreUnique);
            if (lineIdsAreUnique)
            {
                return profiles.Sum(profile => profile.Capacity);
            }

            var capacities = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                foreach (var (lineId, maxPerDay) in profile.LineCapacities)
                {
                    capacities[lineId] = Math.Max(
                        capacities.GetValueOrDefault(lineId),
                        maxPerDay);
                }
            }

            return capacities.Values.Sum();
        }
    }

    private sealed class SafeFeedbackCoverageProfileBuilder
    {
        private readonly SceneDefinition _matchScene;
        private readonly HashSet<string> _texts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lineCapacities = new(StringComparer.Ordinal);

        public SafeFeedbackCoverageProfileBuilder(SceneDefinition matchScene) =>
            _matchScene = matchScene;

        public void Add(DialogueLine line)
        {
            _texts.Add(line.Text);
            _lineCapacities[line.Id] = Math.Max(
                _lineCapacities.GetValueOrDefault(line.Id),
                line.MaxPerDay);
        }

        public SafeFeedbackCoverageProfile Build() => new(
            _matchScene,
            _texts.ToArray(),
            new Dictionary<string, int>(_lineCapacities, StringComparer.Ordinal));
    }

    private sealed class SafeFeedbackCoverageProfile
    {
        public SafeFeedbackCoverageProfile(
            SceneDefinition matchScene,
            IReadOnlyList<string> texts,
            IReadOnlyDictionary<string, int> lineCapacities)
        {
            MatchScene = matchScene;
            Texts = texts;
            LineCapacities = lineCapacities;
            Capacity = lineCapacities.Values.Sum();
        }

        public SceneDefinition MatchScene { get; }

        public IReadOnlyList<string> Texts { get; }

        public IReadOnlyDictionary<string, int> LineCapacities { get; }

        public int Capacity { get; }
    }

    private sealed record SafeFeedbackCoverageProfileKey(
        DialogueTrigger DialogueTrigger,
        string RequiredContext,
        string EventTriggers)
    {
        public static SafeFeedbackCoverageProfileKey Create(SceneDefinition scene) => new(
            scene.DialogueTrigger,
            string.Join("\u001f", scene.RequiredContext.OrderBy(token => token, StringComparer.Ordinal)),
            string.Join(",", scene.Triggers.OrderBy(trigger => trigger).Select(trigger => (int)trigger)));
    }
}
