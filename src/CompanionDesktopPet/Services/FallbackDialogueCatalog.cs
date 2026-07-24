namespace CompanionDesktopPet.Services;

internal static class FallbackDialogueCatalog
{
    public static DialogueLine StartupLine { get; } = Line(
        "fallback-startup-01",
        "我先醒醒，马上就好。",
        "fallback.startup",
        DialogueTrigger.AppStart);

    public static IReadOnlyList<DialogueLine> ClickLines { get; } =
    [
        Line("fallback-click-01", "在呢，别戳啦。", "fallback.click.01"),
        Line("fallback-click-02", "嗯嗯，我听见了。", "fallback.click.02"),
        Line("fallback-click-03", "脑袋加载中，等下。", "fallback.click.03"),
        Line("fallback-click-04", "先陪你一下，马上好。", "fallback.click.04")
    ];

    public static IReadOnlyList<DialogueLine> All { get; } = [StartupLine, .. ClickLines];

    private static DialogueLine Line(
        string id,
        string text,
        string semanticGroup,
        DialogueTrigger trigger = DialogueTrigger.Any) =>
        new(
            id,
            DialogueCategory.CharacterLife,
            DialogueCategoryGroup.CharacterLife,
            "fallback.local",
            semanticGroup,
            DialogueOutputMode.SelfTalk,
            trigger,
            ["none"],
            "dry_warm",
            0,
            1,
            1,
            2,
            1,
            false,
            true,
            text,
            "builtin_fallback",
            "builtin:fallback",
            "cold-start safety fallback");
}
