using System.IO;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal static class PersonaRelationship
{
    private static readonly string[] Allowed =
    [
        "她是对方的女朋友",
        "她是对方的老婆",
        "她是对方的伴侣",
        "她是对方的朋友"
    ];

    internal static string Load(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet",
            "dialogue");
        var path = Path.Combine(root, "persona.json");
        try
        {
            if (!File.Exists(path))
            {
                return "";
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("relationship", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return "";
            }

            var relationship = value.GetString();
            return Allowed.Contains(relationship) ? relationship! : "";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return "";
        }
    }
}
