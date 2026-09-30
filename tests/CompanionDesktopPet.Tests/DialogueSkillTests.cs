using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSkillTests
{
    [Fact]
    public void TryApply_ChangesSpeechAndHiddenSamplingWithoutTouchingTheRest()
    {
        var timing = DeveloperTestParameters.CreateDefault();

        var applied = DialogueSkills.TryApply(
            """[{"skill":"set_speech","speed":0.8,"top_k":8}]""",
            timing,
            PetScale.Normal,
            true,
            false,
            true,
            out var state);

        Assert.True(applied);
        Assert.True(state.Changed);
        Assert.Equal(0.8, state.Timing.SpeechSpeed);
        Assert.Equal(1, state.Timing.SpeechTemperature);
        Assert.Equal(1.35, state.Timing.SpeechRepetition);
        Assert.Equal(8, state.Timing.TopK);
        Assert.Equal(1, state.Timing.TopP);
        Assert.Equal(1, timing.SpeechSpeed);
        Assert.Equal(80, state.Timing.ReplyMaxChars);
    }

    [Fact]
    public void TryApply_RejectsAnOutOfRangeBatchAndKeepsThePreviousValues()
    {
        var timing = DeveloperTestParameters.CreateDefault();

        var applied = DialogueSkills.TryApply(
            """[{"skill":"set_interval","period":"day","minimum_minutes":20,"maximum_minutes":3}]""",
            timing,
            PetScale.Normal,
            true,
            false,
            true,
            out var state);

        Assert.False(applied);
        Assert.False(state.Changed);
        Assert.Equal(5, state.Timing.DayMinimumMinutes);
        Assert.Equal(15, state.Timing.DayMaximumMinutes);
        Assert.Equal(5, timing.DayMinimumMinutes);
    }

    [Fact]
    public void TryApply_UpdatesIntervalBubbleAndCompanionTogether()
    {
        var applied = DialogueSkills.TryApply(
            """
            [
              {"skill":"set_interval","period":"late","minimum_minutes":40,"maximum_minutes":70},
              {"skill":"set_bubble","seconds":9},
              {"skill":"set_companion","scale":"Large","voice_enabled":false},
              {"skill":"unknown"}
            ]
            """,
            DeveloperTestParameters.CreateDefault(),
            PetScale.Normal,
            true,
            false,
            true,
            out var state);

        Assert.True(applied);
        Assert.Equal(40, state.Timing.LateNightMinimumMinutes);
        Assert.Equal(70, state.Timing.LateNightMaximumMinutes);
        Assert.Equal(9, state.Timing.BubbleSeconds);
        Assert.Equal(PetScale.Large, state.Scale);
        Assert.False(state.VoiceEnabled);
        Assert.True(state.AlwaysOnTop);
        Assert.False(state.AnimationPaused);
    }
}
