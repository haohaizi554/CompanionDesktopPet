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

    private static void AssertMetadata(
        IReadOnlyDictionary<string, PropertyInfo> properties,
        DialogueLine line,
        string name) =>
        Assert.False(string.IsNullOrWhiteSpace((string?)properties[name].GetValue(line)), name);

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
}
