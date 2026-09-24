namespace CompanionDesktopPet.Services;

public sealed class CorpusFolder
{
    public CorpusFolder(string title, DialogueLine[] lines, CorpusFolder[] children)
    {
        Title = title;
        Lines = lines;
        Children = children;
    }

    public string Title { get; }

    public DialogueLine[] Lines { get; }

    public CorpusFolder[] Children { get; }

    public string Header => $"{Title}  {Lines.Length}";
}

public static class CorpusLabels
{
    public static string Group(DialogueCategoryGroup group) => group switch
    {
        DialogueCategoryGroup.Technical => "技术",
        DialogueCategoryGroup.Growth => "成长",
        DialogueCategoryGroup.Career => "职业",
        DialogueCategoryGroup.DailyCare => "日常关心",
        DialogueCategoryGroup.EmotionalReflection => "情绪",
        DialogueCategoryGroup.CharacterLife => "角色日常",
        DialogueCategoryGroup.EasterEgg => "彩蛋",
        DialogueCategoryGroup.SystemAmbient => "环境",
        _ => group.ToString()
    };

    public static string Category(DialogueCategory category) => category switch
    {
        DialogueCategory.Debugging => "调试",
        DialogueCategory.Python => "Python",
        DialogueCategory.Java => "Java",
        DialogueCategory.Cpp => "C++",
        DialogueCategory.Frontend => "前端",
        DialogueCategory.Backend => "后端",
        DialogueCategory.Database => "数据库",
        DialogueCategory.Algorithms => "算法",
        DialogueCategory.Systems => "系统",
        DialogueCategory.Networks => "网络",
        DialogueCategory.GitDevOps => "Git 与运维",
        DialogueCategory.Architecture => "架构",
        DialogueCategory.Study => "学习",
        DialogueCategory.Career => "职业",
        DialogueCategory.DailyCare => "日常关心",
        DialogueCategory.EmotionalSupport => "情绪支持",
        DialogueCategory.EnglishPractice => "英语",
        DialogueCategory.ProactiveChat => "主动聊天",
        DialogueCategory.WanderingLife => "闲逛",
        DialogueCategory.DressesHobbies => "穿搭爱好",
        DialogueCategory.EasterEgg => "彩蛋",
        DialogueCategory.CharacterLife => "角色日常",
        DialogueCategory.SystemAmbient => "环境",
        _ => category.ToString()
    };
}

public static class CorpusBrowserIndex
{
    private static readonly Lazy<CorpusFolder> Snapshot = new(Build);

    public static CorpusFolder Root => Snapshot.Value;

    private static CorpusFolder Build()
    {
        var lines = PersonaCorpus.All;
        var byGroup = new Dictionary<DialogueCategoryGroup, List<DialogueLine>>();
        foreach (var line in lines)
        {
            Add(byGroup, line.CategoryGroup, line);
        }

        var children = new List<CorpusFolder>();
        foreach (var group in Enum.GetValues<DialogueCategoryGroup>())
        {
            if (byGroup.TryGetValue(group, out var groupLines))
            {
                children.Add(BuildGroup(group, groupLines));
            }
        }

        return new CorpusFolder("全库", lines.ToArray(), children.ToArray());
    }

    private static CorpusFolder BuildGroup(
        DialogueCategoryGroup group,
        List<DialogueLine> lines)
    {
        var byCategory = new Dictionary<DialogueCategory, List<DialogueLine>>();
        foreach (var line in lines)
        {
            Add(byCategory, line.Category, line);
        }

        var children = new List<CorpusFolder>();
        foreach (var category in Enum.GetValues<DialogueCategory>())
        {
            if (byCategory.TryGetValue(category, out var categoryLines))
            {
                children.Add(BuildCategory(category, categoryLines));
            }
        }

        return new CorpusFolder(CorpusLabels.Group(group), lines.ToArray(), children.ToArray());
    }

    private static CorpusFolder BuildCategory(
        DialogueCategory category,
        List<DialogueLine> lines)
    {
        var byTopic = new Dictionary<string, List<DialogueLine>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            Add(byTopic, line.TopicId, line);
        }

        var topicIds = byTopic.Keys.ToArray();
        return new CorpusFolder(
            CorpusLabels.Category(category),
            lines.ToArray(),
            Fold(byTopic, (topicId, topicLines) => BuildTopic(topicId, topicLines, topicIds)));
    }

    private static CorpusFolder BuildTopic(
        string topicId,
        List<DialogueLine> lines,
        IReadOnlyList<string> topicSiblings)
    {
        var byScene = new Dictionary<string, List<DialogueLine>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            Add(byScene, line.SemanticGroup, line);
        }

        var sceneIds = byScene.Keys.ToArray();
        var scenes = Fold(byScene, (sceneId, sceneLines) => new CorpusFolder(
            DisplayId(sceneId, sceneIds),
            sceneLines.ToArray(),
            []));
        // A topic with one scene repeats the same label as an extra menu and tree row.
        if (scenes.Length == 1)
        {
            return new CorpusFolder(DisplayId(topicId, topicSiblings), lines.ToArray(), []);
        }

        return new CorpusFolder(DisplayId(topicId, topicSiblings), lines.ToArray(), scenes);
    }

    private static CorpusFolder[] Fold(
        Dictionary<string, List<DialogueLine>> map,
        Func<string, List<DialogueLine>, CorpusFolder> build)
    {
        var keys = map.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);
        var folders = new CorpusFolder[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            folders[index] = build(keys[index], map[keys[index]]);
        }

        return folders;
    }

    private static string DisplayId(string id, IReadOnlyList<string> siblings)
    {
        var leaf = Leaf(id);
        var collisions = 0;
        foreach (var sibling in siblings)
        {
            if (Leaf(sibling) == leaf)
            {
                collisions++;
            }
        }

        return collisions > 1 ? id : leaf;
    }

    private static string Leaf(string id)
    {
        var dot = id.LastIndexOf('.');
        return dot >= 0 && dot < id.Length - 1 ? id[(dot + 1)..] : id;
    }

    private static void Add<TKey>(
        Dictionary<TKey, List<DialogueLine>> map,
        TKey key,
        DialogueLine line)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(line);
    }
}
