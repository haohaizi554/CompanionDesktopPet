using System.IO;

namespace CompanionDesktopPet.Services;

internal static class SpeechMouthTimeline
{
    internal const int FramesPerSecond = 24;

    private const double SilenceRms = 0.012;
    private const double OpenRms = 0.12;
    private const double ClosedLevel = 0.18;
    private const double MidLevel = 0.42;
    private const double Follow = 0.45;

    internal static byte[] FromWav(string path)
    {
        try
        {
            var (samples, sampleRate) = ReadMono(path);
            return FromSamples(samples, sampleRate);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            return [];
        }
    }

    internal static byte LevelAt(ReadOnlySpan<byte> levels, TimeSpan position)
    {
        if (levels.Length == 0 || position < TimeSpan.Zero)
        {
            return 0;
        }

        var index = (int)(position.TotalSeconds * FramesPerSecond);
        if ((uint)index >= (uint)levels.Length)
        {
            return 0;
        }

        return levels[index];
    }

    internal static byte[] FromSamples(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.Length == 0 || sampleRate <= 0)
        {
            return [];
        }

        var hop = Math.Max(1, sampleRate / FramesPerSecond);
        var frameCount = Math.Max(1, (samples.Length + hop - 1) / hop);
        var levels = new byte[frameCount];
        double opening = 0;
        byte current = 0;
        var held = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var start = frame * hop;
            var end = Math.Min(samples.Length, start + hop);
            double sum = 0;
            for (var index = start; index < end; index++)
            {
                var sample = samples[index];
                sum += sample * sample;
            }

            var rms = Math.Sqrt(sum / Math.Max(1, end - start));
            var normalized = rms <= SilenceRms
                ? 0
                : Math.Clamp((rms - SilenceRms) / (OpenRms - SilenceRms), 0, 1);
            var target = Math.Sqrt(normalized);
            opening += (target - opening) * Follow;
            var next = opening < ClosedLevel ? (byte)0 : opening < MidLevel ? (byte)1 : (byte)2;
            if (frame == 0)
            {
                current = next;
                held = 1;
            }
            else if (next == current)
            {
                held++;
            }
            else if (next > current || held >= 2)
            {
                current = next;
                held = 1;
            }
            else
            {
                held++;
            }

            levels[frame] = current;
        }

        return levels;
    }

    private static (float[] Samples, int SampleRate) ReadMono(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 44
            || new string(reader.ReadChars(4)) != "RIFF"
            || reader.ReadInt32() < 0
            || new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Not a wav file.");
        }

        short channels = 1;
        var sampleRate = 0;
        short bits = 16;
        short format = 1;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (size < 0 || stream.Position + size > stream.Length)
            {
                throw new InvalidDataException("Wav chunk is truncated.");
            }

            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                var unread = size - 16;
                if (unread > 0)
                {
                    reader.ReadBytes(unread);
                }
            }
            else if (id == "data")
            {
                data = reader.ReadBytes(size);
            }
            else
            {
                reader.ReadBytes(size);
            }

            if ((size & 1) == 1 && stream.Position < stream.Length)
            {
                reader.ReadByte();
            }
        }

        if (data is null || sampleRate <= 0 || channels <= 0)
        {
            throw new InvalidDataException("Wav has no samples.");
        }

        var bytesPerSample = bits / 8;
        if (bytesPerSample <= 0 || data.Length < channels * bytesPerSample)
        {
            throw new InvalidDataException("Wav sample format is empty.");
        }

        var frameCount = data.Length / (channels * bytesPerSample);
        var samples = new float[frameCount];
        var offset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            float sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += ReadSample(data, offset, format, bits);
                offset += bytesPerSample;
            }

            samples[frame] = sum / channels;
        }

        return (samples, sampleRate);
    }

    private static float ReadSample(byte[] data, int offset, short format, short bits)
    {
        if (format == 3 && bits == 32)
        {
            return BitConverter.ToSingle(data, offset);
        }

        if (format == 1 && bits == 16)
        {
            return BitConverter.ToInt16(data, offset) / 32768f;
        }

        throw new InvalidDataException("Only 16-bit PCM and 32-bit float wav files can drive the mouth.");
    }
}
