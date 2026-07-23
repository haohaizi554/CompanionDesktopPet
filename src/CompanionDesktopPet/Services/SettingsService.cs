using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false)
        }
    };

    private readonly string _directory;

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet");
    }

    public async Task<PetSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return PetSettings.Default;
            }

            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<PetSettings>(
                stream,
                JsonOptions);
            return PetSettings.IsValid(settings)
                ? settings!
                : PetSettings.Default;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return PetSettings.Default;
        }
    }

    public async Task SaveAsync(PetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!PetSettings.IsValid(settings))
        {
            throw new ArgumentException(
                "Only complete settings with finite, plausible coordinates can be saved.",
                nameof(settings));
        }

        Directory.CreateDirectory(_directory);
        var temporaryPath = SettingsPath + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, SettingsPath, true);
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
