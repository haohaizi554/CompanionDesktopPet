using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class AutomaticDialogueCadenceControllerTests
{
    [Fact]
    public void Evaluate_BeforeArm_ReturnsNotArmed()
    {
        var controller = CreateController(new ManualTimeProvider());

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.NotArmed, evaluation.Decision);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Fact]
    public void Evaluate_AtDueTimestamp_ReturnsSpeak()
    {
        var time = new ManualTimeProvider();
        var controller = CreateController(time);
        var delay = controller.Arm(Daytime, effectiveQuietMode: false);
        time.Advance(delay);

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.Speak, evaluation.Decision);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Fact]
    public void Evaluate_BeforeDueTimestamp_ReturnsExactRemainingDelay()
    {
        var time = new ManualTimeProvider();
        var controller = CreateController(time);
        var delay = controller.Arm(Daytime, effectiveQuietMode: false);
        time.Advance(TimeSpan.FromMinutes(2));

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.Wait, evaluation.Decision);
        Assert.Equal(delay - TimeSpan.FromMinutes(2), evaluation.Remaining);
    }

    [Fact]
    public void Evaluate_WhenModeChanges_ReturnsSilentRearm()
    {
        var controller = CreateController(new ManualTimeProvider());
        controller.Arm(Daytime, effectiveQuietMode: false);

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: true);

        Assert.True(controller.RequiresModeRearm(Daytime, effectiveQuietMode: true));
        Assert.Equal(AutomaticCadenceDecision.RearmModeChanged, evaluation.Decision);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Fact]
    public void Evaluate_WhenExactlyOneMinuteLate_ReturnsSpeak()
    {
        var time = new ManualTimeProvider();
        var controller = CreateController(time);
        var delay = controller.Arm(Daytime, effectiveQuietMode: false);
        time.Advance(delay + TimeSpan.FromMinutes(1));

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.Speak, evaluation.Decision);
    }

    [Fact]
    public void Evaluate_WhenMoreThanOneMinuteLate_ReturnsLateRearm()
    {
        var time = new ManualTimeProvider();
        var controller = CreateController(time);
        var delay = controller.Arm(Daytime, effectiveQuietMode: false);
        time.Advance(delay + TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1));

        var evaluation = controller.Evaluate(Daytime, effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.RearmLate, evaluation.Decision);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Fact]
    public void Evaluate_WhenWallClockRollsBack_UsesMonotonicElapsedTime()
    {
        var time = new ManualTimeProvider();
        var controller = CreateController(time);
        var delay = controller.Arm(Daytime, effectiveQuietMode: false);
        time.Advance(delay);

        var evaluation = controller.Evaluate(Daytime.AddHours(-1), effectiveQuietMode: false);

        Assert.Equal(AutomaticCadenceDecision.Speak, evaluation.Decision);
    }

    [Fact]
    public void Reset_ClearsArmState()
    {
        var controller = CreateController(new ManualTimeProvider());
        controller.Arm(Daytime, effectiveQuietMode: false);

        controller.Reset();

        var state = controller.Capture();
        Assert.False(state.IsArmed);
        Assert.Null(state.Mode);
        Assert.Equal(TimeSpan.Zero, state.Delay);
        Assert.Equal(0, state.ArmedAtTimestamp);
        Assert.Equal(AutomaticCadenceDecision.NotArmed,
            controller.Evaluate(Daytime, effectiveQuietMode: false).Decision);
    }

    [Fact]
    public void RequiresModeRearm_WhenFullscreenSpansOrdinaryBandBoundary_ReturnsFalse()
    {
        var controller = CreateController(new ManualTimeProvider());
        controller.Arm(new DateTime(2026, 7, 26, 17, 59, 59), effectiveQuietMode: true);

        var requiresRearm = controller.RequiresModeRearm(
            new DateTime(2026, 7, 26, 18, 0, 0), effectiveQuietMode: true);

        Assert.False(requiresRearm);
    }

    private static readonly DateTime Daytime = new(2026, 7, 26, 10, 0, 0);

    private static AutomaticDialogueCadenceController CreateController(TimeProvider timeProvider) =>
        new(new DialogueScheduler(new EndpointRandom().Next), timeProvider);

    private sealed class EndpointRandom : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
