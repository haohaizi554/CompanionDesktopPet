using System.Text.Json;
using Android.Content;
using CompanionDesktopPet.Services;
using Java.Lang;

namespace CompanionDesktopPet.Android;

internal static class PackedAgent
{
    private static readonly object Gate = new();

    public static string? TryReply(
        Context context,
        string stateDirectory,
        string text,
        IReadOnlyDictionary<string, object?>? settings = null)
    {
        lock (Gate)
        {
            return ReplyUnlocked(context, stateDirectory, text, settings);
        }
    }

    private static string? ReplyUnlocked(
        Context context,
        string stateDirectory,
        string text,
        IReadOnlyDictionary<string, object?>? settings)
    {
        try
        {
            var files = context.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(files))
            {
                return null;
            }

            var corpus = Path.Combine(files, "persona-corpus-v2.tsv");
            var soul = Path.Combine(files, "jiayi-soul.json");
            EnsureCorpus(corpus);
            EnsureSoul(context, soul);
            MigrateCachedState(context, stateDirectory);
            EnsureRelationship(context, stateDirectory);
            if (settings is not null)
            {
                File.WriteAllText(
                    Path.Combine(stateDirectory, "phone-settings.json"),
                    JsonSerializer.Serialize(settings));
            }
            return Call(
                context,
                "reply",
                context,
                new Java.Lang.String(corpus),
                new Java.Lang.String(soul),
                new Java.Lang.String(stateDirectory),
                new Java.Lang.String(text),
                new Java.Lang.String(LinkDefaults.ModelUrl),
                new Java.Lang.String(LinkDefaults.ModelName),
                new Java.Lang.String(LinkDefaults.ApiKey));
        }
        catch (System.Exception exception)
        {
            global::Android.Util.Log.Warn("jiayi", "packed agent " + exception);
            return null;
        }
    }

    public static bool TryRemember(Context context, string stateDirectory, string text)
    {
        lock (Gate)
        {
            return RememberUnlocked(context, stateDirectory, text);
        }
    }

    private static bool RememberUnlocked(Context context, string stateDirectory, string text)
    {
        try
        {
            MigrateCachedState(context, stateDirectory);
            EnsureRelationship(context, stateDirectory);
            var payload = Call(
                context,
                "remember",
                context,
                new Java.Lang.String(stateDirectory),
                new Java.Lang.String(text));
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (System.Exception exception)
        {
            global::Android.Util.Log.Warn("jiayi", "packed remember " + exception.Message);
            return false;
        }
    }

    public static PersonaDialogueReply? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var ok))
            {
                return null;
            }

            var text = root.TryGetProperty("text", out var textValue) ? textValue.GetString() : null;
            var actions = root.TryGetProperty("actions", out var actionsValue) ? actionsValue.GetRawText() : "[]";
            var accepted = ok.ValueKind == JsonValueKind.True;
            if (!accepted)
            {
                return new PersonaDialogueReply(false, string.IsNullOrWhiteSpace(text) ? "这句话我没接住。" : text, false, actions);
            }

            return new PersonaDialogueReply(
                !string.IsNullOrWhiteSpace(text),
                string.IsNullOrWhiteSpace(text) ? "这句话我没接住。" : text,
                false,
                actions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnsureCorpus(string destination)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
        {
            return;
        }

        using var source = typeof(PackedAgent).Assembly.GetManifestResourceStream("CompanionDesktopPet.Assets.persona-corpus-v2.tsv");
        if (source is null)
        {
            return;
        }

        using var output = File.Create(destination);
        source.CopyTo(output);
    }

    private static void EnsureSoul(Context context, string destination)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
        {
            return;
        }

        using var source = context.Assets?.Open("agent/jiayi-soul.json");
        if (source is null)
        {
            return;
        }

        using var output = File.Create(destination);
        source.CopyTo(output);
    }

    public static string Relationship(string stateDirectory)
    {
        return ReadRelationship(Path.Combine(stateDirectory, "persona-setting.json"));
    }

    public static List<(string Role, string Text)> Transcript(string stateDirectory)
    {
        var turns = new List<(string Role, string Text)>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(stateDirectory, "phone-agent.json")));
            if (!document.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                return turns;
            }

            foreach (var message in messages.EnumerateArray())
            {
                var role = message.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : "assistant";
                var text = message.TryGetProperty("text", out var textValue) ? textValue.GetString() : "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                turns.Add((role == "user" ? "user" : "assistant", text.Trim()));
            }
        }
        catch (System.Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return turns;
    }

    public static void AppendExchange(string stateDirectory, string user, string assistant)
    {
        lock (Gate)
        {
            AppendExchangeUnlocked(stateDirectory, user, assistant);
        }
    }

    private static void AppendExchangeUnlocked(string stateDirectory, string user, string assistant)
    {
        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, "phone-agent.json");
        var messages = new List<Dictionary<string, string>>();
        var memory = "";
        var facts = new List<string>();
        var cover = 0;
        JsonElement? settings = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            memory = root.TryGetProperty("memory", out var memoryValue) ? memoryValue.GetString() ?? "" : "";
            if (root.TryGetProperty("settings", out var settingsValue) && settingsValue.ValueKind == JsonValueKind.Object)
            {
                settings = settingsValue.Clone();
            }
            cover = root.TryGetProperty("cover", out var coverValue) && coverValue.TryGetInt32(out var parsed) ? parsed : 0;
            if (root.TryGetProperty("facts", out var factValue) && factValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var fact in factValue.EnumerateArray())
                {
                    var line = fact.GetString();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        facts.Add(line);
                    }
                }
            }

            if (root.TryGetProperty("messages", out var existing) && existing.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in existing.EnumerateArray())
                {
                    var role = message.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : "assistant";
                    var text = message.TryGetProperty("text", out var textValue) ? textValue.GetString() : "";
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        messages.Add(new Dictionary<string, string> { ["role"] = role ?? "assistant", ["text"] = text });
                    }
                }
            }
        }
        catch (System.Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        if (messages.Count == 0 || messages[^1]["text"] != user)
        {
            messages.Add(new Dictionary<string, string> { ["role"] = "user", ["text"] = user });
        }

        if (!string.IsNullOrWhiteSpace(assistant))
        {
            messages.Add(new Dictionary<string, string> { ["role"] = "assistant", ["text"] = assistant });
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["messages"] = messages,
            ["facts"] = facts,
            ["memory"] = memory,
            ["cover"] = cover,
            ["settings"] = settings.HasValue ? settings.Value : new Dictionary<string, object?>(),
            ["prior"] = assistant
        });
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, path, overwrite: true);
    }

    private static string? Call(Context context, string name, params Java.Lang.Object[] arguments)
    {
        var type = context.ClassLoader?.LoadClass("com.jiayi.agent.JiayiAgent")
            ?? Class.ForName("com.jiayi.agent.JiayiAgent");
        var method = type?.GetDeclaredMethods()?.FirstOrDefault(candidate => candidate.Name == name);
        if (method is null)
        {
            return null;
        }

        method.Accessible = true;
        return method.Invoke(null, arguments)?.ToString();
    }

    private static void MigrateCachedState(Context context, string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        var sources = new[] { context.CacheDir?.AbsolutePath, context.ExternalCacheDir?.AbsolutePath };
        foreach (var name in new[] { "phone-agent.json", "persona-setting.json" })
        {
            var destination = Path.Combine(stateDirectory, name);
            if (File.Exists(destination) && new FileInfo(destination).Length > 2)
            {
                continue;
            }

            foreach (var sourceRoot in sources)
            {
                if (string.IsNullOrWhiteSpace(sourceRoot))
                {
                    continue;
                }

                var source = Path.Combine(sourceRoot, name);
                if (!File.Exists(source) || new FileInfo(source).Length <= 2)
                {
                    continue;
                }

                File.Copy(source, destination, overwrite: true);
                break;
            }
        }
    }

    private static void EnsureRelationship(Context context, string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        var destination = Path.Combine(stateDirectory, "persona-setting.json");
        if (ReadRelationship(destination).StartsWith("她是对方的", StringComparison.Ordinal))
        {
            return;
        }

        var json = "{\"relationship\":\"她是对方的女朋友\"}";
        try
        {
            using var source = context.Assets?.Open("agent/persona.json");
            if (source is not null)
            {
                using var reader = new StreamReader(source);
                var packaged = reader.ReadToEnd();
                if (packaged.Contains("她是对方的", StringComparison.Ordinal))
                {
                    json = packaged;
                }
            }
        }
        catch (System.Exception exception) when (exception is IOException or Java.Lang.Throwable)
        {
        }

        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, destination, overwrite: true);
    }

    private static string ReadRelationship(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("relationship", out var value) ? value.GetString() ?? "" : "";
        }
        catch (System.Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
