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
}
