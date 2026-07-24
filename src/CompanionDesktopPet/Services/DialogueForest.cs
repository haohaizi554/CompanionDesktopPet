namespace CompanionDesktopPet.Services;

public enum DialogueTreeKind
{
    Technical,
    Growth,
    Companion,
    Life
}

public enum CompanionEvent
{
    Startup,
    Click,
    Automatic,
    DragReleased,
    AnimationPaused,
    AnimationResumed,
    SizeChanged,
    PositionRestored,
    ClockTick,
    DayChanged,
    IdleReturned,
    SystemUnlocked,
    SleepResumed,
    FullscreenChanged,
    StoryTimerDue
}

public sealed record DialogueTree(
    DialogueTreeKind Kind,
    string Name,
    IReadOnlyList<DialogueCategory> Categories);

public static class DialogueForest
{
    public static IReadOnlyDictionary<DialogueCategoryGroup, double> CategoryGroupWeights { get; } =
        PersonaContractGenerated.CategoryGroupWeights;

    public static IReadOnlyDictionary<DialogueOutputMode, double> OutputModeTargets { get; } =
        PersonaContractGenerated.OutputModeTargets;

    public static IReadOnlyDictionary<DialogueTreeKind, double> TreeWeights { get; } =
        new Dictionary<DialogueTreeKind, double>
        {
            [DialogueTreeKind.Technical] = 0.18,
            [DialogueTreeKind.Growth] = 0.17,
            [DialogueTreeKind.Companion] = 0.38,
            [DialogueTreeKind.Life] = 0.27
        };

    public static IReadOnlySet<DialogueCategoryGroup> BlockAdjacentCategoryGroups { get; } =
        PersonaContractGenerated.BlockAdjacentCategoryGroups;

    public static IReadOnlyList<DialogueTree> Trees { get; } =
    [
        new(DialogueTreeKind.Technical, "技术森林",
        [
            DialogueCategory.Debugging, DialogueCategory.Python, DialogueCategory.Java,
            DialogueCategory.Cpp, DialogueCategory.Frontend, DialogueCategory.Backend,
            DialogueCategory.Database, DialogueCategory.Algorithms, DialogueCategory.Systems,
            DialogueCategory.Networks, DialogueCategory.GitDevOps, DialogueCategory.Architecture
        ]),
        new(DialogueTreeKind.Growth, "成长森林",
        [DialogueCategory.Study, DialogueCategory.Career, DialogueCategory.EnglishPractice]),
        new(DialogueTreeKind.Companion, "陪伴森林",
        [
            DialogueCategory.DailyCare, DialogueCategory.EmotionalSupport,
            DialogueCategory.ProactiveChat, DialogueCategory.SystemAmbient
        ]),
        new(DialogueTreeKind.Life, "生活森林",
        [DialogueCategory.WanderingLife, DialogueCategory.DressesHobbies, DialogueCategory.CharacterLife])
    ];

    public static DialogueTree SelectTree(
        CompanionEvent trigger,
        DialogueCategory? previousCategory,
        int turnCount,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return trigger switch
        {
            CompanionEvent.AnimationPaused or CompanionEvent.PositionRestored => GetTree(DialogueTreeKind.Companion),
            CompanionEvent.SizeChanged or CompanionEvent.DragReleased => GetTree(DialogueTreeKind.Life),
            CompanionEvent.AnimationResumed => GetTree(DialogueTreeKind.Growth),
            CompanionEvent.Startup => GetTree(DialogueTreeKind.Companion),
            _ => WeightedTree(random)
        };
    }

    public static DialogueCategory SelectCategory(
        DialogueTree tree,
        DialogueCategory? previousCategory,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(random);
        var candidates = previousCategory is { } previous
            ? GetConnectedCategories(previous).Where(tree.Categories.Contains).ToArray()
            : [];
        return candidates.Length > 0
            ? candidates[random.Next(candidates.Length)]
            : tree.Categories[random.Next(tree.Categories.Count)];
    }

    public static DialogueTree GetTreeForCategory(DialogueCategory category) =>
        category == DialogueCategory.EasterEgg
            ? GetTree(DialogueTreeKind.Companion)
            : Trees.Single(tree => tree.Categories.Contains(category));

    public static DialogueTree GetTreeForGroup(DialogueCategoryGroup group) => group switch
    {
        DialogueCategoryGroup.Technical => GetTree(DialogueTreeKind.Technical),
        DialogueCategoryGroup.Growth or DialogueCategoryGroup.Career => GetTree(DialogueTreeKind.Growth),
        DialogueCategoryGroup.CharacterLife => GetTree(DialogueTreeKind.Life),
        _ => GetTree(DialogueTreeKind.Companion)
    };

    public static IReadOnlyList<DialogueCategory> GetConnectedCategories(DialogueCategory category)
    {
        var tree = GetTreeForCategory(category);
        return Trees
            .Where(candidate => candidate.Kind == tree.Kind || Math.Abs((int)candidate.Kind - (int)tree.Kind) == 1)
            .SelectMany(candidate => candidate.Categories)
            .Where(candidate => candidate != category)
            .ToArray();
    }

    private static DialogueTree GetTree(DialogueTreeKind kind) => Trees.Single(tree => tree.Kind == kind);

    private static DialogueTree WeightedTree(Random random)
    {
        var roll = random.NextDouble();
        foreach (var (kind, weight) in TreeWeights)
        {
            roll -= weight;
            if (roll <= 0)
            {
                return GetTree(kind);
            }
        }

        return GetTree(DialogueTreeKind.Life);
    }
}
