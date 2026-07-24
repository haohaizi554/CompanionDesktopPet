using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueForestTests
{
    [Fact]
    public void SchedulerTargets_MatchTheValidatedV2Configuration()
    {
        var expectedGroups = new Dictionary<DialogueCategoryGroup, double>
        {
            [DialogueCategoryGroup.Technical] = 0.18,
            [DialogueCategoryGroup.Growth] = 0.10,
            [DialogueCategoryGroup.Career] = 0.07,
            [DialogueCategoryGroup.DailyCare] = 0.10,
            [DialogueCategoryGroup.EmotionalReflection] = 0.10,
            [DialogueCategoryGroup.CharacterLife] = 0.27,
            [DialogueCategoryGroup.EasterEgg] = 0.10,
            [DialogueCategoryGroup.SystemAmbient] = 0.08
        };
        var expectedModes = new Dictionary<DialogueOutputMode, double>
        {
            [DialogueOutputMode.SelfTalk] = 0.45,
            [DialogueOutputMode.Ambient] = 0.25,
            [DialogueOutputMode.UserDirect] = 0.10,
            [DialogueOutputMode.SystemObserve] = 0.20
        };

        Assert.Equal(expectedGroups, DialogueForest.CategoryGroupWeights);
        Assert.Equal(expectedModes, DialogueForest.OutputModeTargets);
        Assert.Equal(1.0, DialogueForest.CategoryGroupWeights.Values.Sum(), 8);
        Assert.Equal(1.0, DialogueForest.OutputModeTargets.Values.Sum(), 8);
        Assert.Contains(DialogueCategoryGroup.EasterEgg, DialogueForest.BlockAdjacentCategoryGroups);
    }

    [Fact]
    public void Forest_AggregatesGroupTargetsWithoutTheLegacyTechnicalBias()
    {
        Assert.Equal(0.18, DialogueForest.TreeWeights[DialogueTreeKind.Technical], 8);
        Assert.Equal(0.17, DialogueForest.TreeWeights[DialogueTreeKind.Growth], 8);
        Assert.Equal(0.38, DialogueForest.TreeWeights[DialogueTreeKind.Companion], 8);
        Assert.Equal(0.27, DialogueForest.TreeWeights[DialogueTreeKind.Life], 8);
        Assert.True(DialogueForest.TreeWeights[DialogueTreeKind.Technical] <
                    DialogueForest.TreeWeights[DialogueTreeKind.Life]);
    }

    [Fact]
    public void Forest_CoversEveryEnabledCorpusCategory()
    {
        var covered = DialogueForest.Trees.SelectMany(tree => tree.Categories).ToHashSet();

        Assert.All(PersonaCorpus.Regular, line => Assert.Contains(line.Category, covered));
        Assert.Equal(covered.Count, DialogueForest.Trees.SelectMany(tree => tree.Categories).Distinct().Count());
    }

    [Theory]
    [InlineData(CompanionEvent.AnimationPaused, DialogueTreeKind.Companion)]
    [InlineData(CompanionEvent.SizeChanged, DialogueTreeKind.Life)]
    [InlineData(CompanionEvent.DragReleased, DialogueTreeKind.Life)]
    [InlineData(CompanionEvent.AnimationResumed, DialogueTreeKind.Growth)]
    public void SelectTree_KeepsConcreteDesktopEventRouting(
        CompanionEvent trigger,
        DialogueTreeKind expected)
    {
        Assert.Equal(expected, DialogueForest.SelectTree(trigger, null, 0, new Random(42)).Kind);
    }
}
