namespace CompanionDesktopPet.Services;

public sealed record StoryArcDefinition(string Id, string Name, IReadOnlyList<SceneDefinition> Nodes);

public static class StoryArcCatalog
{
    public static IReadOnlyList<StoryArcDefinition> All { get; } = Build();

    private static IReadOnlyList<StoryArcDefinition> Build()
    {
        var source = SceneCatalog.PersonaScenes
            .Where(scene => scene.CategoryGroup is DialogueCategoryGroup.CharacterLife
                or DialogueCategoryGroup.Growth
                or DialogueCategoryGroup.Career)
            .Where(scene => scene.DialogueTrigger == DialogueTrigger.Any
                            && scene.RequiredContext.Count == 1
                            && scene.RequiredContext[0] == "none")
            .OrderBy(scene => scene.CategoryGroup == DialogueCategoryGroup.CharacterLife ? 0 : 1)
            .ThenBy(scene => scene.Id, StringComparer.Ordinal)
            .Take(30)
            .ToArray();
        if (source.Length < 30)
        {
            throw new InvalidOperationException("The enabled v2 corpus does not contain enough story-safe semantic groups.");
        }

        return source
            .Chunk(3)
            .Select((chunk, arcIndex) =>
            {
                var arcId = $"v2_story_{arcIndex + 1:D2}";
                var nodes = chunk.Select((scene, nodeIndex) => SceneCatalog.CreateScene(
                    $"story:{arcId}:{nodeIndex}",
                    scene.Lines,
                    arcId,
                    nodeIndex)).ToArray();
                return new StoryArcDefinition(arcId, $"生活片段 {arcIndex + 1:D2}", nodes);
            })
            .ToArray();
    }
}
