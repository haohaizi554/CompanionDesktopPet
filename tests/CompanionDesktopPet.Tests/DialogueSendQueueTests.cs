using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSendQueueTests
{
    [Fact]
    public void TryEnqueue_KeepsOrderUpToCapacity()
    {
        var queue = new DialogueSendQueue();

        Assert.True(queue.TryEnqueue("  第一句  "));
        Assert.True(queue.TryEnqueue("第二句"));
        Assert.False(queue.TryEnqueue("   "));

        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal("第一句", first);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal("第二句", second);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void TryEnqueue_RejectsTheNinthLineWithoutDroppingEarlierOnes()
    {
        var queue = new DialogueSendQueue();
        for (var index = 0; index < DialogueSendQueue.Capacity; index++)
        {
            Assert.True(queue.TryEnqueue($"第{index}句"));
        }

        Assert.False(queue.TryEnqueue("装不下了"));
        Assert.Equal(DialogueSendQueue.Capacity, queue.Count);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal("第0句", first);
    }
}
