using System.IO;
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
        var chat = root.Children
            .SelectMany(group => group.Children)
            .Single(category => category.Title == "主动聊天");
        Assert.Contains(chat.Children, family => family.Title == "英语");
        Assert.Contains(chat.Children, family => family.Title == "写代码");
        Assert.Contains(chat.Children, family => family.Title == "街上");
        Assert.DoesNotContain(chat.Children, family => family.Title.StartsWith("小耳饰", StringComparison.Ordinal));
        var algorithms = root.Children
            .SelectMany(group => group.Children)
            .Single(category => category.Title == "算法");
        Assert.Contains(algorithms.Children, family => family.Title == "图与搜索");
        Assert.All(root.Children.SelectMany(group => group.Children), category =>
        {
            Assert.InRange(category.Children.Length, 1, 16);
            Assert.Equal(category.Lines.Length, category.Children.Sum(family => family.Lines.Length));
            Assert.All(category.Children, family =>
            {
                Assert.InRange(family.Title.Length, 2, 8);
                Assert.Contains(family.Title, character => character is >= '\u4e00' and <= '\u9fff');
                Assert.DoesNotContain("_", family.Title);
                Assert.DoesNotContain("，", family.Title);
                Assert.DoesNotContain("。", family.Title);
                Assert.Empty(family.Children);
            });
        });
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
    public void DeveloperParameters_RejectSpeechSpeedOutsideTheHearingRange()
    {
        var parameters = DeveloperTestParameters.CreateDefault();
        parameters.SpeechSpeed = 3;

        Assert.Equal("语速要在 0.5 到 2 倍之间。", parameters.Validate());
    }

    [Fact]
    public void DeveloperParameters_SnapsAHalfwayRepetitionOntoTheSpeechStep()
    {
        var parameters = DeveloperTestParameters.CreateDefault();
        Assert.Equal(1.4, parameters.SpeechRepetition);

        var tuning = parameters.ToTuning(true) with
        {
            SpeechRepetition = 1.35,
            SpeechSpeed = 0.94
        };

        Assert.True(parameters.TryCopyTuning(tuning));
        Assert.Equal(1.4, parameters.SpeechRepetition);
        Assert.Equal(0.9, parameters.SpeechSpeed);
    }

    [Fact]
    public void DeveloperParameters_RejectReplyLengthOutsideTheReadableRange()
    {
        var parameters = DeveloperTestParameters.CreateDefault();
        parameters.ReplyMaxChars = 2000;

        Assert.Equal("输出要在 50 到 200 字之间。", parameters.Validate());
    }

    [Fact]
    public void VoiceLibrary_PublishesEveryReferenceWithItsPromptAndChineseTone()
    {
        var root = VoiceLibraryIndex.Load(RepoVoicePack());

        Assert.Equal(60, root.Clips.Length);
        Assert.Equal(root.Clips.Length, root.Clips.Select(clip => clip.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(root.Clips, clip => clip.Prompt.Contains("先送小哥哥们进场啦", StringComparison.Ordinal));
        Assert.All(root.Clips, clip =>
        {
            Assert.False(string.IsNullOrWhiteSpace(clip.Prompt));
            Assert.True(File.Exists(clip.AudioPath));
            Assert.EndsWith(".wav", clip.AudioPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(clip.ToneLabel, character => character is >= '\u4e00' and <= '\u9fff');
            Assert.DoesNotContain("_", clip.ToneLabel);
            Assert.DoesNotContain("_", clip.SituationLabel);
        });
        Assert.Contains(root.Children, child => child.Title == "平静");
        Assert.Equal(root.Clips.Length, root.Children.Sum(child => child.Clips.Length));
        Assert.All(root.Children, child => Assert.Contains(child.Title, character => character is >= '\u4e00' and <= '\u9fff'));
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

    private static string RepoVoicePack()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var pack = Path.Combine(directory.FullName, "voice", "packs", "jiayi");
            if (File.Exists(Path.Combine(pack, "manifest.json")))
            {
                return pack;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("voice/packs/jiayi was not found above the test output.");
    }

    private sealed class EndpointRandom(bool maximum) : Random
    {
        public override int Next(int minValue, int maxValue) =>
            maximum ? maxValue - 1 : minValue;
    }
}
