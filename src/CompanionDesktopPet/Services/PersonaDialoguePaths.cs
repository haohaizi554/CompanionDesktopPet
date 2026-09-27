using System.IO;

namespace CompanionDesktopPet.Services;

internal sealed record PersonaDialoguePaths(
    string PythonPath,
    string ServePath,
    string SoulPath,
    string CorpusPath,
    string ConfigPath,
    string CheckpointPath)
{
    internal static PersonaDialoguePaths? Resolve(string? startDirectory = null)
    {
        var root = FindRoot(startDirectory ?? AppContext.BaseDirectory);
        if (root is null)
        {
            return null;
        }

        var python = FirstFile(
            Path.Combine(root, "dialogue", "python", "Scripts", "python.exe"),
            Path.Combine(root, "dialogue", "python", "python.exe"));
        var serve = Path.Combine(root, "dialogue", "serve.py");
        var soul = Path.Combine(root, "data", "persona", "jiayi-soul.json");
        var corpus = Path.Combine(root, "data", "optimized", "persona-corpus-v2.tsv");
        var config = FirstFile(
            Path.Combine(DialogueDirectory(), "llm.runtime.json"),
            Path.Combine(root, "config", "llm.runtime.json"));
        if (python is null || config is null || !File.Exists(serve) || !File.Exists(soul) || !File.Exists(corpus))
        {
            return null;
        }

        return new PersonaDialoguePaths(
            python,
            serve,
            soul,
            corpus,
            config,
            Path.Combine(DialogueDirectory(), "checkpoints.sqlite"));
    }

    internal static string UserConfigPath => Path.Combine(DialogueDirectory(), "llm.runtime.json");

    internal static bool RuntimeInstalled(string? startDirectory = null)
    {
        var root = FindRoot(startDirectory ?? AppContext.BaseDirectory);
        if (root is null)
        {
            return false;
        }

        var python = FirstFile(
            Path.Combine(root, "dialogue", "python", "Scripts", "python.exe"),
            Path.Combine(root, "dialogue", "python", "python.exe"));
        return python is not null
            && File.Exists(Path.Combine(root, "dialogue", "serve.py"))
            && File.Exists(Path.Combine(root, "data", "persona", "jiayi-soul.json"))
            && File.Exists(Path.Combine(root, "data", "optimized", "persona-corpus-v2.tsv"));
    }

    internal static string? ExistingConfigPath(string? startDirectory = null)
    {
        if (File.Exists(UserConfigPath))
        {
            return UserConfigPath;
        }

        var root = FindRoot(startDirectory ?? AppContext.BaseDirectory);
        if (root is null)
        {
            return null;
        }

        var installed = Path.Combine(root, "config", "llm.runtime.json");
        return File.Exists(installed) ? installed : null;
    }

    private static string DialogueDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet",
            "dialogue");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string? FindRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dialogue", "serve.py")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FirstFile(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
