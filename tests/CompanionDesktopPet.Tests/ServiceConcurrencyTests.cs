using System.Collections;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class ServiceConcurrencyTests
{
    private static readonly TimeSpan ConcurrencyTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ContentionProbe = TimeSpan.FromMilliseconds(250);
    private static readonly DateTime LocalNow =
        new(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public async Task OfflineAgent_DirectRespondWaitsForTheInFlightStateTransition()
    {
        _ = SceneCatalog.PersonaScenes.Count;
        var agent = new OfflineCompanionAgent();
        var firstRandom = new GateRandom();
        var secondRandom = new SignalRandom();

        var first = Task.Run(() => agent.Respond(
            CompanionEvent.Click,
            LocalNow,
            firstRandom));
        await firstRandom.Entered.WaitAsync(ConcurrencyTimeout);

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(() =>
        {
            secondStarted.TrySetResult();
            return agent.Respond(
                CompanionEvent.Click,
                LocalNow.AddSeconds(1),
                secondRandom);
        });

        try
        {
            await secondStarted.Task.WaitAsync(ConcurrencyTimeout);
            var reachedBeforeRelease = await Task.WhenAny(
                secondRandom.Entered,
                Task.Delay(ContentionProbe));

            Assert.NotSame(secondRandom.Entered, reachedBeforeRelease);
        }
        finally
        {
            firstRandom.Release();
        }

        var replies = await Task.WhenAll(first, second).WaitAsync(ConcurrencyTimeout);

        Assert.All(replies, reply => Assert.True(reply.ShouldDisplayText));
        Assert.Equal(2, agent.CreateSnapshot().TurnCount);
    }

    [Fact]
    public async Task SceneHistory_RestoreDoesNotEnumerateLiveEntriesOrLoseAConcurrentRecord()
    {
        var scene = SceneCatalog.PersonaScenes.First();
        var history = new SceneHistory();
        history.Record(scene, LocalNow, scene.Lines[0]);
        var source = new GateFirstMoveEnumerable<SceneHistoryEntry>(history.Entries);

        var restore = Task.Run(() => history.Restore(source));
        await source.Entered.WaitAsync(ConcurrencyTimeout);

        var recordStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = Task.Run(() =>
        {
            recordStarted.TrySetResult();
            history.Record(scene, LocalNow.AddMinutes(1), scene.Lines[1]);
        });

        try
        {
            await recordStarted.Task.WaitAsync(ConcurrencyTimeout);
            await AssertCompletesAfterRelease(record);
        }
        finally
        {
            source.Release();
        }

        await Task.WhenAll(restore, record).WaitAsync(ConcurrencyTimeout);
        Assert.Equal(
            [LocalNow, LocalNow.AddMinutes(1)],
            history.Entries.Select(entry => entry.PlayedAt));
    }

    [Fact]
    public void CharacterState_ActiveStoriesCopiesIncomingAndExposesNoWritableLiveCollection()
    {
        var state = CharacterState.Create(LocalNow);
        var pending = new StoryProgress("story", 1, LocalNow.AddHours(1));
        var incoming = new List<StoryProgress> { pending };

        state.ActiveStories = incoming;
        incoming.Clear();

        var exposed = state.ActiveStories;
        Assert.Single(exposed);
        Assert.False(exposed is ICollection<StoryProgress> { IsReadOnly: false });
    }

    [Fact]
    public async Task CompanionEventPump_ConcurrentDirectPollsEmitEachLogicalTickEventOnce()
    {
        const int callerCount = 32;
        var beforeMidnight = new DateTime(2026, 7, 27, 23, 59, 45, DateTimeKind.Local);
        var now = beforeMidnight.AddSeconds(30);
        var dueAt = beforeMidnight.AddSeconds(20);
        var pump = new CompanionEventPump(beforeMidnight, TimeSpan.FromMinutes(12));
        using var start = new Barrier(callerCount);

        var callers = Enumerable.Range(0, callerCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(ConcurrencyTimeout);
                    return pump.Poll(now, TimeSpan.FromSeconds(5), dueAt);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        var emitted = await Task.WhenAll(callers).WaitAsync(ConcurrencyTimeout);

        Assert.Equal(
            new[]
            {
                CompanionEvent.DayChanged,
                CompanionEvent.IdleReturned,
                CompanionEvent.StoryTimerDue,
                CompanionEvent.ClockTick
            }.Order(),
            emitted.Where(companionEvent => companionEvent is not null)
                .Select(companionEvent => companionEvent!.Value)
                .Order());
    }

    private static async Task AssertCompletesAfterRelease(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(ContentionProbe));
        Assert.NotSame(task, completed);
    }

    private sealed class GateRandom : Random
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _gateUsed;

        public Task Entered => _entered.Task;

        public override double NextDouble()
        {
            if (Interlocked.Exchange(ref _gateUsed, 1) == 0)
            {
                _entered.TrySetResult();
                _release.Task.GetAwaiter().GetResult();
            }

            return 0.5;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class SignalRandom : Random
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public override double NextDouble()
        {
            _entered.TrySetResult();
            return 0.5;
        }
    }

    private sealed class GateFirstMoveEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public IEnumerator<T> GetEnumerator()
        {
            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                yield break;
            }

            _entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            yield return enumerator.Current;
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Release() => _release.TrySetResult();
    }
}
