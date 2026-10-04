using System.Globalization;
using System.Text.Json;
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public readonly record struct DialogueSkillState(
    DeveloperTestParameters Timing,
    PetScale Scale,
    bool AlwaysOnTop,
    bool AnimationPaused,
    bool VoiceEnabled,
    bool Changed);

public static class DialogueSkills
{
    public static Dictionary<string, object?> Describe(
        DeveloperTestParameters timing,
        PetScale scale,
        bool alwaysOnTop,
        bool animationPaused,
        bool voiceEnabled)
    {
        ArgumentNullException.ThrowIfNull(timing);
        return new Dictionary<string, object?>
        {
            ["day_min"] = timing.DayMinimumMinutes,
            ["day_max"] = timing.DayMaximumMinutes,
            ["evening_min"] = timing.EveningMinimumMinutes,
            ["evening_max"] = timing.EveningMaximumMinutes,
            ["late_min"] = timing.LateNightMinimumMinutes,
            ["late_max"] = timing.LateNightMaximumMinutes,
            ["fullscreen_min"] = timing.FullscreenMinimumMinutes,
            ["fullscreen_max"] = timing.FullscreenMaximumMinutes,
            ["bubble_seconds"] = timing.BubbleSeconds,
            ["speech_speed"] = timing.SpeechSpeed,
            ["speech_temperature"] = timing.SpeechTemperature,
            ["speech_repetition"] = timing.SpeechRepetition,
            ["top_k"] = timing.TopK,
            ["top_p"] = timing.TopP,
            ["reply_max_chars"] = timing.ReplyMaxChars,
            ["scale"] = scale.ToString(),
            ["always_on_top"] = alwaysOnTop,
            ["animation_paused"] = animationPaused,
            ["voice_enabled"] = voiceEnabled
        };
    }

    public static bool TryApply(
        string? actionsJson,
        DeveloperTestParameters timing,
        PetScale scale,
        bool alwaysOnTop,
        bool animationPaused,
        bool voiceEnabled,
        out DialogueSkillState state)
    {
        ArgumentNullException.ThrowIfNull(timing);
        var next = timing.Clone();
        var nextScale = scale;
        var nextTop = alwaysOnTop;
        var nextPaused = animationPaused;
        var nextVoice = voiceEnabled;
        var changed = false;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(actionsJson) ? "[]" : actionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                state = Unchanged(timing.Clone(), scale, alwaysOnTop, animationPaused, voiceEnabled);
                return false;
            }

            foreach (var action in document.RootElement.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object
                    || !action.TryGetProperty("skill", out var skillName))
                {
                    continue;
                }

