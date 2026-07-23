using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class PetActionCoordinator
{
    private bool _returnToPaused;

    public PetActionState State { get; private set; } = PetActionState.Idle;

    public bool TryBeginAmbient(PetAmbientAction action)
    {
        if (State != PetActionState.Idle)
        {
            return false;
        }

        State = action == PetAmbientAction.Blink
            ? PetActionState.Blinking
            : PetActionState.Greeting;
        return true;
    }

    public void BeginDrag()
    {
        _returnToPaused = State == PetActionState.Paused;
        State = PetActionState.Dragging;
    }

    public void BeginLanding()
    {
        if (State == PetActionState.Dragging)
        {
            State = PetActionState.Landing;
        }
    }

    public void Pause() => State = PetActionState.Paused;

    public void Resume()
    {
        if (State == PetActionState.Paused)
        {
            State = PetActionState.Idle;
        }
    }

    public void Complete(PetActionState completed)
    {
        if (State != completed)
        {
            return;
        }

        State = completed == PetActionState.Landing && _returnToPaused
            ? PetActionState.Paused
            : PetActionState.Idle;
        _returnToPaused = false;
    }
}
