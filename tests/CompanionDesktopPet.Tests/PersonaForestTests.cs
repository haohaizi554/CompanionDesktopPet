using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class PersonaForestTests
{
    [Fact]
    public void CloseRelationship_PrefersCareOverTechnical()
    {
        var basis = SceneCatalog.PersonaScenes[0];
        var care = basis with
        {
            CategoryGroup = DialogueCategoryGroup.DailyCare,
            Tone = "gentle",
            RelationshipProfile = "warm_friend",
            RequiredContext = ["none"]
        };
        var technical = basis with
        {
            CategoryGroup = DialogueCategoryGroup.Technical,
            Tone = "dry",
            RelationshipProfile = "neutral",
            RequiredContext = ["none"]
        };

        var careScore = PersonaForest.Adjustment(care, "她是对方的女朋友", null, null, null, "time:evening");
        var technicalScore = PersonaForest.Adjustment(technical, "她是对方的女朋友", null, null, null, "time:evening");

        Assert.True(careScore > technicalScore);
        Assert.Equal(0, PersonaForest.Adjustment(technical, "", null, null, null, "time:evening"));
    }

    [Fact]
    public void ToneSticksForOneExtraTurnAndTimeMatchAdds()
    {
        var basis = SceneCatalog.PersonaScenes[0];
        var evening = basis with
        {
            Category = DialogueCategory.DailyCare,
            Tone = "gentle",
            RequiredContext = ["time:evening"]
        };

        var continued = PersonaForest.Adjustment(
            evening,
            "",
            "gentle",
            DialogueCategory.DailyCare,
            DialogueCategory.CharacterLife,
            "time:evening");
        var alreadyLingering = PersonaForest.Adjustment(
            evening,
            "",
            "gentle",
            DialogueCategory.DailyCare,
            DialogueCategory.DailyCare,
            "time:evening");

        Assert.Equal(PersonaForest.ToneContinue + PersonaForest.TopicStick + PersonaForest.TimeMatch, continued);
        Assert.Equal(PersonaForest.ToneContinue + PersonaForest.TimeMatch, alreadyLingering);
    }

    [Fact]
    public void WarmFriendQuota_OpensWhenTheRelationshipIsClose()
    {
        var warm = SceneCatalog.PersonaScenes
            .Where(scene => scene.RelationshipProfile == "warm_friend")
            .Take(3)
            .ToArray();
        var history = new SceneHistory();
        var now = new DateTime(2026, 9, 29, 21, 0, 0, DateTimeKind.Local);
        history.Record(warm[0], now.AddMinutes(-2), warm[0].Lines[0]);
        history.Record(warm[1], now.AddMinutes(-1), warm[1].Lines[0]);

        Assert.False(history.MeetsRelationshipProfileQuota(warm[2]));
        Assert.True(history.MeetsRelationshipProfileQuota(warm[2], closeRelationship: true));
    }

    [Fact]
    public void PersonaFile_ReadsOnlyAKnownRelationship()
    {
        var directory = Path.Combine(Path.GetTempPath(), "persona-forest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "persona.json"), """{"relationship":"她是对方的女朋友"}""");
            Assert.Equal("她是对方的女朋友", PersonaRelationship.Load(directory));
            File.WriteAllText(Path.Combine(directory, "persona.json"), """{"relationship":"模型"}""");
            Assert.Equal("", PersonaRelationship.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
