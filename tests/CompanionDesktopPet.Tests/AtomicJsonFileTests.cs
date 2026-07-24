using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CleanupFailure_ReleasesDestinationGateAndPreservesPrimaryFailure()
    {
        var path = Path.Combine(_directory, "state.json");
        var failingOptions = new JsonSerializerOptions();
        failingOptions.Converters.Add(new ThrowingPayloadConverter());
        var cleanupCalls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicJsonFile.WriteAsync(
                path,
                new ThrowingPayload(),
                failingOptions,
                CancellationToken.None,
                File.Exists,
                temporaryPath =>
                {
                    Interlocked.Increment(ref cleanupCalls);
                    throw new IOException($"cleanup failed for {temporaryPath}");
                }));

        Assert.Equal("serialization failed", exception.Message);
        Assert.Equal(1, cleanupCalls);
        await AtomicJsonFile.WriteAsync(path, new { Value = 42 }, new JsonSerializerOptions())
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(42, JsonDocument.Parse(await File.ReadAllTextAsync(path))
            .RootElement.GetProperty("Value").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record ThrowingPayload;

    private sealed class ThrowingPayloadConverter : JsonConverter<ThrowingPayload>
    {
        public override ThrowingPayload? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            ThrowingPayload value,
            JsonSerializerOptions options) =>
            throw new InvalidOperationException("serialization failed");
    }
}
