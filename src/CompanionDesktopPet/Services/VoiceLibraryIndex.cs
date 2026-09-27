using System.Globalization;
using System.IO;

namespace CompanionDesktopPet.Services;

internal sealed class VoiceLibraryClip
{
    public required string Id { get; init; }

    public required string Prompt { get; init; }

    public required string AudioPath { get; init; }

    public required string ToneLabel { get; init; }

    public required string SituationLabel { get; init; }

    public required IReadOnlyList<string> ToneKeys { get; init; }

    public double Seconds { get; init; }

    public bool Fallback { get; init; }

    public string Meta
    {
        get
        {
            var seconds = Seconds.ToString("0.#", CultureInfo.InvariantCulture);
            var spare = Fallback ? "  ·  备用" : string.Empty;
            return $"{ToneLabel}  ·  {SituationLabel}  ·  {seconds} 秒{spare}";
        }
    }
}

internal sealed class VoiceLibraryFolder
{
    public VoiceLibraryFolder(string title, VoiceLibraryClip[] clips, VoiceLibraryFolder[] children)
    {
        Title = title;
        Clips = clips;
        Children = children;
    }

    public string Title { get; }

    public VoiceLibraryClip[] Clips { get; }

    public VoiceLibraryFolder[] Children { get; }

    public string Header => $"{Title}  {Clips.Length}";
}

internal static class VoiceLibraryIndex
{
    private static readonly string[] ToneOrder =
    [
        "calm",
        "gentle",
        "playful",
        "curious",
        "dry",
        "dry_sharp",
        "serious",
        "encouraging",
        "intimate",
        "nostalgic",
        "sleepy"
    ];

    public static VoiceLibraryFolder? TryLoad(string startDirectory)
    {
        var root = VoiceRuntime.FindVoiceRoot(startDirectory);
        if (root is null)
        {
            return null;
        }

        var packDirectory = Path.Combine(root, "voice", "packs", "jiayi");
        return File.Exists(Path.Combine(packDirectory, "manifest.json"))
            ? Load(packDirectory)
            : null;
    }

    public static VoiceLibraryFolder Load(string packDirectory)
    {
        var pack = JiayiVoicePack.Load(packDirectory);
        var clips = pack.References.Select(ToClip).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var children = new List<VoiceLibraryFolder>();
        foreach (var tone in ToneOrder.Concat(clips.SelectMany(clip => clip.ToneKeys)))
        {
            if (!seen.Add(tone))
            {
                continue;
            }

            var group = clips
                .Where(clip => clip.ToneKeys.Any(key => string.Equals(key, tone, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (group.Length == 0)
            {
                continue;
            }

            children.Add(new VoiceLibraryFolder(ToneLabel(tone), group, []));
        }

        return new VoiceLibraryFolder("全部", clips, children.ToArray());
    }

    public static string ToneLabel(string? tone) => tone?.ToLowerInvariant() switch
    {
        "calm" => "平静",
        "gentle" => "温柔",
        "playful" => "俏皮",
        "curious" => "好奇",
        "dry" => "平淡",
        "dry_sharp" => "利落",
        "serious" => "认真",
        "encouraging" => "打气",
        "intimate" => "亲近",
        "nostalgic" => "怀念",
        "sleepy" => "犯困",
        null or "" => "未标语气",
        _ => tone
    };

    public static string SituationLabel(string? situation) => situation?.ToLowerInvariant() switch
    {
        "explain" => "说明",
        "question" => "问句",
        "comfort" => "安慰",
        "warning" => "提醒",
        "tease" => "逗你",
        "encourage" => "加油",
        "night" => "夜里",
        null or "" => "未标场合",
        _ => situation
    };

    private static VoiceLibraryClip ToClip(VoiceReference reference)
    {
        var tones = reference.Tones.Where(tone => !string.IsNullOrWhiteSpace(tone)).ToArray();
        var situations = reference.Situations.Where(situation => !string.IsNullOrWhiteSpace(situation)).ToArray();
        return new VoiceLibraryClip
        {
            Id = reference.Id,
            Prompt = reference.Prompt,
            AudioPath = reference.AudioPath,
            ToneKeys = tones,
            ToneLabel = tones.Length == 0
                ? ToneLabel(null)
                : string.Join("、", tones.Select(ToneLabel)),
            SituationLabel = situations.Length == 0
                ? SituationLabel(null)
                : string.Join("、", situations.Select(SituationLabel)),
            Seconds = reference.Seconds,
            Fallback = reference.Fallback
        };
    }
}
