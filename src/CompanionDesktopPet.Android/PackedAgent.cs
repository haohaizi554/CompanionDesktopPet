using System.Text.Json;
using Android.Content;
using CompanionDesktopPet.Services;
using Java.Lang;

namespace CompanionDesktopPet.Android;

internal static class PackedAgent
{
    public static string? TryReply(
        Context context,
        string stateDirectory,
        string text,
        IReadOnlyDictionary<string, object?>? settings = null)
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
            EnsureRelationship(context, stateDirectory);
            if (settings is not null)
            {
                File.WriteAllText(
                    Path.Combine(stateDirectory, "phone-settings.json"),
                    JsonSerializer.Serialize(settings));
            }
            var types = new[]
            {
                Class.FromType(typeof(Context)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string))
            };
            var method = Class.ForName("com.jiayi.agent.JiayiAgent")?.GetMethod("reply", types);
            var value = method?.Invoke(null, new Java.Lang.Object[]
            {
                context,
                new Java.Lang.String(corpus),
                new Java.Lang.String(soul),
                new Java.Lang.String(stateDirectory),
                new Java.Lang.String(text),
                new Java.Lang.String(LinkDefaults.ModelUrl),
                new Java.Lang.String(LinkDefaults.ModelName),
                new Java.Lang.String(LinkDefaults.ApiKey)
            });
            return value?.ToString();
        }
        catch (System.Exception exception)
        {
            global::Android.Util.Log.Warn("jiayi", "packed agent " + exception.Message);
            return null;
        }
    }

    public static bool TryRemember(Context context, string stateDirectory, string text)
    {
        try
        {
            EnsureRelationship(context, stateDirectory);
            var types = new[]
            {
                Class.FromType(typeof(Context)),
                Class.FromType(typeof(string)),
                Class.FromType(typeof(string))
            };
            var method = Class.ForName("com.jiayi.agent.JiayiAgent")?.GetMethod("remember", types);
            var value = method?.Invoke(null, new Java.Lang.Object[]
            {
                context,
                new Java.Lang.String(stateDirectory),
                new Java.Lang.String(text)
            });
            var payload = value?.ToString();
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

            return string.IsNullOrWhiteSpace(text) ? null : new PersonaDialogueReply(true, text, false, actions);
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

    private static void EnsureRelationship(Context context, string stateDirectory)
    {
        var destination = Path.Combine(stateDirectory, "persona-setting.json");
        if (File.Exists(destination) && File.ReadAllText(destination).Contains("她是对方的", StringComparison.Ordinal))
        {
            return;
        }

        using var source = context.Assets?.Open("agent/persona.json");
        if (source is null)
        {
            return;
        }

        using var output = File.Create(destination);
        source.CopyTo(output);
    }
}
