using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DeveloperModeTests
{
    [Fact]
    public void CorpusBrowser_PartitionsEveryRuntimeLineThroughEveryLevel()
    {
        var root = CorpusBrowserIndex.Root;

        Assert.Equal(PersonaCorpus.All.Count, root.Lines.Length);
        Assert.Equal(root.Lines.Length, root.Children.Sum(child => child.Lines.Length));
        Assert.Contains(root.Children, child => child.Title == "技术");
        var leaves = new List<CorpusFolder>();
        CollectLeaves(root, leaves);
        Assert.Equal(root.Lines.Length, leaves.Sum(leaf => leaf.Lines.Length));
        Assert.All(leaves, leaf => Assert.Empty(leaf.Children));
    }

    [Fact]
    public void DeveloperParameters_RejectAnInvertedInterval()
    {
        var parameters = DeveloperTestParameters.CreateDefault();
        parameters.DayMinimumMinutes = 12;
        parameters.DayMaximumMinutes = 3;

        Assert.Equal("白天的最短间隔不能大于最长间隔。", parameters.Validate());
    }

    [Fact]
    public void NextDelay_UsesHandEditedWindowsWithoutChangingTheCanonicalDefault()
    {
        var at = new DateTime(2026, 7, 26, 10, 0, 0);
        var parameters = DeveloperTestParameters.CreateDefault();
        parameters.DayMinimumMinutes = 1;
        parameters.DayMaximumMinutes = 2;
        var edited = new DialogueScheduler(new EndpointRandom(false).Next)
        {
            TestParameters = parameters
        };
        var canonical = new DialogueScheduler(new EndpointRandom(false).Next);

        Assert.Equal(TimeSpan.FromMinutes(1), edited.NextDelay(at));
        Assert.Equal(TimeSpan.FromMinutes(5), canonical.NextDelay(at));
    }

    private static void CollectLeaves(CorpusFolder folder, List<CorpusFolder> leaves)
    {
        if (folder.Children.Length == 0)
        {
            leaves.Add(folder);
            return;
        }

        foreach (var child in folder.Children)
        {
            CollectLeaves(child, leaves);
        }
    }

    private sealed class EndpointRandom(bool maximum) : Random
    {
        public override int Next(int minValue, int maxValue) =>
            maximum ? maxValue - 1 : minValue;
    }
}
