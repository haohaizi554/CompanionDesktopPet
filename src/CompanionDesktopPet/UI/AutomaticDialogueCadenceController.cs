using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

internal enum AutomaticCadenceDecision
{
    NotArmed,
    Wait,
    Speak,
    RearmModeChanged,
    RearmLate
}

internal readonly record struct AutomaticCadenceEvaluation(
    AutomaticCadenceDecision Decision,
    TimeSpan Remaining);

internal readonly record struct AutomaticCadenceState(
    bool IsArmed,
    AutomaticCadenceMode? Mode,
    TimeSpan Delay,
    long ArmedAtTimestamp);

internal sealed class AutomaticDialogueCadenceController
{
    private static readonly TimeSpan LateTolerance = TimeSpan.FromMinutes(1);
    private readonly DialogueScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private AutomaticCadenceMode? _mode;
    private TimeSpan _delay;
    private long _armedAt;

    internal AutomaticDialogueCadenceController(
        DialogueScheduler scheduler,
        TimeProvider timeProvider)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal TimeSpan Arm(DateTime localTime, bool effectiveQuietMode)
    {
        _mode = DialogueScheduler.GetMode(localTime, effectiveQuietMode);
        _delay = _scheduler.NextDelay(localTime, effectiveQuietMode);
        _armedAt = _timeProvider.GetTimestamp();
        return _delay;
    }

    internal bool RequiresModeRearm(DateTime localTime, bool effectiveQuietMode) =>
        _mode.HasValue
        && _mode.Value != DialogueScheduler.GetMode(localTime, effectiveQuietMode);

    internal AutomaticCadenceEvaluation Evaluate(DateTime localTime, bool effectiveQuietMode)
    {
        if (!_mode.HasValue)
        {
            return new(AutomaticCadenceDecision.NotArmed, TimeSpan.Zero);
        }

        if (RequiresModeRearm(localTime, effectiveQuietMode))
        {
            return new(AutomaticCadenceDecision.RearmModeChanged, TimeSpan.Zero);
        }

        var elapsed = _timeProvider.GetElapsedTime(_armedAt, _timeProvider.GetTimestamp());
        if (elapsed < _delay)
        {
            return new(AutomaticCadenceDecision.Wait, _delay - elapsed);
        }

        if (elapsed - _delay > LateTolerance)
        {
            return new(AutomaticCadenceDecision.RearmLate, TimeSpan.Zero);
        }

        return new(AutomaticCadenceDecision.Speak, TimeSpan.Zero);
    }

    internal AutomaticCadenceState Capture() =>
        new(_mode.HasValue, _mode, _delay, _armedAt);

    internal void Reset()
    {
        _mode = null;
        _delay = TimeSpan.Zero;
        _armedAt = 0;
    }
}
