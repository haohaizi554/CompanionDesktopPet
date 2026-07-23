using System.Globalization;
using System.IO;
using System.Text;

namespace CompanionDesktopPet.Services;

public enum DialogueCategory
{
    Debugging,
    Python,
    Java,
    Cpp,
    Frontend,
    Backend,
    Database,
    Algorithms,
    Systems,
    Networks,
    GitDevOps,
    Architecture,
    Study,
    Career,
    DailyCare,
    EmotionalSupport,
    EnglishPractice,
    ProactiveChat,
    WanderingLife,
    DressesHobbies,
    EasterEgg,
    CharacterLife,
    SystemAmbient
}

public enum DialogueCategoryGroup
{
    Technical,
    Growth,
    Career,
    DailyCare,
    EmotionalReflection,
    CharacterLife,
    EasterEgg,
    SystemAmbient
}

public enum DialogueOutputMode
{
    SelfTalk,
    Ambient,
    UserDirect,
    SystemObserve
}

public enum DialogueTrigger
{
    Any,
    AppStart,
    Morning,
    Noon,
    Afternoon,
    Evening,
    LateNight,
    DayChanged,
    Weekday,
    Weekend,
    Holiday,
    Anniversary,
    LongSilence,
    IdeForeground,
    LongActive,
    IdleReturn,
    StoryTimer
}

public sealed record DialogueLine(
    string Id,
    DialogueCategory Category,
    DialogueCategoryGroup CategoryGroup,
    string TopicId,
    string SemanticGroup,
    DialogueOutputMode OutputMode,
    DialogueTrigger Trigger,
    IReadOnlyList<string> RequiredContext,
    string Tone,
    int InterruptionCost,
    double CooldownHours,
    double SemanticCooldownHours,
    int MaxPerDay,
    double Weight,
    bool RequiresReply,
    bool Enabled,
    string Text,
    string SourceKind,
    string SourceReference,
    string RewriteReason)
{
    public TimeSpan Cooldown => TimeSpan.FromHours(CooldownHours);

    public TimeSpan SemanticCooldown => TimeSpan.FromHours(SemanticCooldownHours);
}

public static class PersonaCorpus
{
    public const string EmbeddedResourceName = "CompanionDesktopPet.Assets.persona-corpus-v2.tsv";

    public static IReadOnlyList<string> V2Header { get; } =
    [
        "id", "category", "category_group", "topic_id", "semantic_group",
        "output_mode", "trigger", "required_context", "tone", "interrupt_cost",
        "cooldown_hours", "semantic_cooldown_hours", "max_per_day", "weight",
        "requires_reply", "enabled", "text", "source_kind", "source_reference",
        "rewrite_reason"
    ];

    private static readonly Lazy<CorpusSnapshot> Snapshot = new(Build);
    private static readonly string[] ForbiddenPiiMarkers =
        ["雷琳玥", "小玥", "玥玥", "湖南", "长沙", "广东", "月薪", "工资", "打零工"];

    public static IReadOnlyList<DialogueLine> All => Snapshot.Value.All;

    public static IReadOnlyList<DialogueLine> Regular => Snapshot.Value.Regular;

    public static IReadOnlyList<DialogueLine> EasterEggs => Snapshot.Value.EasterEggs;

    private static CorpusSnapshot Build()
    {
        using var stream = typeof(PersonaCorpus).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded persona corpus '{EmbeddedResourceName}' was not found.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var all = Parse(reader);
        if (all.Count is < 800 or > 1_200)
        {
            throw new InvalidDataException($"Enabled v2 persona corpus must contain 800-1200 rows, found {all.Count}.");
        }

        return new CorpusSnapshot(
            all,
            all.Where(line => line.CategoryGroup != DialogueCategoryGroup.EasterEgg).ToArray(),
            all.Where(line => line.CategoryGroup == DialogueCategoryGroup.EasterEgg).ToArray());
    }

