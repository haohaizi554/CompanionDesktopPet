using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class FullscreenStateTrackerTests
{
    [Fact]
    public void Initial_State_IsNullObservedAndFalseEffectiveQuietMode()
    {
        var tracker = new FullscreenStateTracker();

        Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
    }

    [Fact]
    public void RepeatedSameObservation_IsIdempotent()
    {
        var tracker = new FullscreenStateTracker();

        Assert.Equal(tracker.Update(true), tracker.Update(true));
        Assert.Equal(tracker.Update(false), tracker.Update(false));
    }

    [Fact]
    public void TrueFalseTrue_TransitionsRespectStickyLogic()
    {
        var tracker = new FullscreenStateTracker();

        Assert.Equal(new FullscreenSnapshot(true, true), tracker.Update(true));
        Assert.Equal(new FullscreenSnapshot(null, true), tracker.Update(null));
        Assert.Equal(new FullscreenSnapshot(false, false), tracker.Update(false));
        Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
        Assert.Equal(new FullscreenSnapshot(true, true), tracker.Update(true));
    }

    [Fact]
    public void ObserveTrueAfterFalse_SwitchesToEffectiveQuietModeTrue()
    {
        var tracker = new FullscreenStateTracker();

        Assert.Equal(new FullscreenSnapshot(false, false), tracker.Update(false));
        Assert.Equal(new FullscreenSnapshot(true, true), tracker.Update(true));
    }

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
