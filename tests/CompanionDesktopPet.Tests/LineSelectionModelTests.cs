using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class LineSelectionModelTests
{
    [Fact]
    public void Train_UsesEveryRuntimeLineAndSeparatesFamilies()
    {
        var model = LineSelectionModel.Shared;

        Assert.Equal(82132, PersonaCorpus.All.Count);
        Assert.Equal(PersonaCorpus.All.Count, model.LineCount);
        Assert.Equal(
            PersonaCorpus.All.Select(line => line.SemanticGroup).Distinct().Count(),
            model.GroupCount);
        Assert.Contains(
            PersonaCorpus.All,
            line => model.TryGetGroup(line.SemanticGroup, out var group)
                    && group.Category == line.Category.ToString()
                    && group.Target is > 0 and <= 1);

        var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in PersonaCorpus.All)
        {
            Assert.True(model.TryGetGroup(line.SemanticGroup, out var group));
            if (group.Category == nameof(DialogueCategory.ProactiveChat))
            {
                families.Add(group.Family);
            }
        }

        Assert.True(families.Count > 1);

        var snapshot = Path.Combine(RepoRoot(), "data", "optimized", LineSelectionModel.SnapshotFileName);
        model.WriteSnapshot(snapshot);
        Assert.Contains("\"lineCount\":82132", File.ReadAllText(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void Adjustment_PrefersAnUnusedFamilyAndDownranksNearCopies()
    {
        var basis = PersonaCorpus.All[0];
        var english = "english phrasebook 今天把这个音标再慢慢读一遍，嘴型放轻一点。";
        var street = "street bus 路口的灯刚换，我们在这边等车就好，风有一点凉。";
        var lines = new[]
        {
            Line(basis, "en-1", "chat.en", "chat.english_phrase", english),
            Line(basis, "st-1", "chat.st", "chat.street_bus", street),
            Line(basis, "copy-a", "chat.copy.a", "chat.english_phrase", english),
            Line(basis, "copy-b", "chat.copy.b", "chat.english_phrase", english)
        };
        var model = LineSelectionModel.Train(lines);
        var recentEnglish = new[]
        {
            new SceneHistoryEntry("en", "chat.en", DateTime.UnixEpoch, english, Category: DialogueCategory.ProactiveChat)
        };

        var streetScore = model.Adjustment("chat.st", recentEnglish);
        var englishScore = model.Adjustment("chat.en", recentEnglish);
        Assert.True(streetScore > englishScore);

        Assert.True(model.TryGetGroup("chat.copy.a", out var left));
        Assert.True(model.TryGetGroup("chat.copy.b", out var right));
        Assert.True(model.TryGetGroup("chat.st", out var other));
        Assert.Equal(0, LineSelectionModel.HammingDistance(left.Fingerprint, right.Fingerprint));
        Assert.True(LineSelectionModel.HammingDistance(left.Fingerprint, other.Fingerprint) > LineSelectionModel.NearDuplicateHamming);

        var recentCopy = new[]
        {
            new SceneHistoryEntry("copy", "chat.copy.a", DateTime.UnixEpoch, english, Category: DialogueCategory.ProactiveChat)
        };
        Assert.True(model.Adjustment("chat.copy.b", recentCopy) < model.Adjustment("chat.st", recentCopy));
        Assert.Equal(0, model.Adjustment("missing.group", recentCopy));
    }

    private static DialogueLine Line(
        DialogueLine basis,
        string id,
        string semanticGroup,
        string topicId,
        string text) =>
        basis with
        {
            Id = id,
            Category = DialogueCategory.ProactiveChat,
            TopicId = topicId,
            SemanticGroup = semanticGroup,
            Text = text,
            Enabled = true
        };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CompanionDesktopPet.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
