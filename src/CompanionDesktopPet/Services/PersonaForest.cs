namespace CompanionDesktopPet.Services;

internal static class PersonaForest
{
    internal const double CloseCare = 12;
    internal const double CloseLife = 4;
    internal const double CloseTechnical = -8;
    internal const double CloseWarm = 10;
    internal const double ToneContinue = 6;
    internal const double TopicStick = 5;
    internal const double TimeMatch = 8;

    internal static bool IsClose(string? relationship) =>
        relationship is "她是对方的女朋友" or "她是对方的老婆" or "她是对方的伴侣";

    internal static double Adjustment(
        SceneDefinition scene,
        string? relationship,
        string? lastTone,
        DialogueCategory? lastCategory,
        DialogueCategory? categoryBeforeLast,
        string? timeToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var score = 0.0;
        if (IsClose(relationship))
        {
            score += scene.CategoryGroup switch
            {
                DialogueCategoryGroup.DailyCare or DialogueCategoryGroup.EmotionalReflection => CloseCare,
                DialogueCategoryGroup.CharacterLife => CloseLife,
                DialogueCategoryGroup.Technical
                    or DialogueCategoryGroup.Growth
                    or DialogueCategoryGroup.Career => CloseTechnical,
                _ => 0
            };
            if (scene.RelationshipProfile == "warm_friend" || scene.Tone is "intimate" or "gentle")
            {
                score += CloseWarm;
            }
        }

        if (!string.IsNullOrEmpty(lastTone) && lastTone == scene.Tone)
        {
            score += ToneContinue;
        }

        if (lastCategory == scene.Category && categoryBeforeLast != scene.Category)
        {
            score += TopicStick;
        }

        if (!string.IsNullOrEmpty(timeToken)
            && scene.RequiredContext.Any(token => token == timeToken))
        {
            score += TimeMatch;
        }

        return score;
    }
}
