using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class DialogueWarmupTests
{
    private static readonly TimeSpan BlockingCallTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTime LocalNow =
        new(2026, 7, 24, 9, 30, 0, DateTimeKind.Local);

    [Fact]
    [Trait("Category", "Performance")]
    public async Task WarmupAsync_ConcurrentCallersInitializeExactlyOnceWhileRepliesStayImmediate()
    {
        var factoryEntered = new ManualResetEventSlim();
        var releaseFactory = new ManualResetEventSlim();
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
        var warmupsCompleted = Task.WhenAll(warmups);
        _ = warmupsCompleted.ContinueWith(
            _ =>
            {
                factoryEntered.Dispose();
                releaseFactory.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(2)));
            var fallbackCall = Task.Run(() =>
                service.GetReply(CompanionEvent.Click, LocalNow, new Random(7)));

            var fallback = await fallbackCall.WaitAsync(BlockingCallTimeout);

            Assert.StartsWith("fallback:", fallback.SceneId, StringComparison.Ordinal);
            Assert.True(fallback.ShouldDisplayText);
            Assert.InRange(fallback.Text.Length, 1, 18);
            Assert.False(service.IsReady);
            Assert.All(warmups, warmup => Assert.False(warmup.IsCompleted));
            Assert.Equal(1, Volatile.Read(ref factoryCalls));
        }
        finally
        {
            releaseFactory.Set();
        }

        Assert.All(
            await warmupsCompleted.WaitAsync(TimeSpan.FromSeconds(5)),
            Assert.True);

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
        state.ActiveStories = [new StoryProgress("pending-story", 2, dueAt)];
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
    public async Task DeferredService_DetachesInitialReturnedAndFactorySnapshots()
    {
        var state = CharacterState.Create(LocalNow);
        var dueAt = LocalNow.AddMinutes(20);
        state.ActiveStories = [new StoryProgress("pending-story", 2, dueAt)];
        var history = new List<SceneHistoryEntry>
        {
            new("scene", "semantic", LocalNow, "line")
        };
        var recentLines = new List<string> { "previous line" };
        var initial = new AgentMemorySnapshot(
            state,
            history,
            23,
            DialogueCategory.Python,
            recentLines);
        AgentMemorySnapshot? factorySnapshot = null;
        var service = DialogueService.CreateDeferred(
            initial,
            restored =>
            {
                factorySnapshot = restored;
                return new FixedAgent(restored, "ready");
            });

        initial.State.ActiveStories = [];
        history.Clear();
        recentLines.Clear();
        var first = service.CreateSnapshot();
        first.State.ActiveStories = [];
        Assert.IsType<SceneHistoryEntry[]>(first.History)[0] = new(
            "mutated", "mutated", LocalNow, "mutated");
        Assert.IsType<string[]>(first.RecentLines)[0] = "mutated";

        var second = service.CreateSnapshot();
        Assert.Equal(dueAt, service.NextStoryDueAt);
        Assert.Single(second.State.ActiveStories);
        Assert.Single(second.History);
        Assert.Single(second.RecentLines);

        Assert.True(await service.WarmupAsync());
        Assert.NotNull(factorySnapshot);
        Assert.Single(factorySnapshot!.State.ActiveStories);
        Assert.Single(factorySnapshot.History);
        Assert.Single(factorySnapshot.RecentLines);
        Assert.NotSame(second.State, factorySnapshot.State);

        var readyFirst = service.CreateSnapshot();
        readyFirst.State.ActiveStories = [];
        Assert.IsType<SceneHistoryEntry[]>(readyFirst.History)[0] = new(
            "ready-mutated", "ready-mutated", LocalNow, "ready-mutated");
        Assert.IsType<string[]>(readyFirst.RecentLines)[0] = "ready-mutated";
        var readySecond = service.CreateSnapshot();

        Assert.Single(readySecond.State.ActiveStories);
        Assert.Equal("scene", Assert.Single(readySecond.History).SceneId);
        Assert.Equal("previous line", Assert.Single(readySecond.RecentLines));
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
        compatible.State.ActiveStories = [
            .. compatible.State.ActiveStories,
            new StoryProgress(
                arc.Id,
                1,
                LocalNow.AddHours(4))
        ];
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

    [Fact]
    public async Task GetReply_SerializesConcurrentCallsIntoTheMutableAgent()
    {
        var agent = new ConcurrentCallDetectingAgent();
        var service = DialogueService.CreateDeferred(_ => agent);
        Assert.True(await service.WarmupAsync());
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callers = Enumerable.Range(0, 24)
            .Select(index => Task.Run(async () =>
            {
                await start.Task;
                return service.GetReply(
                    CompanionEvent.Click,
                    LocalNow.AddSeconds(index),
                    new Random(index));
            }))
            .ToArray();

        start.SetResult();
        var replies = await Task.WhenAll(callers);

        Assert.All(replies, reply => Assert.True(reply.ShouldDisplayText));
        Assert.Equal(1, agent.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task WarmupAsync_CorruptCatalogDoesNotReportReadyAndFallbackReplyRemainsAvailable()
    {
        var originalFailure = new InvalidDataException("corrupt persona corpus");
        var fallback = SceneCatalog.BuildPersonaScenes(FallbackDialogueCatalog.All);
        var service = DialogueService.CreateDeferred(_ =>
        {
            var agent = new OfflineCompanionAgent(() => new SceneCatalogLoadResult(fallback, originalFailure));
            agent.WarmUp();
            return agent;
        });

        Assert.False(await service.WarmupAsync());
        Assert.False(service.IsReady);
        var warmupFailure = Assert.IsType<InvalidDataException>(service.LastWarmupException);
        Assert.Equal(
            "The validated v2 persona corpus is unavailable; degraded dialogue cannot report ready.",
            warmupFailure.Message);
        Assert.Same(originalFailure, warmupFailure.InnerException);

        var reply = service.GetReply(CompanionEvent.Click, LocalNow, new Random(20260727));
        Assert.StartsWith("fallback:", reply.SceneId, StringComparison.Ordinal);
        Assert.True(reply.ShouldDisplayText);
        Assert.NotNull(reply.SourceLine);
    }

    [Fact]
    public async Task BlockedAgentResponse_DoesNotBlockServiceStateAndSerializesAllAgentOperations()
    {
        using var agent = new BlockingOperationAgent();
        var service = DialogueService.CreateDeferred(_ => agent);
        Assert.True(await service.WarmupAsync());

        var response = Task.Run(() => service.GetReply(
            CompanionEvent.Click,
            LocalNow,
            new Random(20260727)));
        Assert.True(agent.ResponseEntered.Wait(TimeSpan.FromSeconds(2)));

        var serviceState = await Task.Run(() =>
            (service.IsReady, service.LastWarmupException)).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(serviceState.IsReady);
        Assert.Null(serviceState.LastWarmupException);

        var snapshot = Task.Run(service.CreateSnapshot);
        var dueAt = Task.Run(() => service.NextStoryDueAt);
        Assert.NotSame(snapshot, await Task.WhenAny(snapshot, Task.Delay(100)));
        Assert.NotSame(dueAt, await Task.WhenAny(dueAt, Task.Delay(100)));

        agent.ReleaseResponse.Set();
        var reply = await response.WaitAsync(BlockingCallTimeout);
        _ = await snapshot.WaitAsync(BlockingCallTimeout);
        _ = await dueAt.WaitAsync(BlockingCallTimeout);

        Assert.True(reply.ShouldDisplayText);
        Assert.Equal(1, agent.MaximumConcurrentOperations);
        Assert.Equal(3, agent.OperationCount);
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

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen) =>
            new(
                _text,
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "full:test",
                SemanticGroup: "full.test");
    }

    private sealed class ConcurrentCallDetectingAgent : ICompanionDialogueAgent
    {
        private int _activeCalls;
        private int _maximumConcurrentCalls;

        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        public DateTime? NextStoryDueAt => null;

        public AgentMemorySnapshot CreateSnapshot() => new(
            CharacterState.Create(LocalNow),
            [],
            0,
            null,
            []);

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(active);
            try
            {
                Thread.Sleep(15);
                return new AgentReply(
                    "并发也要排好队。",
                    DialogueCategory.CharacterLife,
                    DialogueTreeKind.Companion,
                    trigger,
                    SceneId: "full:concurrency",
                    SemanticGroup: "full.concurrency");
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maximumConcurrentCalls);
                if (active <= observed
                    || Interlocked.CompareExchange(ref _maximumConcurrentCalls, active, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class BlockingOperationAgent : ICompanionDialogueAgent, IDisposable
    {
        private int _activeOperations;
        private int _maximumConcurrentOperations;
        private int _operationCount;

        public ManualResetEventSlim ResponseEntered { get; } = new();

        public ManualResetEventSlim ReleaseResponse { get; } = new();

        public int MaximumConcurrentOperations => Volatile.Read(ref _maximumConcurrentOperations);

        public int OperationCount => Volatile.Read(ref _operationCount);

        public DateTime? NextStoryDueAt
        {
            get
            {
                ExecuteOperation();
                return LocalNow.AddMinutes(30);
            }
        }

        public AgentMemorySnapshot CreateSnapshot()
        {
            ExecuteOperation();
            return new AgentMemorySnapshot(
                CharacterState.Create(LocalNow),
                [],
                0,
                null,
                []);
        }

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen)
        {
            ExecuteOperation(
                () =>
                {
                    ResponseEntered.Set();
                    ReleaseResponse.Wait(BlockingCallTimeout);
                });
            return new AgentReply(
                "blocked response released",
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "full:blocked",
                SemanticGroup: "full.blocked");
        }

        public void Dispose()
        {
            ResponseEntered.Dispose();
            ReleaseResponse.Dispose();
        }

        private void ExecuteOperation(Action? operation = null)
        {
            var active = Interlocked.Increment(ref _activeOperations);
            UpdateMaximum(active);
            Interlocked.Increment(ref _operationCount);
            try
            {
                operation?.Invoke();
                Thread.Sleep(20);
            }
            finally
            {
                Interlocked.Decrement(ref _activeOperations);
            }
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maximumConcurrentOperations);
                if (active <= observed
                    || Interlocked.CompareExchange(ref _maximumConcurrentOperations, active, observed) == observed)
                {
                    return;
                }
            }
        }
    }
}
