using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal readonly record struct LineSelectionGroup(
    string Family,
    string Category,
    double Target,
    ulong Fingerprint);

internal sealed class LineSelectionModel
{
    internal const double FamilyScale = 24;
    internal const double NearDuplicatePenalty = 18;
    internal const int NearDuplicateHamming = 10;
    internal const int NearDuplicateWindow = 8;
    internal const string SnapshotFileName = "line-selection-model.json";

    private static readonly Lazy<LineSelectionModel> SharedModel = new(
        () => Train(PersonaCorpus.All),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, LineSelectionGroup> _groups;

    private LineSelectionModel(
        int lineCount,
        IReadOnlyDictionary<string, LineSelectionGroup> groups)
    {
        LineCount = lineCount;
        _groups = new Dictionary<string, LineSelectionGroup>(groups, StringComparer.Ordinal);
    }

    internal static LineSelectionModel Shared => SharedModel.Value;

    internal int LineCount { get; }

    internal int GroupCount => _groups.Count;

    internal bool TryGetGroup(string semanticGroup, out LineSelectionGroup group) =>
        _groups.TryGetValue(semanticGroup, out group);

    internal static LineSelectionModel Train(IReadOnlyList<DialogueLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return new LineSelectionModel(0, new Dictionary<string, LineSelectionGroup>());
        }

        var familyByTopic = new Dictionary<(DialogueCategory Category, string TopicId), string>();
        foreach (var topic in lines.GroupBy(line => (line.Category, line.TopicId)))
        {
            var topicLines = topic.ToArray();
            familyByTopic[topic.Key] = CorpusMenuTaxonomy.Assign(
                topic.Key.Category,
                topic.Key.TopicId,
                topicLines);
        }

        var familyVotes = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var termFrequency = new Dictionary<string, Dictionary<ulong, int>>(StringComparer.Ordinal);
        var documentFrequency = new Dictionary<ulong, int>();
        foreach (var line in lines)
        {
            var family = line.Category + "|" + familyByTopic[(line.Category, line.TopicId)];
            if (!familyVotes.TryGetValue(line.SemanticGroup, out var votes))
            {
                votes = new Dictionary<string, int>(StringComparer.Ordinal);
                familyVotes[line.SemanticGroup] = votes;
                termFrequency[line.SemanticGroup] = new Dictionary<ulong, int>();
            }

            votes[family] = votes.GetValueOrDefault(family) + 1;
            AddBigrams(line.Text, termFrequency[line.SemanticGroup], documentFrequency);
        }

        var familiesByCategory = new Dictionary<DialogueCategory, HashSet<string>>();
        var chosenFamily = new Dictionary<string, string>(familyVotes.Count, StringComparer.Ordinal);
        foreach (var (semanticGroup, votes) in familyVotes)
        {
            string? bestFamily = null;
            var bestCount = -1;
            foreach (var pair in votes)
            {
                if (pair.Value > bestCount
                    || (pair.Value == bestCount && string.CompareOrdinal(pair.Key, bestFamily) < 0))
                {
                    bestFamily = pair.Key;
                    bestCount = pair.Value;
                }
            }

            chosenFamily[semanticGroup] = bestFamily!;
            var category = CategoryOf(bestFamily!);
            if (!familiesByCategory.TryGetValue(category, out var families))
            {
                families = new HashSet<string>(StringComparer.Ordinal);
                familiesByCategory[category] = families;
            }

            families.Add(bestFamily!);
        }

        var groups = new Dictionary<string, LineSelectionGroup>(chosenFamily.Count, StringComparer.Ordinal);
        var groupCount = Math.Max(1, chosenFamily.Count);
        foreach (var (semanticGroup, family) in chosenFamily)
        {
            var category = CategoryOf(family);
            var target = 1.0 / familiesByCategory[category].Count;
            groups[semanticGroup] = new LineSelectionGroup(
                family,
                category.ToString(),
                target,
                Fingerprint(termFrequency[semanticGroup], documentFrequency, groupCount));
        }

        return new LineSelectionModel(lines.Count, groups);
    }

    internal double Adjustment(string semanticGroup, IReadOnlyList<SceneHistoryEntry> recent)
    {
        if (!_groups.TryGetValue(semanticGroup, out var prior))
        {
            return 0;
        }

        var categoryCount = 0;
        var familyCount = 0;
        var duplicatePenalty = 0.0;
        var windowStart = Math.Max(0, recent.Count - NearDuplicateWindow);
        for (var index = 0; index < recent.Count; index++)
        {
            var entry = recent[index];
            if (!_groups.TryGetValue(entry.SemanticGroup, out var other) || other.Category != prior.Category)
            {
                continue;
            }

            categoryCount++;
            if (other.Family == prior.Family)
            {
                familyCount++;
            }

            if (index >= windowStart
                && entry.SemanticGroup != semanticGroup
                && HammingDistance(prior.Fingerprint, other.Fingerprint) <= NearDuplicateHamming)
            {
                duplicatePenalty = NearDuplicatePenalty;
            }
        }

        var familyBonus = categoryCount == 0
            ? 0
            : (prior.Target - (familyCount / (double)categoryCount)) * FamilyScale;
        return familyBonus - duplicatePenalty;
    }

    internal void WriteSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("lineCount", LineCount);
        writer.WriteNumber("familyScale", FamilyScale);
        writer.WriteNumber("nearDuplicatePenalty", NearDuplicatePenalty);
        writer.WriteNumber("nearDuplicateHamming", NearDuplicateHamming);
        writer.WriteNumber("nearDuplicateWindow", NearDuplicateWindow);
        writer.WritePropertyName("groups");
        writer.WriteStartObject();
        foreach (var (semanticGroup, group) in _groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(semanticGroup);
            writer.WriteStartObject();
            writer.WriteString("family", group.Family);
            writer.WriteString("category", group.Category);
            writer.WriteNumber("target", group.Target);
            writer.WriteString("fingerprint", group.Fingerprint.ToString("x16"));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    internal static int HammingDistance(ulong left, ulong right) =>
        BitOperations.PopCount(left ^ right);

    private static DialogueCategory CategoryOf(string family)
    {
        var split = family.IndexOf('|');
        return Enum.Parse<DialogueCategory>(family[..split]);
    }

    private static void AddBigrams(
        string text,
        Dictionary<ulong, int> frequency,
        Dictionary<ulong, int> documentFrequency)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            return;
        }

        for (var index = 0; index < text.Length - 1; index++)
        {
            var hash = Mix(((ulong)text[index] << 16) ^ text[index + 1]);
            if (frequency.TryGetValue(hash, out var count))
            {
                frequency[hash] = count + 1;
                continue;
            }

            frequency[hash] = 1;
            documentFrequency[hash] = documentFrequency.GetValueOrDefault(hash) + 1;
        }
    }

    private static ulong Fingerprint(
        Dictionary<ulong, int> frequency,
        Dictionary<ulong, int> documentFrequency,
        int groupCount)
    {
        Span<double> votes = stackalloc double[64];
        foreach (var (hash, count) in frequency)
        {
            var weight = (Math.Log(groupCount / (double)documentFrequency[hash]) + 0.1) * count;
            for (var bit = 0; bit < 64; bit++)
            {
                votes[bit] += ((hash >> bit) & 1UL) == 1 ? weight : -weight;
            }
        }

        ulong fingerprint = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (votes[bit] > 0)
            {
                fingerprint |= 1UL << bit;
            }
        }

        return fingerprint;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }
}
