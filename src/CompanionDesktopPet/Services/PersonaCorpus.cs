using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

public enum PersonaSourceTier
{
    Authored,
    Legacy
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
    string RewriteReason,
    string RelationshipProfile = "neutral")
{
    private string _text = Text;
    private bool? _hasSeasoningMarker;
    private IReadOnlyList<string>? _identityMarkerClasses;
    private SurfaceExposureProfile? _surfaceExposure;

    public string Text
    {
        get => _text;
        init
        {
            _text = value;
            _hasSeasoningMarker = null;
            _identityMarkerClasses = null;
            _surfaceExposure = null;
        }
    }

    public bool HasSeasoningMarker =>
        _hasSeasoningMarker ??= PersonaContractGenerated.ContainsSeasoningMarker(Text);

    public IReadOnlyList<string> IdentityMarkerClasses =>
        _identityMarkerClasses ??= PersonaContractGenerated.FindAuthoredIdentityMarkers(Text);

    internal SurfaceExposureProfile SurfaceExposureProfile =>
        _surfaceExposure ??= SurfaceExposure.Profile(Text);

    public TimeSpan Cooldown => TimeSpan.FromHours(CooldownHours);

    public TimeSpan SemanticCooldown => TimeSpan.FromHours(SemanticCooldownHours);

    public PersonaSourceTier SourceTier => SourceKind == "curated_authored"
        ? PersonaSourceTier.Authored
        : PersonaSourceTier.Legacy;
}

public static class PersonaCorpus
{
    public const string EmbeddedResourceName = "CompanionDesktopPet.Assets.persona-corpus-v2.tsv";
    public const int MinimumRuntimeRows = PersonaContractGenerated.ExpandedRuntimeMinimumRows;
    public const int MaximumRuntimeRows = PersonaContractGenerated.ExpandedRuntimeMaximumRows;
    public const int ExpectedRuntimeRows = PersonaContractGenerated.ExpandedRuntimeRows;
    public const int ExpectedLegacySurfaceRows = PersonaContractGenerated.LegacySurfaceRows;
    public const int ExpectedAuthoredRuntimeRows = PersonaContractGenerated.ExpectedAuthoredRuntimeRows;

    public static IReadOnlyList<string> V2Header { get; } =
    [
        "id", "category", "category_group", "topic_id", "semantic_group",
        "output_mode", "trigger", "required_context", "tone", "interrupt_cost",
        "cooldown_hours", "semantic_cooldown_hours", "max_per_day", "weight",
        "requires_reply", "enabled", "relationship_profile", "text", "source_kind", "source_reference",
        "rewrite_reason"
    ];

