using System.Diagnostics;
using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueWarmupTests
{
    private static readonly DateTime LocalNow =
        new(2026, 7, 24, 9, 30, 0, DateTimeKind.Local);

    [Fact]
    public async Task WarmupAsync_ConcurrentCallersInitializeExactlyOnceWhileRepliesStayImmediate()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var factoryCalls = 0;
        var service = DialogueService.CreateDeferred(
            snapshot =>
            {
                Interlocked.Increment(ref factoryCalls);
                factoryEntered.Set();
                releaseFactory.Wait();
                return new FixedAgent(snapshot, "全量文库已经醒啦。");
            });

        var warmups = Enumerable.Range(0, 12)
            .Select(_ => service.WarmupAsync())
            .ToArray();
        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(2)));

        var stopwatch = Stopwatch.StartNew();
        var fallback = service.GetReply(CompanionEvent.Click, LocalNow, new Random(7));
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        Assert.StartsWith("fallback:", fallback.SceneId, StringComparison.Ordinal);
        Assert.True(fallback.ShouldDisplayText);
        Assert.InRange(fallback.Text.Length, 1, 18);
        Assert.False(service.IsReady);
        Assert.Equal(1, Volatile.Read(ref factoryCalls));

        releaseFactory.Set();
        Assert.All(await Task.WhenAll(warmups), Assert.True);

        Assert.True(service.IsReady);
        Assert.Equal(1, Volatile.Read(ref factoryCalls));
        var full = service.GetReply(CompanionEvent.Click, LocalNow, new Random(8));
        Assert.Equal("full:test", full.SceneId);
        Assert.Equal("全量文库已经醒啦。", full.Text);
    }

    [Fact]
    public async Task WarmupAsync_FailureKeepsFallbackSafeAndNextAttemptCanRecover()
    {
        var factoryCalls = 0;
        var service = DialogueService.CreateDeferred(snapshot =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                throw new InvalidDataException("broken corpus");
            }

            return new FixedAgent(snapshot, "重试好了。");
        });

        Assert.False(await service.WarmupAsync());
        Assert.False(service.IsReady);
        Assert.IsType<InvalidDataException>(service.LastWarmupException);
        Assert.StartsWith(
            "fallback:",
            service.GetReply(CompanionEvent.Click, LocalNow, new Random(1)).SceneId,
            StringComparison.Ordinal);

        Assert.True(await service.WarmupAsync());
        Assert.True(service.IsReady);
        Assert.Null(service.LastWarmupException);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(
            "重试好了。",
            service.GetReply(CompanionEvent.Click, LocalNow, new Random(2)).Text);
    }

    [Fact]
    public void DeferredService_ReadsDueStoryAndSnapshotWithoutTouchingBlockedFactory()
    {
        var state = CharacterState.Create(LocalNow);
        var dueAt = LocalNow.AddMinutes(20);
        state.ActiveStories.Add(new StoryProgress("pending-story", 2, dueAt));
        var snapshot = new AgentMemorySnapshot(
            state,
            [],
            23,
            DialogueCategory.Python,
            ["上一句"]);
        var factoryCalls = 0;
        var service = DialogueService.CreateDeferred(
            snapshot,
            restored =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new FixedAgent(restored, "不该在读取时初始化");
            });

        var restored = service.CreateSnapshot();

        Assert.Equal(dueAt, service.NextStoryDueAt);
        Assert.Equal(23, restored.TurnCount);
        Assert.Equal(DialogueCategory.Python, restored.LastCategory);
        Assert.Equal(["上一句"], restored.RecentLines);
        Assert.Equal(0, factoryCalls);
        Assert.False(service.IsReady);
    }

    [Fact]
    public async Task Warmup_DefaultFactoryDropsCatalogIncompatibleDeferredMemory()
    {
        var state = CharacterState.Create(LocalNow);
        var incompatible = new AgentMemorySnapshot(
            state,
            [
                new SceneHistoryEntry(
                    "retired-scene",
                    "retired.semantic",
                    LocalNow,
                    "旧版本的一句话。",
                    "retired-line",
                    DialogueCategory.CharacterLife,
                    DialogueCategoryGroup.CharacterLife,
                    DialogueOutputMode.SelfTalk,
                    DialogueTrigger.Any,
                    0,
                    DateOnly.FromDateTime(LocalNow))
            ],
            1,
            DialogueCategory.CharacterLife,
            ["旧版本的一句话。"]);
        var service = DialogueService.CreateDeferred(incompatible);

        Assert.True(await service.WarmupAsync());

        var recovered = service.CreateSnapshot();
        Assert.Empty(recovered.History);
        Assert.Empty(recovered.RecentLines);
        Assert.Equal(incompatible.State.Energy, recovered.State.Energy);
        Assert.Equal(incompatible.State.InstalledAt, recovered.State.InstalledAt);
        Assert.Equal(1, recovered.TurnCount);
        Assert.Equal(DialogueCategory.CharacterLife, recovered.LastCategory);
    }

    [Fact]
    public async Task Warmup_DefaultFactoryPreservesCompatibleStateStoriesHistoryAndRecentLines()
    {
        var agent = new OfflineCompanionAgent();
        agent.Respond(CompanionEvent.Click, LocalNow, new Random(20260724));
        var compatible = agent.CreateSnapshot();
        var arc = StoryArcCatalog.All[0];
        compatible.State.ActiveStories.Add(new StoryProgress(
            arc.Id,
            1,
            LocalNow.AddHours(4)));
        var service = DialogueService.CreateDeferred(compatible);

        Assert.True(await service.WarmupAsync());

        var recovered = service.CreateSnapshot();
        Assert.Equal(compatible.TurnCount, recovered.TurnCount);
        Assert.Equal(compatible.LastCategory, recovered.LastCategory);
        Assert.Equal(compatible.History, recovered.History);
        Assert.Equal(compatible.RecentLines, recovered.RecentLines);
        Assert.Equal(compatible.State.Energy, recovered.State.Energy);
        Assert.Equal(compatible.State.ActiveStories, recovered.State.ActiveStories);
    }

    private sealed class FixedAgent : ICompanionDialogueAgent
    {
        private readonly AgentMemorySnapshot _snapshot;
        private readonly string _text;

        public FixedAgent(AgentMemorySnapshot? snapshot, string text)
        {
            _snapshot = snapshot ?? new AgentMemorySnapshot(
                CharacterState.Create(LocalNow),
                [],
                0,
                null,
                []);
            _text = text;
        }

        public DateTime? NextStoryDueAt => _snapshot.State.ActiveStories.Count == 0
            ? null
            : _snapshot.State.ActiveStories.Min(story => story.DueAt);

        public AgentMemorySnapshot CreateSnapshot() => _snapshot;

        public AgentReply Respond(CompanionEvent trigger, DateTime localTime, Random random) =>
            new(
                _text,
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "full:test",
                SemanticGroup: "full.test");
    }
}
