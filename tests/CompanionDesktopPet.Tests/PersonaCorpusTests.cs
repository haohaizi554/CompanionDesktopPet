using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class PersonaCorpusTests
{
    private static readonly string[] RequiredMetadataProperties =
    [
        "Id",
        "Category",
        "CategoryGroup",
        "TopicId",
        "SemanticGroup",
        "OutputMode",
        "Trigger",
        "RequiredContext",
        "Tone",
        "InterruptionCost",
        "CooldownHours",
        "SemanticCooldownHours",
        "MaxPerDay",
        "Weight",
        "RequiresReply",
        "Enabled",
        "Text",
        "SourceKind",
        "SourceReference",
        "RewriteReason"
    ];

    [Fact]
    public void Corpus_LoadsTheCuratedEnabledV2Inventory()
    {
        var lines = PersonaCorpus.All;

        Assert.InRange(lines.Count, 800, 1_200);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Text)));
        Assert.Equal(lines.Count, lines.Select(line => Normalize(line.Text)).Distinct().Count());
        Assert.DoesNotContain(lines, line => line.Text.Contains('?') || line.Text.Contains('？'));
        var piiMarkers = new[] { "雷琳玥", "小玥", "玥玥", "湖南", "长沙", "广东", "月薪", "工资", "打零工" };
        Assert.DoesNotContain(lines, line => piiMarkers.Any(marker => line.Text.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplicationAssembly_DoesNotEmbedReviewedPiiMarkers()
    {
        var assemblyBytes = File.ReadAllBytes(typeof(PersonaCorpus).Assembly.Location);
        var piiMarkers = new[] { "雷琳玥", "小玥", "玥玥", "湖南", "长沙", "广东", "月薪", "工资", "打零工" };
        Encoding[] encodings = [Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode];

        Assert.All(piiMarkers, marker =>
            Assert.All(encodings, encoding =>
                Assert.False(
                    ContainsBytes(assemblyBytes, encoding.GetBytes(marker)),
                    $"Application assembly embeds reviewed PII marker bytes ({encoding.WebName}).")));
    }

    [Fact]
    public void Corpus_ExposesCompleteSafeV2Metadata()
    {
        var properties = typeof(DialogueLine)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        Assert.All(RequiredMetadataProperties, name => Assert.True(properties.ContainsKey(name), name));
        Assert.All(PersonaCorpus.All, line =>
        {
            Assert.True((bool)properties["Enabled"].GetValue(line)!);
            Assert.False((bool)properties["RequiresReply"].GetValue(line)!);
            AssertMetadata(properties, line, "Id");
            AssertMetadata(properties, line, "TopicId");
            AssertMetadata(properties, line, "SemanticGroup");
            AssertMetadata(properties, line, "Tone");
            AssertMetadata(properties, line, "SourceKind");
            AssertMetadata(properties, line, "SourceReference");
            AssertMetadata(properties, line, "RewriteReason");
            Assert.NotEmpty(line.RequiredContext);
            var cooldown = Convert.ToDouble(properties["CooldownHours"].GetValue(line), CultureInfo.InvariantCulture);
            var semanticCooldown = Convert.ToDouble(properties["SemanticCooldownHours"].GetValue(line), CultureInfo.InvariantCulture);
            Assert.True(cooldown >= 1);
            Assert.True(semanticCooldown >= cooldown);
            Assert.InRange((int)properties["InterruptionCost"].GetValue(line)!, 0, 5);
            Assert.InRange((int)properties["MaxPerDay"].GetValue(line)!, 1, 2);
            Assert.InRange(Convert.ToDouble(properties["Weight"].GetValue(line), CultureInfo.InvariantCulture), double.Epsilon, 2);
        });
    }

    [Fact]
    public void Corpus_EmbedsOnlyTheV2ResourceUnderTheStableLogicalName()
    {
        var resources = typeof(PersonaCorpus).Assembly.GetManifestResourceNames();

        Assert.Contains("CompanionDesktopPet.Assets.persona-corpus-v2.tsv", resources);
        Assert.DoesNotContain("CompanionDesktopPet.Assets.persona-corpus.tsv", resources);
        using var stream = typeof(PersonaCorpus).Assembly.GetManifestResourceStream(PersonaCorpus.EmbeddedResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal(string.Join('\t', PersonaCorpus.V2Header), reader.ReadLine()!.TrimStart('\uFEFF'));
    }

    [Fact]
    public void Corpus_TechnicalInventoryIsNotUsedAsTheRuntimeWeight()
    {
        var groupProperty = typeof(DialogueLine).GetProperty("CategoryGroup");
        Assert.NotNull(groupProperty);
        var technicalShare = PersonaCorpus.All.Count(line =>
                string.Equals(groupProperty!.GetValue(line)?.ToString(), "Technical", StringComparison.Ordinal))
            / (double)PersonaCorpus.All.Count;

        Assert.True(technicalShare > 0.40, $"Technical inventory share: {technicalShare:P2}");
        Assert.Equal(0.18, DialogueForest.CategoryGroupWeights[DialogueCategoryGroup.Technical], 6);
    }

    [Fact]
    public void EasterEggs_AreAFilteredViewOfTheEnabledCorpus()
    {
        var groupProperty = typeof(DialogueLine).GetProperty("CategoryGroup");
        Assert.NotNull(groupProperty);
        var expected = PersonaCorpus.All
            .Where(line => string.Equals(groupProperty!.GetValue(line)?.ToString(), "EasterEgg", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, PersonaCorpus.EasterEggs);
        Assert.DoesNotContain(PersonaCorpus.Regular, expected.Contains);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_AcceptsOnlyUtf8WithAnOptionalUtf8Bom(bool includeBom)
    {
        using var stream = CorpusStream(includeBom ? new UTF8Encoding(true, true) : new UTF8Encoding(false, true));

        var line = Assert.Single(PersonaCorpus.Load(stream));

        Assert.Equal("v2_test_001", line.Id);
        Assert.True(line.Enabled);
    }

    [Fact]
    public void Load_RejectsUtf16AndUtf32EvenWhenTheyHaveAByteOrderMark()
    {
        Encoding[] encodings =
        [
            new UnicodeEncoding(false, true, true),
            new UnicodeEncoding(true, true, true),
            new UTF32Encoding(false, true, true),
            new UTF32Encoding(true, true, true)
        ];

        foreach (var encoding in encodings)
        {
            using var stream = CorpusStream(encoding);
            Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
        }
    }

    [Theory]
    [InlineData("category", "999")]
    [InlineData("category_group", "999")]
    [InlineData("output_mode", "999")]
    [InlineData("trigger", "999")]
    [InlineData("tone", "sarcastic")]
    [InlineData("source_kind", "generated")]
    [InlineData("source_kind", "archived_question")]
    [InlineData("source_kind", "manual_review")]
    [InlineData("required_context", "time:twilight")]
    [InlineData("cooldown_hours", "0.5")]
    [InlineData("semantic_cooldown_hours", "0.5")]
    [InlineData("max_per_day", "3")]
    [InlineData("weight", "2.01")]
    public void Load_RejectsValuesOutsideTheControlledRuntimeContract(string column, string value)
    {
        using var stream = CorpusStream(new UTF8Encoding(false, true), (column, value));

        Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
    }

    [Theory]
    [InlineData("calm", "curated_standalone", "not_fullscreen")]
    [InlineData("sleepy", "preserved_easter_egg", "time:dawn")]
    [InlineData("encouraging", "new_ambient", "date:month_boundary")]
    public void Load_AcceptsEveryControlledFieldFamily(string tone, string sourceKind, string context)
    {
        using var stream = CorpusStream(
            new UTF8Encoding(false, true),
            ("tone", tone),
            ("source_kind", sourceKind),
            ("required_context", context));

        var line = Assert.Single(PersonaCorpus.Load(stream));

        Assert.Equal(tone, line.Tone);
        Assert.Equal(sourceKind, line.SourceKind);
        Assert.Equal(context, Assert.Single(line.RequiredContext));
    }

    [Theory]
    [InlineData("archived_question")]
    [InlineData("manual_review")]
    public void Load_AllowsControlledReviewSourcesOnlyWhenDisabled(string sourceKind)
    {
        using var stream = CorpusStream(
            new UTF8Encoding(false, true),
            ("enabled", "false"),
            ("source_kind", sourceKind));

        Assert.Empty(PersonaCorpus.Load(stream));
    }

    [Fact]
    public void Load_ValidatesControlledFieldsBeforeDiscardingDisabledRows()
    {
        using var stream = CorpusStream(
            new UTF8Encoding(false, true),
            ("enabled", "false"),
            ("tone", "sarcastic"));

        Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
    }

    private static void AssertMetadata(
        IReadOnlyDictionary<string, PropertyInfo> properties,
        DialogueLine line,
        string name) =>
        Assert.False(string.IsNullOrWhiteSpace((string?)properties[name].GetValue(line)), name);

    private static MemoryStream CorpusStream(Encoding encoding, params (string Name, string Value)[] changes)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "v2_test_001",
            ["category"] = "Python",
            ["category_group"] = "technical",
            ["topic_id"] = "python.testing",
            ["semantic_group"] = "python.testing.red_green",
            ["output_mode"] = "self_talk",
            ["trigger"] = "any",
            ["required_context"] = "none",
            ["tone"] = "dry",
            ["interrupt_cost"] = "1",
            ["cooldown_hours"] = "1",
            ["semantic_cooldown_hours"] = "2",
            ["max_per_day"] = "1",
            ["weight"] = "1",
            ["requires_reply"] = "false",
            ["enabled"] = "true",
            ["text"] = "测试先红，再改实现。",
            ["source_kind"] = "rewritten_topic",
            ["source_reference"] = "test",
            ["rewrite_reason"] = "runtime parser fixture"
        };
        foreach (var (name, value) in changes)
        {
            values[name] = value;
        }

        var content = string.Join('\t', PersonaCorpus.V2Header) + "\n"
            + string.Join('\t', PersonaCorpus.V2Header.Select(column => values[column])) + "\n";
        var bytes = encoding.GetBytes(content);
        var preamble = encoding.GetPreamble();
        var stream = new MemoryStream(preamble.Length + bytes.Length);
        stream.Write(preamble);
        stream.Write(bytes);
        stream.Position = 0;
        return stream;
    }

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is not UnicodeCategory.Control
                and not UnicodeCategory.Format
                and not UnicodeCategory.SpaceSeparator
                and not UnicodeCategory.LineSeparator
                and not UnicodeCategory.ParagraphSeparator
                && !char.IsPunctuation(character)
                && !char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool ContainsBytes(byte[] source, byte[] candidate)
    {
        if (candidate.Length == 0 || candidate.Length > source.Length)
        {
            return false;
        }

        for (var offset = 0; offset <= source.Length - candidate.Length; offset++)
        {
            if (source.AsSpan(offset, candidate.Length).SequenceEqual(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
