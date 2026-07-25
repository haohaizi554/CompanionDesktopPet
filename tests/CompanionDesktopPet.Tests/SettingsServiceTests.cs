using System.IO;
using System.Text.Json;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private const string CompleteJson =
        """{"Left":120,"Top":240,"Scale":"Large","AnimationPaused":true,"AlwaysOnTop":false}""";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    public static TheoryData<string> IncompatibleJson => new()
    {
        """{}""",
        """{"Left":120,"Top":240,"Scale":"Large","AnimationPaused":true}""",
        """{"Left":120,"Top":240,"Scale":"Large","AnimationPaused":true,"AlwaysOnTop":false,"FutureField":1}""",
        """{"Left":120,"Top":240,"Scale":2,"AnimationPaused":true,"AlwaysOnTop":false}""",
        """{"Left":120,"Top":240,"Scale":"Huge","AnimationPaused":true,"AlwaysOnTop":false}""",
        """{"Left":1000001,"Top":240,"Scale":"Large","AnimationPaused":true,"AlwaysOnTop":false}""",
        """{"Left":"NaN","Top":240,"Scale":"Large","AnimationPaused":true,"AlwaysOnTop":false}"""
    };

    [Fact]
    public async Task SaveAndLoad_RoundTripsEveryField()
    {
        var service = new SettingsService(_directory);
        var expected = new PetSettings(-120, 240, PetScale.Large, true, false);

        await service.SaveAsync(expected);

        Assert.Equal(expected, await service.LoadAsync());
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.tmp")));
    }

    [Fact]
    public async Task ConcurrentSaves_UseIndependentTemporaryFilesAndLeaveOneCompleteDocument()
    {
        var service = new SettingsService(_directory);
        var candidates = Enumerable.Range(0, 24)
            .Select(index => new PetSettings(index, -index, PetScale.Normal, index % 2 == 0, true))
            .ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = candidates.Select(candidate => Task.Run(async () =>
        {
            await start.Task;
            await service.SaveAsync(candidate);
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(saves);

        Assert.Contains(await service.LoadAsync(), candidates);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Load_MalformedJson_ReturnsDefaults()
    {
        await WriteSettingsAsync("{broken");

        Assert.Equal(PetSettings.Default, await new SettingsService(_directory).LoadAsync());
    }

    [Fact]
    public async Task Load_MalformedJson_ReportsTheFallbackReasonOnce()
    {
        await WriteSettingsAsync("{broken");
        var failures = new List<Exception>();

        Assert.Equal(
            PetSettings.Default,
            await new SettingsService(_directory, failures.Add).LoadAsync());

        Assert.IsType<JsonException>(Assert.Single(failures));
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsDefaultsWithoutReportingFailure()
    {
        var failures = new List<Exception>();

        Assert.Equal(
            PetSettings.Default,
            await new SettingsService(_directory, failures.Add).LoadAsync());

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Load_ContractInvalidValues_ReportTheFallbackReason()
    {
        await WriteSettingsAsync(
            """{"Left":1000001,"Top":240,"Scale":"Large","AnimationPaused":true,"AlwaysOnTop":false}""");
        var failures = new List<Exception>();

        Assert.Equal(
            PetSettings.Default,
            await new SettingsService(_directory, failures.Add).LoadAsync());

        var failure = Assert.IsType<InvalidDataException>(Assert.Single(failures));
        Assert.Contains("supported contract", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_DiagnosticFailureDoesNotDisableTheDefaultFallback()
    {
        await WriteSettingsAsync("{broken");
        var service = new SettingsService(
            _directory,
            _ => throw new InvalidOperationException("diagnostics unavailable"));

        Assert.Equal(PetSettings.Default, await service.LoadAsync());
    }

    [Theory]
    [MemberData(nameof(IncompatibleJson))]
    public async Task Load_IncompleteOrIncompatibleJson_ReturnsDefaults(string json)
    {
        await WriteSettingsAsync(json);

        Assert.Equal(PetSettings.Default, await new SettingsService(_directory).LoadAsync());
    }

    [Fact]
    public async Task Load_CompleteCompatibleJson_LoadsSettings()
    {
        await WriteSettingsAsync(CompleteJson);

        Assert.Equal(
            new PetSettings(120, 240, PetScale.Large, true, false),
            await new SettingsService(_directory).LoadAsync());
    }

    private async Task WriteSettingsAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

}
