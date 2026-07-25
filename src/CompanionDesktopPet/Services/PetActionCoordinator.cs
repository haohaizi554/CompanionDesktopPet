using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class PetActionCoordinator
{
    private bool _returnToPaused;

    public PetActionState State { get; private set; } = PetActionState.Idle;

    public bool TryBeginAmbient(PetAmbientAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown ambient action.");
        }

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
        if (State == PetActionState.Dragging)
        {
            return;
        }

        _returnToPaused = State switch
        {
            PetActionState.Paused => true,
            PetActionState.Landing => _returnToPaused,
            _ => false
        };
        State = PetActionState.Dragging;
    }

    public void BeginLanding()
    {
        if (State == PetActionState.Dragging)
        {
            State = PetActionState.Landing;
        }
    }

    public void Pause()
    {
        if (State is PetActionState.Dragging or PetActionState.Landing)
        {
            _returnToPaused = true;
            return;
        }

        State = PetActionState.Paused;
    }

    public void Resume()
    {
        if (State == PetActionState.Paused)
        {
            State = PetActionState.Idle;
        }

        if (State is PetActionState.Dragging or PetActionState.Landing)
        {
            _returnToPaused = false;
        }
    }

    public void Complete(PetActionState completed)
    {
        if (completed is not (PetActionState.Blinking
                or PetActionState.Greeting
                or PetActionState.Landing)
            || State != completed)
        {
            return;
        }

        State = completed == PetActionState.Landing && _returnToPaused
            ? PetActionState.Paused
            : PetActionState.Idle;
        _returnToPaused = false;
    }
}
