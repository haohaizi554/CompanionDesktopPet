using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanionDesktopPet.Services;

public sealed record AgentMemorySnapshot(
    [property: JsonRequired] CharacterState State,
    [property: JsonRequired] IReadOnlyList<SceneHistoryEntry> History,
    [property: JsonRequired] int TurnCount,
    [property: JsonRequired] DialogueCategory? LastCategory,
    [property: JsonRequired] IReadOnlyList<string> RecentLines);

public sealed class AgentMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false)
        }
    };

    private readonly string _directory;

    private string MemoryPath => Path.Combine(_directory, "agent-memory.json");

    public AgentMemoryService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet");
    }

    public async Task<AgentMemorySnapshot?> LoadAsync()
    {
        try
        {
            if (!File.Exists(MemoryPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(MemoryPath);
            var snapshot = await JsonSerializer.DeserializeAsync<AgentMemorySnapshot>(
                stream,
                JsonOptions);
            return IsValid(snapshot) ? snapshot : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AgentMemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsValid(snapshot))
        {
            throw new ArgumentException(
                "Only a complete, compatible agent memory snapshot can be saved.",
                nameof(snapshot));
        }

        Directory.CreateDirectory(_directory);
        var temporaryPath = MemoryPath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, MemoryPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool IsValid(AgentMemorySnapshot? snapshot)
    {
        if (snapshot?.State is null
            || snapshot.History is null
            || snapshot.RecentLines is null
            || snapshot.TurnCount < 0
            || snapshot.History.Count > 2_000
            || snapshot.RecentLines.Count > OfflineCompanionAgent.RecentMemoryLimit
            || snapshot.TurnCount < snapshot.History.Count
            || (snapshot.History.Count == 0 && snapshot.RecentLines.Count != 0)
            || (snapshot.LastCategory is { } category && !Enum.IsDefined(category)))
        {
            return false;
        }

        try
        {
            var scenes = SceneCatalog.All.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
            var knownLines = scenes.Values
                .SelectMany(scene => scene.Lines)
                .GroupBy(line => line.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var knownTexts = knownLines.Values
                .Select(line => line.Text)
                .ToHashSet(StringComparer.Ordinal);

            return IsValidState(snapshot.State)
                   && snapshot.History.All(entry => IsValidHistory(entry, scenes))
                   && snapshot.RecentLines.All(line =>
                       !string.IsNullOrWhiteSpace(line)
                       && !line.Contains('\t')
                       && !line.Contains('\r')
                       && !line.Contains('\n')
                       && knownTexts.Contains(line));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ArgumentException
                or TypeInitializationException)
        {
            return false;
        }
    }

    private static bool IsValidState(CharacterState state)
    {
        if (!IsUnitInterval(state.Energy)
            || !IsUnitInterval(state.Sociability)
            || !IsUnitInterval(state.Boredom)
            || !Enum.IsDefined(state.Mood)
            || !Enum.IsDefined(state.Activity)
            || state.InstalledAt == default
            || state.LastUpdatedAt == default
            || state.LastUpdatedAt < state.InstalledAt
            || state.AttachmentDays < 1
            || state.ActiveStories is null)
        {
            return false;
        }

        var arcs = StoryArcCatalog.All.ToDictionary(arc => arc.Id, StringComparer.Ordinal);
        if (state.ActiveStories.Count > arcs.Count)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in state.ActiveStories)
        {
            if (story is null
                || string.IsNullOrWhiteSpace(story.ArcId)
                || !seen.Add(story.ArcId)
                || !arcs.TryGetValue(story.ArcId, out var arc)
                || story.NodeIndex <= 0
                || story.NodeIndex >= arc.Nodes.Count
                || story.DueAt == default)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidHistory(
        SceneHistoryEntry? entry,
        IReadOnlyDictionary<string, SceneDefinition> scenes)
    {
        if (entry is null
            || string.IsNullOrWhiteSpace(entry.SceneId)
            || string.IsNullOrWhiteSpace(entry.SemanticGroup)
            || string.IsNullOrWhiteSpace(entry.Variant)
            || string.IsNullOrWhiteSpace(entry.DialogueLineId)
            || entry.PlayedAt == default
            || entry.PlayedLocalDate is null
            || entry.PlayedLocalDate != DateOnly.FromDateTime(entry.PlayedAt)
            || !Enum.IsDefined(entry.Category)
            || !Enum.IsDefined(entry.CategoryGroup)
            || !Enum.IsDefined(entry.OutputMode)
            || !Enum.IsDefined(entry.DialogueTrigger)
            || entry.InterruptionCost is < 0 or > 5
            || !scenes.TryGetValue(entry.SceneId, out var scene))
        {
            return false;
        }

        var line = scene.Lines.FirstOrDefault(
            candidate => candidate.Id == entry.DialogueLineId);
        return line is not null
               && entry.SemanticGroup == scene.SemanticGroup
               && entry.SemanticGroup == line.SemanticGroup
               && entry.Variant == line.Text
               && entry.Category == line.Category
               && entry.CategoryGroup == line.CategoryGroup
               && entry.OutputMode == line.OutputMode
               && entry.DialogueTrigger == line.Trigger
               && entry.InterruptionCost == line.InterruptionCost;
    }

    private static bool IsUnitInterval(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;
}
