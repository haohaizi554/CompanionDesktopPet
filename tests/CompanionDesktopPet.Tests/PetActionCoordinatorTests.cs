using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class PetActionCoordinatorTests
{
    [Fact]
    public void AmbientActions_AreExclusiveAndCompleteBackToIdle()
    {
        var coordinator = new PetActionCoordinator();

        Assert.True(coordinator.TryBeginAmbient(PetAmbientAction.Blink));
        Assert.Equal(PetActionState.Blinking, coordinator.State);
        Assert.False(coordinator.TryBeginAmbient(PetAmbientAction.Greeting));

        coordinator.Complete(PetActionState.Blinking);

        Assert.Equal(PetActionState.Idle, coordinator.State);
    }

    [Fact]
    public void DragPauseAndLanding_HavePriorityAndRecoverExplicitly()
    {
        var coordinator = new PetActionCoordinator();

        Assert.True(coordinator.TryBeginAmbient(PetAmbientAction.Greeting));
        coordinator.BeginDrag();
        Assert.Equal(PetActionState.Dragging, coordinator.State);
        coordinator.BeginLanding();
        Assert.Equal(PetActionState.Landing, coordinator.State);
        coordinator.Complete(PetActionState.Landing);
        Assert.Equal(PetActionState.Idle, coordinator.State);

        coordinator.Pause();
        Assert.Equal(PetActionState.Paused, coordinator.State);
        Assert.False(coordinator.TryBeginAmbient(PetAmbientAction.Blink));
        coordinator.BeginDrag();
        Assert.Equal(PetActionState.Dragging, coordinator.State);
        coordinator.BeginLanding();
        coordinator.Complete(PetActionState.Landing);
        Assert.Equal(PetActionState.Paused, coordinator.State);
        coordinator.Resume();
        Assert.Equal(PetActionState.Idle, coordinator.State);
    }

    [Fact]
    public void ResumeDuringDrag_ClearsThePausedLandingDestination()
    {
        var coordinator = new PetActionCoordinator();

        coordinator.Pause();
        coordinator.BeginDrag();
        coordinator.Resume();

        Assert.Equal(PetActionState.Dragging, coordinator.State);
        coordinator.BeginLanding();
        coordinator.Complete(PetActionState.Landing);

        Assert.Equal(PetActionState.Idle, coordinator.State);
    }

    [Fact]
    public void RepeatedBeginDrag_DoesNotLoseThePausedLandingDestination()
    {
        var coordinator = new PetActionCoordinator();

        coordinator.Pause();
        coordinator.BeginDrag();
        coordinator.BeginDrag();
        coordinator.BeginLanding();
        coordinator.Complete(PetActionState.Landing);

        Assert.Equal(PetActionState.Paused, coordinator.State);
    }
}
