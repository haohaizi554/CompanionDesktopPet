using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueWarmupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_TransientFailuresUseExactBoundedBackoffAndOneSharedRun()
    {
        var factoryCalls = 0;
        var delays = new List<TimeSpan>();
        var dialogue = DialogueService.CreateDeferred(snapshot =>
        {
            if (Interlocked.Increment(ref factoryCalls) <= 3)
            {
                throw new TimeoutException("temporary corpus access");
            }

            return new FixedAgent(snapshot);
        });
        var coordinator = new DialogueWarmupCoordinator(
            dialogue,
            delayAsync: (delay, _) =>
            {
                lock (delays)
                {
                    delays.Add(delay);
                }

                return Task.CompletedTask;
            });

        var runs = Enumerable.Range(0, 12)
            .Select(_ => coordinator.StartAsync(CancellationToken.None))
            .ToArray();

        Assert.All(runs, run => Assert.Same(runs[0], run));
        Assert.All(await Task.WhenAll(runs), result =>
            Assert.Equal(DialogueWarmupOutcome.Ready, result));
        Assert.Equal(4, factoryCalls);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
            delays);
        Assert.Null(coordinator.LastError);
    }

    [Fact]
    public async Task StartAsync_DeterministicFailureStopsWithoutRetryAndRecordsCause()
    {
        var factoryCalls = 0;
        var delayCalls = 0;
        var dialogue = DialogueService.CreateDeferred(_ =>
        {
            factoryCalls++;
            throw new InvalidDataException("invalid embedded corpus");
        });
        var coordinator = new DialogueWarmupCoordinator(
            dialogue,
            delayAsync: (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(DialogueWarmupOutcome.PermanentFailure, result);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, delayCalls);
        Assert.IsType<InvalidDataException>(coordinator.LastError);
        Assert.Same(
            coordinator.StartAsync(CancellationToken.None),
            coordinator.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_TransientFailureExhaustsOnlyThreeRetries()
    {
        var factoryCalls = 0;
        var delays = new List<TimeSpan>();
        var dialogue = DialogueService.CreateDeferred(_ =>
        {
            factoryCalls++;
            throw new IOException("temporary read failure");
        });
        var coordinator = new DialogueWarmupCoordinator(
            dialogue,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(DialogueWarmupOutcome.RetriesExhausted, result);
        Assert.Equal(4, factoryCalls);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
            delays);
        Assert.IsType<IOException>(coordinator.LastError);
    }

    [Fact]
    public async Task StartAsync_CancellationStopsPendingBackoffWithoutAnotherFactoryCall()
    {
        var delayEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var dialogue = DialogueService.CreateDeferred(_ =>
        {
            factoryCalls++;
            throw new TimeoutException("temporary");
        });
        var coordinator = new DialogueWarmupCoordinator(
            dialogue,
            delayAsync: async (_, cancellationToken) =>
            {
                delayEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        using var lifetime = new CancellationTokenSource();

        var run = coordinator.StartAsync(lifetime.Token);
        await delayEntered.Task;
        lifetime.Cancel();

        Assert.Equal(DialogueWarmupOutcome.Cancelled, await run);
        Assert.Equal(1, factoryCalls);
    }

    private sealed class FixedAgent : ICompanionDialogueAgent
    {
        private readonly AgentMemorySnapshot _snapshot;

        public FixedAgent(AgentMemorySnapshot? snapshot) =>
            _snapshot = snapshot ?? new AgentMemorySnapshot(
                CharacterState.Create(new DateTime(2026, 7, 25, 9, 0, 0)),
                [],
                0,
                null,
                []);

        public DateTime? NextStoryDueAt => null;

        public AgentMemorySnapshot CreateSnapshot() => _snapshot;

        public AgentReply Respond(CompanionEvent trigger, DateTime localTime, Random random) =>
            new(
                "全量回复",
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "full:test",
                SemanticGroup: "full.test");
    }
}
