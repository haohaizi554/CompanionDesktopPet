using System.IO;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal sealed class VoiceReference
{
    public required string Id { get; init; }
    public required string AudioPath { get; init; }
    public required string Prompt { get; init; }
    public required string PromptLang { get; init; }
    public required IReadOnlyList<string> Tones { get; init; }
    public required IReadOnlyList<string> Situations { get; init; }
    public double Seconds { get; init; }
    public bool Fallback { get; init; }
}

internal sealed class JiayiVoicePack
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public required string Id { get; init; }
    public required string TextLang { get; init; }
    public required string GptPath { get; init; }
    public required string SovitsPath { get; init; }
    public required IReadOnlyList<VoiceReference> References { get; init; }

    public VoiceReference Select(string? tone, string? text = null, string? trigger = null, string? preferId = null)
    {
        var pool = MatchTone(CanonicalTone(tone));
        if (pool.Count == 0)
        {
            pool = References.Where(reference => reference.Fallback).ToArray();
        }

        if (pool.Count == 0)
        {
            pool = References;
        }

        pool = Prefer(pool, reference => FitsLength(reference, HanCount(text)));
        if (IsNight(trigger))
        {
            pool = Prefer(pool, reference => HasSituation(reference, "night"));
        }

        if (IsQuestion(text))
        {
            pool = Prefer(pool, reference => HasSituation(reference, "question"));
        }
        else if (IsWarning(text))
        {
            pool = Prefer(pool, reference => HasSituation(reference, "warning"));
        }

        if (!string.IsNullOrEmpty(preferId))
        {
            foreach (var reference in pool)
            {
                if (string.Equals(reference.Id, preferId, StringComparison.Ordinal))
                {
                    return reference;
                }
            }
        }

        return pool[StableIndex(text, pool.Count)];
    }

    private static string? CanonicalTone(string? tone) =>
        string.Equals(tone, "dry_warm", StringComparison.OrdinalIgnoreCase) ? "gentle" : tone;

    private IReadOnlyList<VoiceReference> MatchTone(string? tone)
    {
        if (string.IsNullOrWhiteSpace(tone))
        {
            return [];
        }

        return References.Where(reference =>
            reference.Tones.Any(candidate =>
                string.Equals(candidate, tone, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static IReadOnlyList<VoiceReference> Prefer(
        IReadOnlyList<VoiceReference> pool,
        Func<VoiceReference, bool> predicate)
    {
        var matched = pool.Where(predicate).ToArray();
        return matched.Length > 0 ? matched : pool;
    }

    private static bool FitsLength(VoiceReference reference, int hanCount)
    {
        if (reference.Seconds <= 0)
        {
            return true;
        }

        if (hanCount <= 18)
        {
            return reference.Seconds <= 11.5;
        }

        if (hanCount >= 36)
        {
            return reference.Seconds >= 12;
        }

        return reference.Seconds < 16;
    }

    private static bool HasSituation(VoiceReference reference, string situation) =>
        reference.Situations.Any(candidate =>
            string.Equals(candidate, situation, StringComparison.OrdinalIgnoreCase));

    private static bool IsNight(string? trigger) =>
        string.Equals(trigger, "LateNight", StringComparison.OrdinalIgnoreCase)
        || string.Equals(trigger, "Evening", StringComparison.OrdinalIgnoreCase);

    private static bool IsQuestion(string? text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains('？') || text.Contains('?') || text.Contains('吗') || text.Contains('呢'));

    private static bool IsWarning(string? text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains('别') || text.Contains("不要") || text.Contains("小心") || text.Contains("注意") || text.Contains("千万"));

    private static int HanCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Count(character => character is >= '\u4e00' and <= '\u9fff');
    }

    private static int StableIndex(string? text, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        var hash = 2166136261u;
        foreach (var character in text ?? "")
        {
            hash ^= character;
            hash *= 16777619;
        }

        return (int)(hash % (uint)count);
    }

    public static JiayiVoicePack Load(string packDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packDirectory);
        var manifestPath = Path.Combine(packDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDto>(
            File.ReadAllText(manifestPath),
            JsonOptions)
            ?? throw new InvalidDataException($"Voice manifest is empty: {manifestPath}");
        if (manifest.Refs is null || manifest.Refs.Count == 0)
        {
            throw new InvalidDataException($"Voice manifest has no references: {manifestPath}");
        }

        var references = manifest.Refs.Select(reference => new VoiceReference
        {
            Id = Required(reference.Id, "ref id"),
            AudioPath = Path.GetFullPath(Path.Combine(packDirectory, Required(reference.Audio, "ref audio"))),
            Prompt = Required(reference.Prompt, "ref prompt"),
            PromptLang = string.IsNullOrWhiteSpace(reference.PromptLang) ? "zh" : reference.PromptLang,
            Tones = reference.Tones ?? [],
            Situations = reference.Situations ?? [],
            Seconds = reference.Seconds,
            Fallback = reference.Fallback
        }).ToArray();

        return new JiayiVoicePack
        {
            Id = Required(manifest.Id, "id"),
            TextLang = string.IsNullOrWhiteSpace(manifest.TextLang) ? "zh" : manifest.TextLang,
            GptPath = Path.GetFullPath(Path.Combine(packDirectory, Required(manifest.Gpt, "gpt"))),
            SovitsPath = Path.GetFullPath(Path.Combine(packDirectory, Required(manifest.Sovits, "sovits"))),
            References = references
        };
    }

    public string? MissingAsset()
    {
        if (!File.Exists(GptPath))
        {
            return GptPath;
        }

        if (!File.Exists(SovitsPath))
        {
            return SovitsPath;
        }

        return References
            .Select(reference => reference.AudioPath)
            .FirstOrDefault(path => !File.Exists(path));
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Voice manifest is missing {name}.")
            : value;

    private sealed class ManifestDto
    {
        public string? Id { get; set; }
        public string? TextLang { get; set; }
        public string? Gpt { get; set; }
        public string? Sovits { get; set; }
        public List<RefDto>? Refs { get; set; }
    }

    private sealed class RefDto
    {
        public string? Id { get; set; }
        public List<string>? Tones { get; set; }
        public List<string>? Situations { get; set; }
        public double Seconds { get; set; }
        public string? Audio { get; set; }
        public string? Prompt { get; set; }
        public string? PromptLang { get; set; }
        public bool Fallback { get; set; }
    }
}

internal sealed class VoiceRuntime
{
    public required string PythonPath { get; init; }
    public required string RootPath { get; init; }
    public required string BertPath { get; init; }
    public required string HubertPath { get; init; }
    public required string VoiceRoot { get; init; }

    public static VoiceRuntime? TryLoad(string startDirectory)
    {
        var voiceRoot = FindVoiceRoot(startDirectory);
        if (voiceRoot is null)
        {
            return null;
        }

        var python = Path.Combine(voiceRoot, "voice", "python", "python.exe");
        var engine = Path.Combine(voiceRoot, "voice", "engine");
        var bert = Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "chinese-roberta-wwm-ext-large");
        var hubert = Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "chinese-hubert-base");
        var speaker = Path.Combine(engine, "GPT_SoVITS", "pretrained_models", "sv", "pretrained_eres2netv2w24s4ep4.ckpt");
        if (!File.Exists(python)
            || !Directory.Exists(bert)
            || !Directory.Exists(hubert)
            || !File.Exists(speaker))
        {
            return null;
        }

        return new VoiceRuntime
        {
            PythonPath = python,
            RootPath = engine,
            BertPath = bert,
            HubertPath = hubert,
            VoiceRoot = voiceRoot
        };
    }

    internal static string? FindVoiceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++)
        {
            var python = Path.Combine(directory.FullName, "voice", "python", "python.exe");
            var engine = Path.Combine(directory.FullName, "voice", "engine");
            if (File.Exists(python) && Directory.Exists(engine))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
