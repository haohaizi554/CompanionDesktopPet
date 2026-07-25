using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

[Collection(PerformanceTestCollection.Name)]
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

        Assert.Equal(PersonaContractGenerated.ExpandedRuntimeRows, lines.Count);
        Assert.Equal(
            PersonaContractGenerated.LegacySurfaceRows,
            lines.Count(line => line.SourceKind == "legacy_surface_variant"));
        Assert.Equal(PersonaContractGenerated.SemanticSceneCount, SceneCatalog.PersonaScenes.Count);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Text)));
        Assert.Equal(lines.Count, lines.Select(line => Normalize(line.Text)).Distinct().Count());
        Assert.DoesNotContain(lines, line => line.Text.Contains('?') || line.Text.Contains('？'));
        Assert.All(lines, line => Assert.False(line.RequiresReply));
    }

    [Fact]
    public void ApplicationAssembly_EmbedsTheExactEditorialIdentityPolicy()
    {
        Assert.Equal(29, PersonaCorpus.EditorialIdentityEasterEggIds.Count);
        Assert.Equal(0.07, PersonaContractGenerated.DrySharpSceneHashThreshold, 8);
        Assert.Equal(0.04, PersonaContractGenerated.DrySharpSceneInventoryMinimum, 8);
        Assert.Equal(0.06, PersonaContractGenerated.DrySharpSceneInventoryMaximum, 8);
        Assert.Equal("expanded_runtime", PersonaContractGenerated.DrySharpSceneInventoryEnforcementProfile);
        Assert.Equal("observation_only", PersonaContractGenerated.DrySharpRowInventoryPolicy);
        Assert.Equal(0.10, PersonaContractGenerated.SeasoningCuratedCoreInventoryMaximum, 8);
        Assert.Equal("observation_only", PersonaContractGenerated.SeasoningExpandedRuntimeInventoryPolicy);
    }

    [Fact]
    public void ApplicationAssembly_EmbedsEasterEggPlaybackAcceptanceBounds()
    {
        Assert.Equal(0.08, PersonaContractGenerated.EasterEggPlaybackMinimum, 8);
        Assert.Equal(0.12, PersonaContractGenerated.EasterEggPlaybackMaximum, 8);
    }

    [Theory]
    [InlineData("嗯嗯，这次可以。")]
    [InlineData("哈？这次可以。")]
    [InlineData("这次 6，确实可以。")]
    [InlineData("666！")]
    [InlineData("这个结果 nb。")]
    public void GeneratedSeasoningMatcher_AcceptsSharedLexicalMarkers(string text)
    {
        Assert.True(PersonaContractGenerated.ContainsSeasoningMarker(text));
    }

    [Theory]
    [InlineData("Python 3.6")]
    [InlineData("IPv6")]
    [InlineData("6666")]
    [InlineData("v666")]
    [InlineData("第6次")]
    [InlineData("6月")]
    [InlineData("6个")]
    [InlineData("SNBModel")]
    [InlineData("nb_value")]
    [InlineData("玥玥把书翻到下一页。")]
    public void GeneratedSeasoningMatcher_RejectsSubstringsAndIdentityMarkers(string text)
    {
        Assert.False(PersonaContractGenerated.ContainsSeasoningMarker(text));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Corpus_ParsesExpandedRuntimeInventoryWithinDesktopStartupBudget()
    {
        using var stream = typeof(PersonaCorpus).Assembly.GetManifestResourceStream(PersonaCorpus.EmbeddedResourceName);
        Assert.NotNull(stream);
        var before = RetainedMemoryMeasurement.Snapshot();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var lines = PersonaCorpus.Load(stream!);

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var retainedBytes = RetainedMemoryMeasurement.Snapshot() - before;
        Console.WriteLine(
            $"corpus parse: elapsed={stopwatch.Elapsed} allocated={allocatedBytes:N0} retained={retainedBytes:N0}");
        Assert.Equal(PersonaContractGenerated.ExpandedRuntimeRows, lines.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), stopwatch.Elapsed.ToString());
        Assert.True(allocatedBytes < 256L * 1024 * 1024, $"allocated bytes: {allocatedBytes:N0}");
        Assert.True(
            retainedBytes >= 0,
            $"retained-memory measurement was invalid: {retainedBytes:N0} bytes");
        Assert.True(retainedBytes < 128L * 1024 * 1024, $"retained bytes: {retainedBytes:N0}");
        GC.KeepAlive(lines);
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

    [Fact]
    public void Load_ValidatesLegacySurfaceStableIdVariantAndTopicBinding()
    {
        var values = new (string Name, string Value)[]
        {
            ("id", "v2_surface_42_topic_test_008be622ad56"),
            ("topic_id", "topic.test"),
            ("source_kind", "legacy_surface_variant"),
            ("source_reference", "legacy:42;topic:topic.test;variant:surface_42_008be622ad56")
        };
        using (var valid = CorpusStream(new UTF8Encoding(false, true), values))
        {
            Assert.Single(PersonaCorpus.Load(valid));
        }

        foreach (var mutation in new[]
                 {
                     ("id", "v2_surface_42_topic_test_000000000000"),
                     ("source_reference", "legacy:42;topic:wrong.topic;variant:surface_42_008be622ad56"),
                     ("source_reference", "legacy:42;topic:topic.test;variant:surface_42_000000000000")
                 })
        {
            using var invalid = CorpusStream(
                new UTF8Encoding(false, true),
                [.. values.Where(item => item.Name != mutation.Item1), mutation]);
            Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(invalid));
        }
    }

    [Fact]
    public void Corpus_IdentityEasterEggsAreExactAndPrivacyScoped()
    {
        var fullName = "\u96f7\u7433\u73a5";
        var nickname = "\u5c0f\u73a5";
        var repeatedNickname = "\u73a5\u73a5";
        var identityLines = PersonaCorpus.All
            .Where(line => line.Text.Contains(fullName, StringComparison.Ordinal)
                           || line.Text.Contains(nickname, StringComparison.Ordinal)
                           || line.Text.Contains(repeatedNickname, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(29, PersonaCorpus.EditorialIdentityEasterEggIds.Count);
        Assert.Equal(29, identityLines.Length);
        Assert.True(
            identityLines.Select(line => line.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(PersonaCorpus.EditorialIdentityEasterEggIds));
        Assert.All(identityLines, line =>
            Assert.Contains(line.Id, PersonaCorpus.EditorialIdentityEasterEggIds));
        Assert.Single(identityLines, line => line.Text.Contains(fullName, StringComparison.Ordinal));
        Assert.Single(identityLines, line => line.Text.Contains(nickname, StringComparison.Ordinal));
        Assert.All(identityLines, line =>
        {
            Assert.Equal(DialogueCategory.EasterEgg, line.Category);
            Assert.Equal(DialogueCategoryGroup.EasterEgg, line.CategoryGroup);
            Assert.Equal(720, line.CooldownHours);
            Assert.Equal(1, line.MaxPerDay);
            Assert.Equal(0.1, line.Weight, 8);
        });
        var forbidden = new[]
        {
            "\u6e56\u5357", "\u957f\u6c99", "\u5e7f\u4e1c", "\u6708\u85aa",
            "\u5de5\u8d44", "\u6536\u5165", "\u6253\u96f6\u5de5", "\u6362\u5de5\u4f5c"
        };
        Assert.DoesNotContain(identityLines, line =>
            forbidden.Any(marker => line.Text.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public void Load_RejectsCategoryGroupMismatchFromTheSharedContract()
    {
        using var stream = CorpusStream(
            new UTF8Encoding(false, true),
            ("category", "Career"),
            ("category_group", "technical"));

        Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Load_RejectsCategoryGroupOutputModeMismatchEvenWhenDisabled(string enabled)
    {
        using var stream = CorpusStream(
            new UTF8Encoding(false, true),
            ("category", "DailyCare"),
            ("category_group", "daily_care"),
            ("output_mode", "self_talk"),
            ("enabled", enabled));

        Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
    }

    [Fact]
    public void Load_RejectsEditedIdentityTextAndAppendedDirectIdentifier()
    {
        var exact = "\u8fd9\u679a\u5f88\u5c11\u51fa\u73b0\u7684\u540d\u724c\uff0c\u5199\u7740\u96f7\u7433\u73a5\u3002";
        var edits = new[] { exact[..^1] + "\uff01", exact + " 13800138000" };
        foreach (var text in edits)
        {
            using var stream = CorpusStream(
                new UTF8Encoding(false, true),
                ("id", "v2_egg_editorial_full_name_01_3230a1453d30"),
                ("category", "EasterEgg"),
                ("category_group", "easter_egg"),
                ("topic_id", "easter_egg.editorial_identity.full_name"),
                ("semantic_group", "easter_egg.editorial_identity.full_name"),
                ("output_mode", "self_talk"),
                ("tone", "playful"),
                ("interrupt_cost", "0"),
                ("cooldown_hours", "720"),
                ("semantic_cooldown_hours", "720"),
                ("weight", "0.1"),
                ("text", text),
                ("source_kind", "curated_standalone"),
                ("source_reference", "catalog:editorial-easter-egg.identity-v1;variant:egg_editorial_full_name_01"));

            Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(stream));
        }

        using var wrongTopic = CorpusStream(
            new UTF8Encoding(false, true),
            ("id", "v2_egg_editorial_full_name_01_3230a1453d30"),
            ("category", "EasterEgg"),
            ("category_group", "easter_egg"),
            ("topic_id", "easter_egg.editorial_identity.wrong"),
            ("semantic_group", "easter_egg.editorial_identity.full_name"),
            ("output_mode", "self_talk"),
            ("tone", "playful"),
            ("interrupt_cost", "0"),
            ("cooldown_hours", "720"),
            ("semantic_cooldown_hours", "720"),
            ("weight", "0.1"),
            ("text", exact),
            ("source_kind", "curated_standalone"),
            ("source_reference", "catalog:editorial-easter-egg.identity-v1;variant:egg_editorial_full_name_01"));

        Assert.Throws<InvalidDataException>(() => PersonaCorpus.Load(wrongTopic));
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
