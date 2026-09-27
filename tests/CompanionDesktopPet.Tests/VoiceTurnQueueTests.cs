using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class VoiceTurnQueueTests
{
    [Fact]
    public void NewLinesWaitBehindTheOneAlreadySynthesizing()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("第一句"));
        queue.Enqueue(Turn("第二句"));

        var started = queue.TryStartSynthesis();
        Assert.Equal("第一句", started?.Text);
        Assert.Null(queue.TryStartSynthesis());

        queue.CompleteSynthesis("1.wav");
        var playing = queue.TryStartPlayback();
        Assert.Equal("1.wav", playing?.Path);
        Assert.Equal("第二句", queue.TryStartSynthesis()?.Text);
    }

    [Fact]
    public void FinishedClipWaitsUntilPlaybackEnds()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("第一句"));
        queue.Enqueue(Turn("第二句"));
        queue.TryStartSynthesis();
        queue.CompleteSynthesis("1.wav");
        queue.TryStartPlayback();
        queue.TryStartSynthesis();
        queue.CompleteSynthesis("2.wav");

        Assert.Null(queue.TryStartPlayback());
        Assert.Null(queue.TryStartSynthesis());

        queue.CompletePlayback();
        Assert.Equal("2.wav", queue.TryStartPlayback()?.Path);
        Assert.True(queue.IsIdle is false);
    }

    [Fact]
    public void ClearDropsTheQueueWithoutLeavingABusySlot()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("第一句"));
        queue.Enqueue(Turn("第二句"));
        queue.TryStartSynthesis();
        queue.Clear();

        Assert.True(queue.IsIdle);
        Assert.Null(queue.TryStartSynthesis());
    }

    [Fact]
    public void QueueKeepsTheNewestLinesWhenItOverflows()
    {
        var queue = new VoiceTurnQueue();
        for (var index = 0; index < VoiceTurnQueue.Capacity + 2; index++)
        {
            queue.Enqueue(Turn($"第{index}句"));
        }

        var first = queue.TryStartSynthesis();
        Assert.Equal("第2句", first?.Text);
    }

    [Fact]
    public void AClickSkipsLinesThatHaveNotStarted()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("自动"));
        queue.Enqueue(Turn("再一句"));
        queue.TryStartSynthesis();
        queue.Enqueue(new VoiceTurn("你点的", "calm", null, Urgent: true));
        queue.Enqueue(new VoiceTurn("又点一下", "calm", null, Urgent: true));

        queue.CompleteSynthesis("1.wav");
        queue.TryStartPlayback();
        Assert.Equal("你点的", queue.TryStartSynthesis()?.Text);
        queue.CompleteSynthesis("2.wav");
        queue.CompletePlayback();
        queue.TryStartPlayback();
        Assert.Equal("又点一下", queue.TryStartSynthesis()?.Text);
    }

    [Fact]
    public void FailedLineReturnsToTheFrontOnce()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("第一句"));
        queue.Enqueue(Turn("第二句"));
        queue.TryStartSynthesis();

        var failed = queue.FailSynthesis();
        Assert.Equal("第一句", failed?.Text);
        queue.EnqueueFront(failed!.Value);

        Assert.Equal("第一句", queue.TryStartSynthesis()?.Text);
    }

    [Fact]
    public void AFailedAmbientLineStaysBehindAClick()
    {
        var queue = new VoiceTurnQueue();
        queue.Enqueue(Turn("自动"));
        queue.TryStartSynthesis();
        queue.Enqueue(new VoiceTurn("你点的", "calm", null, Urgent: true));

        queue.EnqueueFront(queue.FailSynthesis()!.Value);

        Assert.Equal("你点的", queue.TryStartSynthesis()?.Text);
    }

    private static VoiceTurn Turn(string text) => new(text, "calm", null);
}
