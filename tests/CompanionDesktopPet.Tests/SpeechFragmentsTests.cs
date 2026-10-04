using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SpeechFragmentsTests
{
    [Fact]
    public void AReplyDropsALeadingCopyOfThePreviousSentence()
    {
        const string prior = "我先醒醒，马上就好。";

        Assert.Equal(
            "窗外的光挺亮堂，看着就让人心情舒展。",
            SpeechFragments.DropLeadingRepeat(
                "我先醒醒，马上就好。窗外的光挺亮堂，看着就让人心情舒展。",
                prior));
        Assert.Equal(string.Empty, SpeechFragments.DropLeadingRepeat(prior, prior));
        Assert.Equal("你先坐一会儿。", SpeechFragments.DropLeadingRepeat("你先坐一会儿。", prior));
        const string copied = "你来了，我刚把这页翻过去。今天过得怎么样，有没有遇到让你开心的小事？";
        Assert.Equal(string.Empty, SpeechFragments.DropLeadingRepeat(copied, copied));
        Assert.Equal(
            "你敲这些数字，是想让我歇会儿吗？",
            SpeechFragments.DropLeadingRepeat(copied + "你敲这些数字，是想让我歇会儿吗？", copied));
    }

    [Fact]
    public void OneSentenceStaysWhole()
    {
        var pieces = SpeechFragments.SplitForSpeech("你先把杯子放下。");

        Assert.Equal(["你先把杯子放下。"], pieces);
    }

    [Fact]
    public void LaterSentencesWaitBehindTheFirst()
    {
        var pieces = SpeechFragments.SplitForSpeech("你先坐下。杯子在左边。别急着站起来。");

        Assert.Equal(["你先坐下。", "杯子在左边。", "别急着站起来。"], pieces);
    }

    [Fact]
    public void StageDirectionsAreNotSpoken()
    {
        var pieces = SpeechFragments.SplitForSpeech("我在呢。（轻轻笑）*waves*");

        Assert.Equal(["我在呢。"], pieces);
    }

    [Fact]
    public void AShortTailStaysWithThePreviousSentence()
    {
        var pieces = SpeechFragments.SplitForSpeech("你先坐下。嗯。");

        Assert.Equal(["你先坐下。嗯。"], pieces);
    }

    [Fact]
    public void ALongReplyKeepsTheFirstSentencesAndTheRestAsTheLastPiece()
    {
        var sentences = Enumerable.Range(1, 10).Select(index => $"这是第{index}句。");
        var pieces = SpeechFragments.SplitForSpeech(string.Concat(sentences));

        Assert.Equal(8, pieces.Count);
        Assert.Equal("这是第1句。", pieces[0]);
        Assert.EndsWith("这是第10句。", pieces[7]);
    }

    [Fact]
    public void SpokenPiecesOfALongReplyStillBelongToIt()
    {
        var whole = "你先坐下。杯子在左边。别急着站起来。";

        Assert.True(SpeechFragments.BelongsTo("杯子在左边。", whole));
        Assert.True(SpeechFragments.BelongsTo(whole, whole));
        Assert.False(SpeechFragments.BelongsTo("另一句完全无关。", whole));
    }

    [Fact]
    public void InferenceStaysLoadedUntilVoiceIsTurnedOff()
    {
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(true, true, TimeSpan.FromHours(12), TimeSpan.FromHours(24)));
        Assert.True(JiayiVoiceSpeaker.ShouldParkInference(true, true, TimeSpan.Zero, TimeSpan.Zero));
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(true, false, TimeSpan.FromMinutes(30), TimeSpan.Zero));
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(false, true, TimeSpan.FromMinutes(30), TimeSpan.Zero));
    }
}
