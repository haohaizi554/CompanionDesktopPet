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

    public int Count => Lines.Length > 0 ? Lines.Length : CountChildren();

    public string Header => $"{Title}  {Count}";

    private int CountChildren()
    {
        var total = 0;
        foreach (var child in Children)
        {
            total += child.Count;
        }

        return total;
    }
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

    private static readonly Lazy<CorpusFolder> OutlineSnapshot = new(BuildOutline);

    public static CorpusFolder Outline => OutlineSnapshot.Value;

    private static CorpusFolder BuildOutline()
    {
        var grouped = new Dictionary<DialogueCategoryGroup, Dictionary<DialogueCategory, List<DialogueLine>>>();
        foreach (var line in PersonaCorpus.All)
        {
            if (!grouped.TryGetValue(line.CategoryGroup, out var categories))
            {
                categories = [];
                grouped[line.CategoryGroup] = categories;
            }

            if (!categories.TryGetValue(line.Category, out var bucket))
            {
                bucket = [];
                categories[line.Category] = bucket;
            }

            bucket.Add(line);
        }

        var children = new List<CorpusFolder>();
        foreach (var group in Enum.GetValues<DialogueCategoryGroup>())
        {
            if (!grouped.TryGetValue(group, out var categories))
            {
                continue;
            }

            var categoryFolders = new List<CorpusFolder>();
            foreach (var category in Enum.GetValues<DialogueCategory>())
            {
                if (categories.TryGetValue(category, out var bucket))
                {
                    categoryFolders.Add(new CorpusFolder(CorpusLabels.Category(category), bucket.ToArray(), []));
                }
            }

            children.Add(new CorpusFolder(CorpusLabels.Group(group), [], categoryFolders.ToArray()));
        }

        return new CorpusFolder("全库", [], children.ToArray());
    }

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
        Array.Sort(topicIds, StringComparer.Ordinal);
        var byFamily = new Dictionary<string, List<DialogueLine>>(StringComparer.Ordinal);
        foreach (var topicId in topicIds)
        {
            var topicLines = byTopic[topicId];
            var family = CorpusMenuTaxonomy.Assign(category, topicId, topicLines);
            if (!byFamily.TryGetValue(family, out var familyLines))
            {
                familyLines = [];
                byFamily[family] = familyLines;
            }

            familyLines.AddRange(topicLines);
        }

        var children = new List<CorpusFolder>();
        foreach (var title in CorpusMenuTaxonomy.Order(category))
        {
            if (byFamily.Remove(title, out var familyLines))
            {
                children.Add(new CorpusFolder(title, familyLines.ToArray(), []));
            }
        }

        foreach (var pair in byFamily)
        {
            children.Add(new CorpusFolder(pair.Key, pair.Value.ToArray(), []));
        }

        return new CorpusFolder(CorpusLabels.Category(category), lines.ToArray(), children.ToArray());
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
