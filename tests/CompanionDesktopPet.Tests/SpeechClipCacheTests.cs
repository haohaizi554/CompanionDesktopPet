using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SpeechClipCacheTests
{
    [Fact]
    public void SameLineAndVoiceSettingsShareOneKey()
    {
        var first = SpeechClipCache.Key("你先坐下。", "r01", 1, 1, 1.35, 15, 1);
        var second = SpeechClipCache.Key("你先坐下。", "r01", 1, 1, 1.35, 15, 1);

        Assert.Equal(first, second);
        Assert.NotEqual(first, SpeechClipCache.Key("你先坐下。", "r01", 1.2, 1, 1.35, 15, 1));
        Assert.NotEqual(first, SpeechClipCache.Key("你先坐下。", "r02", 1, 1, 1.35, 15, 1));
    }

    [Fact]
    public void StoredClipIsFoundAndTheOldestClipIsDropped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pet-voice-cache-" + Guid.NewGuid().ToString("N"));
        var cacheDirectory = Path.Combine(directory, "cache");
        Directory.CreateDirectory(directory);
        try
        {
            var cache = new SpeechClipCache(cacheDirectory);
            var source = Path.Combine(directory, "source.wav");
            File.WriteAllBytes(source, new byte[80]);

            for (var index = 0; index < SpeechClipCache.Capacity + 1; index++)
            {
                cache.Store(index.ToString("x2"), source);
            }

            Assert.Null(cache.Find("00"));
            Assert.NotNull(cache.Find("01"));
            Assert.Equal(SpeechClipCache.Capacity, Directory.GetFiles(cacheDirectory, "*.wav").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
