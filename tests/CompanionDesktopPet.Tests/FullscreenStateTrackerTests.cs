using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class FullscreenStateTrackerTests
{
    [Fact]
    public void Update_PreservesLastExplicitQuietModeWithoutInventingObservedFalse()
    {
        var tracker = new FullscreenStateTracker();

        Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
        Assert.Equal(new FullscreenSnapshot(true, true), tracker.Update(true));
        Assert.Equal(new FullscreenSnapshot(null, true), tracker.Update(null));
        Assert.Equal(new FullscreenSnapshot(false, false), tracker.Update(false));
        Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
    }
}