                changed |= skillName.GetString() switch
                {
                    "set_interval" => TryInterval(action, next),
                    "set_bubble" => TryBubble(action, next),
                    "set_speech" => TrySpeech(action, next),
                    "set_companion" => TryCompanion(
                        action,
                        ref nextScale,
                        ref nextTop,
                        ref nextPaused,
                        ref nextVoice),
                    _ => false
                };
            }
        }
        catch (JsonException)
        {
            state = Unchanged(timing.Clone(), scale, alwaysOnTop, animationPaused, voiceEnabled);
            return false;
        }

        if (next.Validate() is not null)
        {
            state = Unchanged(timing.Clone(), scale, alwaysOnTop, animationPaused, voiceEnabled);
            return false;
        }

        state = new DialogueSkillState(next, nextScale, nextTop, nextPaused, nextVoice, changed);
        return true;
    }

    private static DialogueSkillState Unchanged(
        DeveloperTestParameters timing,
        PetScale scale,
        bool alwaysOnTop,
        bool animationPaused,
        bool voiceEnabled) =>
        new(timing, scale, alwaysOnTop, animationPaused, voiceEnabled, false);

    private static bool TryInterval(JsonElement action, DeveloperTestParameters timing)
    {
        if (!TryInt(action, "minimum_minutes", out var minimum)
            || !TryInt(action, "maximum_minutes", out var maximum)
            || !action.TryGetProperty("period", out var periodValue))
        {
            return false;
        }

        switch (periodValue.GetString())
        {
            case "day":
                timing.DayMinimumMinutes = minimum;
                timing.DayMaximumMinutes = maximum;
                return true;
            case "evening":
                timing.EveningMinimumMinutes = minimum;
                timing.EveningMaximumMinutes = maximum;
                return true;
            case "late":
                timing.LateNightMinimumMinutes = minimum;
                timing.LateNightMaximumMinutes = maximum;
                return true;
            case "fullscreen":
                timing.FullscreenMinimumMinutes = minimum;
                timing.FullscreenMaximumMinutes = maximum;
                return true;
            default:
                return false;
        }
    }

    private static bool TryBubble(JsonElement action, DeveloperTestParameters timing)
    {
        if (!TryInt(action, "seconds", out var seconds))
        {
            return false;
        }

        timing.BubbleSeconds = seconds;
        return true;
    }

    private static bool TrySpeech(JsonElement action, DeveloperTestParameters timing)
    {
        var changed = false;
        if (TryDouble(action, "speed", out var speed))
        {
            timing.SpeechSpeed = SnapSpeechIfLegal(
                speed,
                DeveloperTestParameters.MinimumSpeechSpeed,
                DeveloperTestParameters.MaximumSpeechSpeed);
            changed = true;
        }

        if (TryDouble(action, "temperature", out var temperature))
        {
            timing.SpeechTemperature = SnapSpeechIfLegal(
                temperature,
                DeveloperTestParameters.MinimumSpeechTemperature,
                DeveloperTestParameters.MaximumSpeechTemperature);
            changed = true;
        }

        if (TryDouble(action, "repetition", out var repetition))
        {
            timing.SpeechRepetition = SnapSpeechIfLegal(
                repetition,
                DeveloperTestParameters.MinimumSpeechRepetition,
                DeveloperTestParameters.MaximumSpeechRepetition);
            changed = true;
        }

        if (TryInt(action, "top_k", out var topK))
        {
            timing.TopK = topK;
            changed = true;
        }

        if (TryDouble(action, "top_p", out var topP))
        {
            timing.TopP = topP;
            changed = true;
        }

        return changed;
    }

    private static bool TryCompanion(
        JsonElement action,
        ref PetScale scale,
        ref bool alwaysOnTop,
        ref bool animationPaused,
        ref bool voiceEnabled)
    {
        var changed = false;
        if (action.TryGetProperty("scale", out var scaleValue)
            && scaleValue.ValueKind == JsonValueKind.String
            && Enum.TryParse(scaleValue.GetString(), out PetScale parsed)
            && Enum.IsDefined(parsed))
        {
            scale = parsed;
            changed = true;
        }

        if (TryBool(action, "always_on_top", out var topmost))
        {
            alwaysOnTop = topmost;
            changed = true;
        }

        if (TryBool(action, "animation_paused", out var paused))
        {
            animationPaused = paused;
            changed = true;
        }

        if (TryBool(action, "voice_enabled", out var voice))
        {
            voiceEnabled = voice;
            changed = true;
        }

        return changed;
    }

    private static bool TryInt(JsonElement action, string name, out int value)
    {
        value = 0;
        if (!action.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (property.TryGetInt32(out value))
        {
            return true;
        }

        if (!property.TryGetDouble(out var number) || number != Math.Truncate(number))
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    private static double SnapSpeechIfLegal(double value, double minimum, double maximum) =>
        value >= minimum && value <= maximum && !double.IsNaN(value) && !double.IsInfinity(value)
            ? DeveloperTestParameters.SnapSpeechStep(value, minimum, maximum)
            : value;

    private static bool TryDouble(JsonElement action, string name, out double value)
    {
        value = 0;
        if (!action.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryBool(JsonElement action, string name, out bool value)
    {
        value = false;
        if (!action.TryGetProperty(name, out var property)
            || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
