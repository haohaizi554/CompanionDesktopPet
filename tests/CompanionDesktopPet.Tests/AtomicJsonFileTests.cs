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

    [Fact]
    public async Task ConcurrentSuccessfulWritesToOneDestination_AreSerializedAndLeaveNoTemporaryFiles()
    {
        var path = Path.Combine(_directory, "state.json");
        var firstConverter = new BlockingPayloadConverter();
        var secondConverter = new SignallingPayloadConverter();
        var firstOptions = new JsonSerializerOptions();
        var secondOptions = new JsonSerializerOptions();
        firstOptions.Converters.Add(firstConverter);
        secondOptions.Converters.Add(secondConverter);

        var firstWrite = Task.Run(() => AtomicJsonFile.WriteAsync(
            path,
            new PersistedPayload("first"),
            firstOptions));
        await firstConverter.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        var secondWrite = AtomicJsonFile.WriteAsync(
            path,
            new PersistedPayload("second"),
            secondOptions);
        var reachedSecondSerializerBeforeRelease = await Task.WhenAny(
            secondConverter.Entered,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.NotSame(secondConverter.Entered, reachedSecondSerializerBeforeRelease);

        firstConverter.Release();
        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(5));
        await secondConverter.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("second", document.RootElement.GetProperty("value").GetString());
        Assert.Empty(Directory.GetFiles(_directory, "state.json.*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record ThrowingPayload;

    private sealed record PersistedPayload(string Value);

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

    private sealed class BlockingPayloadConverter : JsonConverter<PersistedPayload>
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public override PersistedPayload? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            PersistedPayload value,
            JsonSerializerOptions options)
        {
            _entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            WritePayload(writer, value);
        }
    }

    private sealed class SignallingPayloadConverter : JsonConverter<PersistedPayload>
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public override PersistedPayload? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            PersistedPayload value,
            JsonSerializerOptions options)
        {
            _entered.TrySetResult();
            WritePayload(writer, value);
        }
    }

    private static void WritePayload(Utf8JsonWriter writer, PersistedPayload value)
    {
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        writer.WriteEndObject();
    }
}
