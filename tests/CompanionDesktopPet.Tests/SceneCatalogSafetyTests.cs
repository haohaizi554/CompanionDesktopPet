using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SceneCatalogSafetyTests
{
    [Fact]
    public void LoadPersonaScenes_PrimaryFailureReturnsFallbackWithoutPoisoningTheType()
    {
        var expected = new InvalidDataException("broken embedded corpus");
        var fallback = PersonaCorpus.All.Take(1).ToArray();
        Exception? reported = null;

        var result = SceneCatalog.LoadPersonaScenes(
            () => throw expected,
            () => fallback,
            reportFailure: exception => reported = exception);

        Assert.Same(expected, result.Failure);
        Assert.Same(expected, reported);
        var scene = Assert.Single(result.Scenes);
        Assert.Equal(fallback[0].SemanticGroup, scene.SemanticGroup);
    }

    [Fact]
    public void StoryArcBuild_InsufficientFallbackScenesDisablesStoriesInsteadOfThrowing()
    {
        var fallbackScenes = SceneCatalog.BuildPersonaScenes(PersonaCorpus.All.Take(1).ToArray());

        var arcs = StoryArcCatalog.Build(fallbackScenes);

        Assert.Empty(arcs);
    }
}
