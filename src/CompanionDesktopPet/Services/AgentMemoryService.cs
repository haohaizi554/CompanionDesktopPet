using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanionDesktopPet.Services;

public sealed record AgentMemorySnapshot(
    CharacterState State,
    IReadOnlyList<SceneHistoryEntry> History,
    int TurnCount,
    DialogueCategory? LastCategory,
    IReadOnlyList<string> RecentLines);

public sealed class AgentMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
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
            return await JsonSerializer.DeserializeAsync<AgentMemorySnapshot>(stream, JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AgentMemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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
}
