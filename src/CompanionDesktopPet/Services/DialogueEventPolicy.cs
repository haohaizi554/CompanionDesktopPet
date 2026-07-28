namespace CompanionDesktopPet.Services;

internal static class DialogueEventPolicy
{
    internal static bool IsDirectFeedback(CompanionEvent trigger) => trigger is
        CompanionEvent.Click or CompanionEvent.DragReleased or
        CompanionEvent.AnimationPaused or CompanionEvent.AnimationResumed or
        // Resizing and restoring position are explicit user-control actions, so their
        // acknowledgements should be as immediate as click and drag feedback.
        CompanionEvent.SizeChanged or CompanionEvent.PositionRestored;

    internal static bool BypassesInterruptionBudget(CompanionEvent trigger) =>
        trigger == CompanionEvent.Automatic || IsDirectFeedback(trigger);
}
