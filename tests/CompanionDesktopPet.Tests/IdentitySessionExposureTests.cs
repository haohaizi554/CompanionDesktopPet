using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class IdentitySessionExposureTests
{
    [Fact]
    public void SameIdentitySemanticGroup_RequiresInterveningBubblesAndThenLeavesRecentWindow()
    {
        var exposure = new IdentitySessionExposure();
        var identity = AuthoredDirectLine("identity.same", "identity.same.group", "\u5c0f\u73a5\uff0c\u4eca\u5929\u8fd8\u633a\u4e0d\u9519\u7684\u3002", "b084");

        exposure.Record(identity);
        exposure.Record(Line("neutral.1", "neutral.1.group", "\u5148\u628a\u773c\u524d\u7684\u4e8b\u505a\u5b8c\u3002"));
        exposure.Record(Line("neutral.2", "neutral.2.group", "\u7f13\u4e00\u7f13\uff0c\u4e0d\u6025\u3002"));

        Assert.False(exposure.MeetsMinimumInterveningBubbles(identity.SemanticGroup));
        Assert.False(exposure.IsEligible(identity));

        exposure.Record(Line("neutral.3", "neutral.3.group", "\u8fd9\u4e00\u6b65\u5df2\u7ecf\u5f88\u597d\u4e86\u3002"));

        Assert.True(exposure.MeetsMinimumInterveningBubbles(identity.SemanticGroup));
        Assert.False(exposure.IsEligible(identity));

        for (var index = 4; index <= 8; index++)
        {
            exposure.Record(Line($"neutral.{index}", $"neutral.{index}.group", $"\u666e\u901a\u6c14\u6ce1 {index}\u3002"));
        }

        Assert.True(exposure.IsEligible(identity));
    }

    [Fact]
    public void DirectMarkerClass_IsLimitedPerSessionWithoutBlockingMarkerFreeLore()
    {
        var exposure = new IdentitySessionExposure();
        for (var index = 0; index < PersonaContractGenerated.AuthoredIdentity.DirectMarkerMaxPerIdentityClass; index++)
        {
            exposure.Record(AuthoredDirectLine(
                $"identity.direct.{index}",
                $"identity.direct.group.{index}",
                "\u73a5\u4ed4\uff0c\u4f60\u81ea\u5df1\u770b\u7740\u529e\u54af\u3002",
                "b085"));
        }

        var fourthDirectUse = AuthoredDirectLine(
            "identity.direct.fourth",
            "identity.direct.group.fourth",
            "\u73a5\u4ed4\uff0c\u8fd9\u6b21\u53ef\u4e0d\u8bb8\u6478\u9c7c\u3002",
            "b085");
        var markerFreeLore = Line(
            "identity.lore.marker-free",
            "identity.direct.group.2",
            "\u8fd9\u4e2a\u5c0f\u5267\u60c5\u8fd8\u6709\u540e\u7eed\u3002");

        Assert.False(exposure.IsEligible(fourthDirectUse));
        Assert.True(exposure.IsEligible(AuthoredDirectLine(
            "identity.direct.other-class",
            "identity.direct.group.other-class",
            "\u73a5\u73a5\uff0c\u8fd9\u4e2a\u540d\u989d\u8fd8\u6ca1\u7528\u5462\u3002",
            "b086")));
        Assert.True(exposure.IsEligible(markerFreeLore));

        var restartedSession = new IdentitySessionExposure();
        Assert.True(restartedSession.IsEligible(fourthDirectUse));
    }

    [Fact]
    public void LegacyEditorialMarker_DoesNotConsumeTheAuthoredDirectMarkerSessionBudget()
    {
        var exposure = new IdentitySessionExposure();
        var legacy = Line(
            "legacy.identity",
            "legacy.identity.group",
            "\u73a5\u4ed4\u53ea\u662f\u4e00\u53e5\u65e7\u5f0f\u7f16\u8f91\u5f69\u86cb\u3002");

        for (var index = 0; index < 4; index++)
        {
            exposure.Record(legacy with { Id = $"legacy.identity.{index}" });
        }

        Assert.True(exposure.IsEligible(legacy));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void RecentEightWindow_BlocksEveryRetainedPositionAndReleasesAfterTheNinthBubble(
        int fillerCount,
        bool expectedEligible)
    {
        var exposure = new IdentitySessionExposure();
        var identity = AuthoredDirectLine(
            "identity.window",
            "identity.window.group",
            "\u5c0f\u73a5\uff0c\u8fd9\u4e2a\u7a97\u53e3\u8fd8\u5728\u3002",
            "b084");
        exposure.Record(identity);
        for (var index = 0; index < fillerCount; index++)
        {
            exposure.Record(Line(
                $"window.filler.{index}",
                $"window.filler.group.{index}",
                $"\u7a97\u53e3\u586b\u5145 {index}\u3002"));
        }

        Assert.Equal(expectedEligible, exposure.IsEligible(identity));
    }

    [Fact]
    public void SchedulerFallbacks_ApplyTheSharedLineEligibilityPredicate()
    {
        var now = new DateTime(2026, 7, 28, 15, 0, 0, DateTimeKind.Local);
        var line = AuthoredDirectLine("identity.scheduler", "identity.scheduler.group", "\u5c0f\u73a5\uff0c\u8fd9\u53e5\u8fd8\u5f97\u8f6e\u5230\u5b83\u624d\u884c\u3002", "b084");
        var scene = SceneCatalog.CreateScene("identity-scheduler", [line]);
        var scheduler = new SceneScheduler();
        var history = new SceneHistory();
        var context = new SceneContext(CompanionEvent.Automatic, now, CharacterState.Create(now));

        Assert.Null(scheduler.SelectSafeFeedback(
            [scene],
            context,
            history,
            new Random(1),
            lineEligibility: _ => false));
        Assert.Null(scheduler.SelectReusableClickFallback(
            [scene],
            history,
            new Random(2),
            lineEligibility: _ => false));
    }

    [Fact]
    public void SchedulerNormalAndClickFallbacks_DoNotBypassTheSharedLineEligibilityPredicate()
    {
        var now = new DateTime(2026, 7, 28, 15, 0, 0, DateTimeKind.Local);
        var context = new SceneContext(CompanionEvent.Click, now, CharacterState.Create(now));
        var scheduler = new SceneScheduler();

        Assert.NotNull(scheduler.Select(
            context,
            new SceneHistory(),
            new Random(3),
            bypassInterruptionBudget: true));
        Assert.Null(scheduler.Select(
            context,
            new SceneHistory(),
            new Random(3),
            bypassInterruptionBudget: true,
            lineEligibility: _ => false));
        Assert.NotNull(scheduler.SelectClickFallback(
            context,
            new SceneHistory(),
            new Random(4)));
        Assert.Null(scheduler.SelectClickFallback(
            context,
            new SceneHistory(),
            new Random(4),
            lineEligibility: _ => false));
    }

    private static DialogueLine Line(string id, string semanticGroup, string text)
    {
        var basis = PersonaCorpus.All.First(line =>
            line.Enabled
            && !line.RequiresReply
            && !line.HasSeasoningMarker
            && line.CategoryGroup == DialogueCategoryGroup.CharacterLife
            && line.Tone != "dry_sharp"
            && line.OutputMode != DialogueOutputMode.UserDirect
            && line.Trigger == DialogueTrigger.Any
            && line.RequiredContext.SequenceEqual(["none"]));
        return basis with
        {
            Id = id,
            TopicId = id + ".topic",
            SemanticGroup = semanticGroup,
            Text = text,
            MaxPerDay = 99,
            CooldownHours = 0,
            SemanticCooldownHours = 0,
            Weight = 1
        };
    }

    private static DialogueLine AuthoredDirectLine(
        string id,
        string semanticGroup,
        string text,
        string batchId) =>
        Line(id, semanticGroup, text) with
        {
            SourceKind = "curated_authored",
            SourceReference = $"catalog:authored-v1:{batchId};variant:{id}"
        };
}
