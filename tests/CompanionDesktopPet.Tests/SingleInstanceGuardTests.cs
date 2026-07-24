using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuardWithSameName_IsNotPrimary()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(duplicate.IsPrimaryInstance);
    }

    [Fact]
    public void DuplicateSignalsThePrimaryGuard()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);
        using var activated = new ManualResetEventSlim();
        primary.RegisterActivationCallback(activated.Set);

        Assert.True(duplicate.SignalPrimaryInstance());

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SignalBeforeCallbackRegistration_IsNotLost()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);
        using var activated = new ManualResetEventSlim();

        Assert.True(duplicate.SignalPrimaryInstance());
        primary.RegisterActivationCallback(activated.Set);

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisposePreventsLaterActivationCallbacks()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);
        using var callbackObserved = new ManualResetEventSlim();
        primary.RegisterActivationCallback(callbackObserved.Set);

        primary.Dispose();
        Assert.True(duplicate.SignalPrimaryInstance());

        Assert.False(callbackObserved.Wait(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task GuardCanBeDisposedFromANonCreatingThread()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var guard = new SingleInstanceGuard(name);

        var exception = await Record.ExceptionAsync(() => Task.Run(guard.Dispose));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_HoldsActivationEventBeforePublishingMutexMarker()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var factory = new RecordingKernelObjectFactory();

        using var guard = new SingleInstanceGuard(name, factory);

        Assert.Equal(["event", "mutex"], factory.Calls);
        Assert.NotNull(factory.ActivationEvent);
        Assert.False(factory.ActivationEvent!.SafeWaitHandle.IsClosed);
    }

    [Fact]
    public void ConstructorFailure_DisposesTheAlreadyHeldActivationEvent()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var factory = new RecordingKernelObjectFactory { ThrowWhenCreatingMutex = true };

        Assert.Throws<InvalidOperationException>(() => new SingleInstanceGuard(name, factory));

        Assert.Equal(["event", "mutex"], factory.Calls);
        Assert.NotNull(factory.ActivationEvent);
        Assert.True(factory.ActivationEvent!.SafeWaitHandle.IsClosed);
    }

    [Fact]
    public void ActivationCallback_CanDisposeItsOwnGuardWithoutDeadlock()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var factory = new RecordingKernelObjectFactory();
        var primary = new SingleInstanceGuard(name, factory);
        using var duplicate = new SingleInstanceGuard(name);
        using var callbackReturned = new ManualResetEventSlim();
        primary.RegisterActivationCallback(() =>
        {
            primary.Dispose();
            callbackReturned.Set();
        });

        Assert.True(duplicate.SignalPrimaryInstance());

        Assert.True(callbackReturned.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(primary.SignalPrimaryInstance());
        Assert.True(SpinWait.SpinUntil(
            () => factory.ActivationEvent!.SafeWaitHandle.IsClosed
                && factory.Mutex!.SafeWaitHandle.IsClosed,
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExternalDispose_WaitsForInFlightActivationCallback()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);
        using var callbackStarted = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        Task? disposeTask = null;
        primary.RegisterActivationCallback(() =>
        {
            callbackStarted.Set();
            releaseCallback.Wait();
        });

        try
        {
            Assert.True(duplicate.SignalPrimaryInstance());
            Assert.True(callbackStarted.Wait(TimeSpan.FromSeconds(5)));

            disposeTask = Task.Run(primary.Dispose);
            await Task.Delay(100);
            Assert.False(disposeTask.IsCompleted);

            releaseCallback.Set();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCallback.Set();
            if (disposeTask is not null)
            {
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            primary.Dispose();
        }
    }

    private sealed class RecordingKernelObjectFactory : ISingleInstanceKernelObjectFactory
    {
        public List<string> Calls { get; } = [];
        public EventWaitHandle? ActivationEvent { get; private set; }
        public Mutex? Mutex { get; private set; }
        public bool ThrowWhenCreatingMutex { get; init; }

        public EventWaitHandle CreateActivationEvent(string name)
        {
            Calls.Add("event");
            ActivationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                name);
            return ActivationEvent;
        }

        public Mutex CreateMutex(string name, out bool createdNew)
        {
            Calls.Add("mutex");
            if (ThrowWhenCreatingMutex)
            {
                createdNew = false;
                throw new InvalidOperationException("mutex creation failed");
            }

            Mutex = new Mutex(initiallyOwned: false, name, out createdNew);
            return Mutex;
        }
    }
}