    private static readonly Lazy<CorpusSnapshot> Snapshot = new(Build);
    private static readonly HashSet<string> DisabledOnlySourceKinds = new(StringComparer.Ordinal)
    {
        "archived_question", "manual_review"
    };
    private static readonly Regex DirectIdentifierPattern = new(
        @"(?<!\d)(?:1[3-9]\d{9}|[1-9]\d{16}[\dXx]?)(?!\d)|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant);
    public static IReadOnlyList<DialogueLine> All => Snapshot.Value.All;

    public static IReadOnlyList<DialogueLine> Regular => Snapshot.Value.Regular;

    public static IReadOnlyList<DialogueLine> EasterEggs => Snapshot.Value.EasterEggs;

    public static IReadOnlySet<string> EditorialIdentityEasterEggIds { get; } =
        PersonaContractGenerated.IdentityEasterEggRules.Keys.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<DialogueLine> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            return Parse(reader);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Persona v2 corpus must be strict UTF-8.", exception);
        }
    }

    private static CorpusSnapshot Build()
    {
        using var stream = typeof(PersonaCorpus).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded persona corpus '{EmbeddedResourceName}' was not found.");
        var all = Load(stream);
        if (all.Count != ExpectedRuntimeRows)
        {
            throw new InvalidDataException(
                $"Enabled v2 persona corpus must contain exactly {ExpectedRuntimeRows} rows, found {all.Count}.");
        }

        var legacySurfaceRows = all.Count(line => line.SourceKind == "legacy_surface_variant");
        if (legacySurfaceRows != ExpectedLegacySurfaceRows)
        {
            throw new InvalidDataException(
                $"Enabled v2 persona corpus must contain exactly {ExpectedLegacySurfaceRows} legacy surface rows, found {legacySurfaceRows}.");
        }

        var authoredRows = all.Count(line => line.SourceKind == "curated_authored");
        if (authoredRows != ExpectedAuthoredRuntimeRows)
        {
            throw new InvalidDataException(
                $"Enabled v2 persona corpus must contain exactly {ExpectedAuthoredRuntimeRows} authored rows, found {authoredRows}.");
        }

        var legacyCuratedRows = all.Count(line =>
            line.SourceTier == PersonaSourceTier.Legacy
            && line.SourceKind != "legacy_surface_variant");
        if (legacyCuratedRows != PersonaContractGenerated.ExpectedLegacyCuratedRows)
        {
            throw new InvalidDataException(
                $"Enabled v2 persona corpus must contain exactly {PersonaContractGenerated.ExpectedLegacyCuratedRows} legacy curated rows, found {legacyCuratedRows}.");
        }


        return new CorpusSnapshot(
            all,
            all.Where(line => line.CategoryGroup != DialogueCategoryGroup.EasterEgg).ToArray(),
            all.Where(line => line.CategoryGroup == DialogueCategoryGroup.EasterEgg).ToArray());
    }

    private static IReadOnlyList<DialogueLine> Parse(TextReader reader)
    {
        var rawHeader = reader.ReadLine() ?? throw new InvalidDataException("Persona v2 corpus is missing its header.");
        var normalizedHeader = rawHeader.StartsWith('\uFEFF') ? rawHeader[1..] : rawHeader;
        var header = normalizedHeader.Split('\t');
        if (!header.SequenceEqual(V2Header, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Persona v2 corpus does not use the exact 21-column header.");
        }

        var columns = header.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        var lines = new List<DialogueLine>(MaximumRuntimeRows);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var normalizedTexts = new HashSet<string>(StringComparer.Ordinal);
        var sharedStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        var sharedContexts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var categoryGroupCache = new Dictionary<string, DialogueCategoryGroup>(StringComparer.Ordinal);
        var outputModeCache = new Dictionary<string, DialogueOutputMode>(StringComparer.Ordinal);
        var triggerCache = new Dictionary<string, DialogueTrigger>(StringComparer.Ordinal);
        var topicSlugCache = new Dictionary<string, string>(StringComparer.Ordinal);
        string Share(string value)
        {
            if (sharedStrings.TryGetValue(value, out var existing))
            {
                return existing;
            }
            sharedStrings[value] = value;
            return value;
        }
        var lineNumber = 1;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            var values = raw.Split('\t');
            if (values.Length != V2Header.Count)
            {
                throw Error(lineNumber, $"expected {V2Header.Count} columns, found {values.Length}");
            }

            if (raw.Contains('\uFEFF') || raw.Contains('\0'))
            {
                throw Error(lineNumber, "embedded byte-order marks and NUL characters are not allowed");
            }

            string Value(string name) => values[columns[name]];
            var enabled = ParseBoolean(Value("enabled"), "enabled", lineNumber);
            var id = Required(Value("id"), "id", lineNumber);
            var category = ParseEnum<DialogueCategory>(Value("category"), "category", lineNumber);
            var rawCategoryGroup = Value("category_group");
            if (!categoryGroupCache.TryGetValue(rawCategoryGroup, out var categoryGroup))
            {
                categoryGroup = ParseSnakeEnum<DialogueCategoryGroup>(
                    rawCategoryGroup, "category_group", lineNumber);
                categoryGroupCache[rawCategoryGroup] = categoryGroup;
            }
            if (!PersonaContractGenerated.CategoryGroupByCategory.TryGetValue(category, out var expectedGroup)
                || categoryGroup != expectedGroup)
            {
                throw Error(
                    lineNumber,
                    $"category {category} must use category_group "
                    + $"{expectedGroup}");
            }
            var topicId = Share(Required(Value("topic_id"), "topic_id", lineNumber));
            var semanticGroup = Share(Required(Value("semantic_group"), "semantic_group", lineNumber));
            var rawOutputMode = Value("output_mode");
            if (!outputModeCache.TryGetValue(rawOutputMode, out var outputMode))
            {
                outputMode = ParseSnakeEnum<DialogueOutputMode>(rawOutputMode, "output_mode", lineNumber);
                outputModeCache[rawOutputMode] = outputMode;
            }
            if (!PersonaContractGenerated.CategoryGroupOutputModes.TryGetValue(
                    categoryGroup, out var expectedOutputMode)
                || outputMode != expectedOutputMode)
            {
                throw Error(
                    lineNumber,
                    $"category_group {categoryGroup} must use output_mode {expectedOutputMode}");
            }
            var rawTrigger = Value("trigger");
            if (!triggerCache.TryGetValue(rawTrigger, out var trigger))
            {
                trigger = ParseSnakeEnum<DialogueTrigger>(rawTrigger, "trigger", lineNumber);
                triggerCache[rawTrigger] = trigger;
            }
            var rawContext = Value("required_context");
            if (!sharedContexts.TryGetValue(rawContext, out var requiredContext))
            {
                requiredContext = ParseContext(rawContext, lineNumber)
                    .Select(Share)
                    .ToArray();
                sharedContexts[rawContext] = requiredContext;
            }
            var tone = Share(ParseControlled(
                Value("tone"), "tone", PersonaContractGenerated.ControlledTones, lineNumber));
            var interruptionCost = ParseInteger(Value("interrupt_cost"), "interrupt_cost", lineNumber);
            var cooldown = ParseMinimumDouble(Value("cooldown_hours"), "cooldown_hours", 1, lineNumber);
            var semanticCooldown = ParseMinimumDouble(
                Value("semantic_cooldown_hours"), "semantic_cooldown_hours", 1, lineNumber);
            var maxPerDay = ParseInteger(Value("max_per_day"), "max_per_day", lineNumber);
            var weight = ParsePositiveDouble(Value("weight"), "weight", lineNumber);
            var requiresReply = ParseBoolean(Value("requires_reply"), "requires_reply", lineNumber);
            var text = Required(Value("text"), "text", lineNumber);
            var relationshipProfile = Share(ParseControlled(
                Value("relationship_profile"),
                "relationship_profile",
                PersonaContractGenerated.ControlledRelationshipProfiles,
                lineNumber));
            var sourceKind = Share(ParseControlled(
                Value("source_kind"), "source_kind", PersonaContractGenerated.ControlledSourceKinds, lineNumber));
            var sourceReference = Required(Value("source_reference"), "source_reference", lineNumber);
            var rewriteReason = Share(Required(Value("rewrite_reason"), "rewrite_reason", lineNumber));
            var normalized = Normalize(text);

            if (sourceKind == "legacy_surface_variant")
            {
                if (!topicSlugCache.TryGetValue(topicId, out var topicSlug))
                {
                    var slugBuilder = new StringBuilder(topicId.Length);
                    var previousUnderscore = false;
                    foreach (var character in topicId.ToLowerInvariant())
                    {
                        if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
                        {
                            slugBuilder.Append(character);
                            previousUnderscore = false;
                        }
                        else if (!previousUnderscore && slugBuilder.Length > 0)
                        {
                            slugBuilder.Append('_');
                            previousUnderscore = true;
                        }
                    }
                    topicSlug = slugBuilder.ToString().Trim('_');
                    if (topicSlug.Length == 0)
                    {
                        topicSlug = "topic";
                    }
                    topicSlugCache[topicId] = topicSlug;
                }
                ValidateLegacySurfaceLineage(
                    id,
                    topicId,
                    normalized,
                    sourceReference,
                    topicSlug,
                    lineNumber);
            }

            var identityHits = PersonaContractGenerated.FindAuthoredIdentityMarkers(text);
            var hasIdentityRule = PersonaContractGenerated.IdentityEasterEggRules.TryGetValue(
                id, out var identityRule);
            if (hasIdentityRule)
            {
                ValidateLegacyIdentityEasterEgg(
                    identityRule!,
                    identityHits,
                    text,
                    category,
                    categoryGroup,
                    topicId,
                    sourceReference,
                    cooldown,
                    maxPerDay,
                    weight,
                    lineNumber);
            }
            else if (identityHits.Count > 0)
            {
                ValidateAuthoredIdentityLine(
                    identityHits,
                    text,
                    sourceKind,
                    sourceReference,
                    lineNumber);
            }

            if (semanticCooldown < cooldown)
            {
                throw Error(lineNumber, "semantic_cooldown_hours must be at least cooldown_hours");
            }
            if (interruptionCost is < 0 or > 5 || maxPerDay is not (1 or 2) || weight > 2)
            {
                throw Error(lineNumber, "interrupt_cost, max_per_day, or weight is outside the safe range");
            }
            if (enabled && DisabledOnlySourceKinds.Contains(sourceKind))
            {
                throw Error(lineNumber, "archived_question and manual_review rows cannot be enabled");
            }
            if (!enabled)
            {
                continue;
            }
            if (requiresReply || text.Contains('?') || text.Contains('？'))
            {
                throw Error(lineNumber, "enabled rows cannot ask a question or require a reply");
            }

            if (!ids.Add(id) || !normalizedTexts.Add(normalized))
            {
                throw Error(lineNumber, "enabled row duplicates an id or normalized text");
            }

            lines.Add(new DialogueLine(
                id,
                category,
                categoryGroup,
                topicId,
                semanticGroup,
                outputMode,
                trigger,
                requiredContext,
                tone,
                interruptionCost,
                cooldown,
                semanticCooldown,
                maxPerDay,
                weight,
                requiresReply,
                enabled,
                text,
                sourceKind,
                sourceReference,
                rewriteReason,
                relationshipProfile));
        }

        return lines.ToArray();
    }

    private static IReadOnlyList<string> ParseContext(string value, int lineNumber)
    {
        var tokens = value.Split(',', StringSplitOptions.None);
        if (tokens.Length == 0
            || tokens.Any(string.IsNullOrWhiteSpace)
            || tokens.Any(token => token != token.Trim())
            || tokens.Any(token => !PersonaContractGenerated.ControlledContextTokens.Contains(token))
            || tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length
            || (tokens.Contains("none", StringComparer.Ordinal) && tokens.Length != 1))
        {
            throw Error(lineNumber, "required_context is malformed");
        }

        return tokens;
    }

    private static string ParseControlled(
        string value,
        string name,
        IReadOnlySet<string> allowed,
        int lineNumber)
    {
        var required = Required(value, name, lineNumber);
        return allowed.Contains(required)
            ? required
            : throw Error(lineNumber, $"{name} contains an unknown value '{required}'");
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

    private static double ParseMinimumDouble(string value, string name, double minimum, int lineNumber)
    {
        var result = ParsePositiveDouble(value, name, lineNumber);
        return result >= minimum
            ? result
            : throw Error(lineNumber, $"{name} must be at least {minimum.ToString(CultureInfo.InvariantCulture)}");
    }

    private static T ParseEnum<T>(string value, string name, int lineNumber) where T : struct, Enum =>
        Enum.TryParse<T>(value, false, out var result)
        && Enum.IsDefined(result)
        && result.ToString() == value
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

    private static void ValidateLegacySurfaceLineage(
        string id,
        string topicId,
        string normalizedText,
        string sourceReference,
        string topicSlug,
        int lineNumber)
    {
        const string legacyPrefix = "legacy:";
        const string topicMarker = ";topic:";
        const string variantMarker = ";variant:";
        var topicMarkerAt = sourceReference.IndexOf(topicMarker, StringComparison.Ordinal);
        var variantMarkerAt = sourceReference.IndexOf(
            variantMarker,
            Math.Max(0, topicMarkerAt + topicMarker.Length),
            StringComparison.Ordinal);
        if (!sourceReference.StartsWith(legacyPrefix, StringComparison.Ordinal)
            || topicMarkerAt <= legacyPrefix.Length
            || variantMarkerAt <= topicMarkerAt + topicMarker.Length)
        {
            throw Error(lineNumber, "legacy surface source_reference is malformed or topic-unbound");
        }

        var sourceLine = sourceReference[legacyPrefix.Length..topicMarkerAt];
        var referenceTopic = sourceReference[
            (topicMarkerAt + topicMarker.Length)..variantMarkerAt];
        var variant = sourceReference[(variantMarkerAt + variantMarker.Length)..];
        if (sourceLine.Length == 0
            || sourceLine[0] == '0'
            || sourceLine.Any(character => character is < '0' or > '9')
            || referenceTopic != topicId)
        {
            throw Error(lineNumber, "legacy surface source_reference is malformed or topic-unbound");
        }

        var identity = sourceLine + "\0" + topicId + "\0" + normalizedText;
        Span<byte> hash = stackalloc byte[32];
        var byteCount = Encoding.UTF8.GetByteCount(identity);
        Span<byte> utf8 = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(identity, utf8);
        SHA256.HashData(utf8, hash);
        var digest = Convert.ToHexString(hash[..6]).ToLowerInvariant();
        var expectedVariant = $"surface_{sourceLine}_{digest}";
        var expectedId = $"v2_surface_{sourceLine}_{topicSlug}_{digest}";
        if (variant != expectedVariant || id != expectedId)
        {
            throw Error(lineNumber, "legacy surface id or variant digest does not match immutable lineage");
        }
    }

    private static void ValidateLegacyIdentityEasterEgg(
        IdentityEasterEggRule identityRule,
        IReadOnlyList<string> identityHits,
        string text,
        DialogueCategory category,
        DialogueCategoryGroup categoryGroup,
        string topicId,
        string sourceReference,
        double cooldown,
        int maxPerDay,
        double weight,
        int lineNumber)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
        if (PersonaContractGenerated.LegacyForbiddenIdentityMarkers.Any(marker =>
                text.Contains(marker, StringComparison.Ordinal))
            || category != DialogueCategory.EasterEgg
            || categoryGroup != DialogueCategoryGroup.EasterEgg
            || sourceReference != identityRule.SourceReference
            || topicId != identityRule.TopicId
            || digest != identityRule.TextSha256
            || identityHits.Count != identityRule.AllowedMarkers.Count
            || identityHits.Any(marker => !identityRule.AllowedMarkers.Contains(marker))
            || cooldown != identityRule.CooldownHours
            || maxPerDay != identityRule.MaxPerDay
            || weight != identityRule.Weight)
        {
            throw Error(lineNumber, "identity EasterEgg does not exactly match the editorial manifest");
        }
    }

    private static void ValidateAuthoredIdentityLine(
        IReadOnlyList<string> identityHits,
        string text,
        string sourceKind,
        string sourceReference,
        int lineNumber)
    {
        if (sourceKind != "curated_authored"
            || !sourceReference.StartsWith("catalog:authored-v1:", StringComparison.Ordinal)
            || !PersonaContractGenerated.AuthoredIdentity.AllowMarkersInAnyCategory
            || text.Contains('?')
            || text.Contains('\uFF1F')
            || DirectIdentifierPattern.IsMatch(text)
            || identityHits.Any(marker => !PersonaContractGenerated.AuthoredIdentity.Markers.Contains(marker)))
        {
            throw Error(lineNumber, "authored identity text violates the generated identity policy");
        }
    }

    private static InvalidDataException Error(int lineNumber, string detail) =>
        new($"Persona v2 corpus line {lineNumber}: {detail}.");

    private sealed record CorpusSnapshot(
        IReadOnlyList<DialogueLine> All,
        IReadOnlyList<DialogueLine> Regular,
        IReadOnlyList<DialogueLine> EasterEggs);
}
