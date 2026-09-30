using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SpeechMouthTimelineTests
{
    [Fact]
    public void Silence_StaysClosed()
    {
        var levels = SpeechMouthTimeline.FromSamples(new float[24_000], 24_000);

        Assert.NotEmpty(levels);
        Assert.All(levels, level => Assert.Equal(0, level));
    }

    [Fact]
    public void LoudTone_OpensAfterTheAttack()
    {
        var levels = SpeechMouthTimeline.FromSamples(Tone(0.25f, 24_000, 24_000), 24_000);

        Assert.Contains(levels.Skip(4), level => level == 2);
        Assert.DoesNotContain(levels.Skip(4), level => level == 0);
    }

    [Fact]
    public void QuietTone_StaysOnTheMiddleShape()
    {
        var levels = SpeechMouthTimeline.FromSamples(Tone(0.04f, 24_000, 24_000), 24_000);

        Assert.Contains(levels.Skip(4), level => level == 1);
        Assert.DoesNotContain(levels.Skip(4), level => level == 2);
    }

    [Fact]
    public void LevelAt_ClosesOutsideTheTimeline()
    {
        var levels = new byte[] { 0, 1, 2 };

        Assert.Equal(1, SpeechMouthTimeline.LevelAt(levels, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(0, SpeechMouthTimeline.LevelAt(levels, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, SpeechMouthTimeline.LevelAt(levels, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void FromWav_ReadsSixteenBitPcm()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WritePcm16(path, Tone(0.25f, 16_000, 16_000), 16_000);
            var levels = SpeechMouthTimeline.FromWav(path);

            Assert.Contains(levels.Skip(4), level => level == 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static float[] Tone(float amplitude, int sampleRate, int count)
    {
        var samples = new float[count];
        for (var index = 0; index < count; index++)
        {
            samples[index] = amplitude * MathF.Sin(index * 2 * MathF.PI * 220f / sampleRate);
        }

        return samples;
    }

    private static void WritePcm16(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1, 1);
            writer.Write((short)Math.Round(clamped * 32767));
        }
    }
}
