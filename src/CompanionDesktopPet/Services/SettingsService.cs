using System.Diagnostics;
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
    private readonly Action<Exception> _reportLoadFailure;

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsService(string? directory = null)
        : this(directory, ReportLoadFailure)
    {
    }

    internal SettingsService(
        string? directory,
        Action<Exception> reportLoadFailure)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet");
        _reportLoadFailure = reportLoadFailure
            ?? throw new ArgumentNullException(nameof(reportLoadFailure));
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
            if (PetSettings.IsValid(settings))
            {
                return settings!;
            }

            TryReportLoadFailure(new InvalidDataException(
                "The settings document contains values outside the supported contract."));
            return PetSettings.Default;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            TryReportLoadFailure(exception);
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

        await AtomicJsonFile.WriteAsync(SettingsPath, settings, JsonOptions);
    }

    private void TryReportLoadFailure(Exception exception)
    {
        try
        {
            _reportLoadFailure(exception);
        }
        catch (Exception reportingFailure) when (!IsFatalException(reportingFailure))
        {
            // Preference fallback must remain available even when diagnostics are unavailable.
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    private static void ReportLoadFailure(Exception exception) =>
        Trace.TraceError("Settings load failed; using defaults: {0}", exception);
}