    private static IReadOnlyList<DialogueLine> Parse(TextReader reader)
    {
        var rawHeader = reader.ReadLine() ?? throw new InvalidDataException("Persona v2 corpus is missing its header.");
        var header = rawHeader.TrimStart('\uFEFF').Split('\t');
        if (!header.SequenceEqual(V2Header, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Persona v2 corpus does not use the exact 20-column header.");
        }

        var columns = header.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        var lines = new List<DialogueLine>(1_200);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var normalizedTexts = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 1;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            var values = raw.Split('\t');
            if (values.Length != V2Header.Count)
            {
                throw Error(lineNumber, $"expected {V2Header.Count} columns, found {values.Length}");
            }

            string Value(string name) => values[columns[name]];
            var enabled = ParseBoolean(Value("enabled"), "enabled", lineNumber);
            if (!enabled)
            {
                continue;
            }

            var id = Required(Value("id"), "id", lineNumber);
            var text = Required(Value("text"), "text", lineNumber);
            var requiresReply = ParseBoolean(Value("requires_reply"), "requires_reply", lineNumber);
            if (requiresReply || text.Contains('?') || text.Contains('？'))
            {
                throw Error(lineNumber, "enabled rows cannot ask a question or require a reply");
            }

            if (ForbiddenPiiMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
            {
                throw Error(lineNumber, "enabled row contains a reviewed personal marker");
            }

            var normalized = Normalize(text);
            if (!ids.Add(id) || !normalizedTexts.Add(normalized))
            {
                throw Error(lineNumber, "enabled row duplicates an id or normalized text");
            }

            var cooldown = ParsePositiveDouble(Value("cooldown_hours"), "cooldown_hours", lineNumber);
            var semanticCooldown = ParsePositiveDouble(
                Value("semantic_cooldown_hours"), "semantic_cooldown_hours", lineNumber);
            if (semanticCooldown < cooldown)
            {
                throw Error(lineNumber, "semantic_cooldown_hours must be at least cooldown_hours");
            }

            var interruptionCost = ParseInteger(Value("interrupt_cost"), "interrupt_cost", lineNumber);
            var maxPerDay = ParseInteger(Value("max_per_day"), "max_per_day", lineNumber);
            if (interruptionCost is < 0 or > 5 || maxPerDay < 1)
            {
                throw Error(lineNumber, "interrupt_cost or max_per_day is outside the safe range");
            }

            lines.Add(new DialogueLine(
                id,
                ParseEnum<DialogueCategory>(Value("category"), "category", lineNumber),
                ParseSnakeEnum<DialogueCategoryGroup>(Value("category_group"), "category_group", lineNumber),
                Required(Value("topic_id"), "topic_id", lineNumber),
                Required(Value("semantic_group"), "semantic_group", lineNumber),
                ParseSnakeEnum<DialogueOutputMode>(Value("output_mode"), "output_mode", lineNumber),
                ParseSnakeEnum<DialogueTrigger>(Value("trigger"), "trigger", lineNumber),
                ParseContext(Value("required_context"), lineNumber),
                Required(Value("tone"), "tone", lineNumber),
                interruptionCost,
                cooldown,
                semanticCooldown,
                maxPerDay,
                ParsePositiveDouble(Value("weight"), "weight", lineNumber),
                requiresReply,
                enabled,
                text,
                Required(Value("source_kind"), "source_kind", lineNumber),
                Required(Value("source_reference"), "source_reference", lineNumber),
                Required(Value("rewrite_reason"), "rewrite_reason", lineNumber)));
        }

        return lines.ToArray();
    }

    private static IReadOnlyList<string> ParseContext(string value, int lineNumber)
    {
        var tokens = value.Split(',', StringSplitOptions.None);
        if (tokens.Length == 0
            || tokens.Any(string.IsNullOrWhiteSpace)
            || tokens.Any(token => token != token.Trim())
            || tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length
            || (tokens.Contains("none", StringComparer.Ordinal) && tokens.Length != 1))
        {
            throw Error(lineNumber, "required_context is malformed");
        }

        return tokens;
    }

    private static string Required(string value, string name, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(lineNumber, $"{name} must be non-blank");
        }

        return value;
    }

    private static bool ParseBoolean(string value, string name, int lineNumber) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw Error(lineNumber, $"{name} must be true or false")
    };

    private static int ParseInteger(string value, string name, int lineNumber) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw Error(lineNumber, $"{name} must be an integer");

    private static double ParsePositiveDouble(string value, string name, int lineNumber)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            || !double.IsFinite(result)
            || result <= 0)
        {
            throw Error(lineNumber, $"{name} must be a finite positive number");
        }

        return result;
    }

    private static T ParseEnum<T>(string value, string name, int lineNumber) where T : struct, Enum =>
        Enum.TryParse<T>(value, false, out var result) && result.ToString() == value
            ? result
            : throw Error(lineNumber, $"{name} contains an unknown value '{value}'");

    private static T ParseSnakeEnum<T>(string value, string name, int lineNumber) where T : struct, Enum
    {
        var canonical = string.Concat(value.Split('_').Select(part =>
            part.Length == 0 ? string.Empty : char.ToUpperInvariant(part[0]) + part[1..]));
        return ParseEnum<T>(canonical, name, lineNumber);
    }

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (!char.IsPunctuation(character)
                && !char.IsWhiteSpace(character)
                && category is not UnicodeCategory.Control
                    and not UnicodeCategory.Format
                    and not UnicodeCategory.SpaceSeparator
                    and not UnicodeCategory.LineSeparator
                    and not UnicodeCategory.ParagraphSeparator)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static InvalidDataException Error(int lineNumber, string detail) =>
        new($"Persona v2 corpus line {lineNumber}: {detail}.");

    private sealed record CorpusSnapshot(
        IReadOnlyList<DialogueLine> All,
        IReadOnlyList<DialogueLine> Regular,
        IReadOnlyList<DialogueLine> EasterEggs);
}
