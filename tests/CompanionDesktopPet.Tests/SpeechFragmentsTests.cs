using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SpeechFragmentsTests
{
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
    public void InferenceParksOnlyAfterAQuietIdleInterval()
    {
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(true, true, TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(8)));
        Assert.True(JiayiVoiceSpeaker.ShouldParkInference(true, true, TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(8)));
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(true, false, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(8)));
        Assert.False(JiayiVoiceSpeaker.ShouldParkInference(false, true, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(8)));
    }
}
