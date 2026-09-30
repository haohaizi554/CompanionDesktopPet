using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CompanionDesktopPet.Services;

internal sealed class SpeechClipCache
{
    internal const int Capacity = 48;

    private readonly string _directory;
    private readonly object _gate = new();

    internal SpeechClipCache(string directory)
    {
        _directory = directory;
    }

    internal static string Key(
        string text,
        string referenceId,
        double speed,
        double temperature,
        double repetition,
        int topK,
        double topP)
    {
        var raw = string.Join(
            '\n',
            text,
            referenceId,
            speed.ToString("0.###", CultureInfo.InvariantCulture),
            temperature.ToString("0.###", CultureInfo.InvariantCulture),
            repetition.ToString("0.###", CultureInfo.InvariantCulture),
            topK.ToString(CultureInfo.InvariantCulture),
            topP.ToString("0.###", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    internal string? Find(string key)
    {
        var path = Path.Combine(_directory, key + ".wav");
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                if (new FileInfo(path).Length <= 44)
                {
                    File.Delete(path);
                    return null;
                }

                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return path;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    internal void Store(string key, string sourcePath)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var destination = Path.Combine(_directory, key + ".wav");
                File.Copy(sourcePath, destination, overwrite: true);
                File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
                Trim();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Trim()
    {
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.wav")
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < files.Length - Capacity; index++)
        {
            try
            {
                files[index].Delete();
            }
            catch (IOException)
            {
            }
        }
    }
}
