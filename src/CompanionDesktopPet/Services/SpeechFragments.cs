using System.Text.RegularExpressions;

namespace CompanionDesktopPet.Services;

internal static partial class SpeechFragments
{
    internal static IReadOnlyList<string> SplitForSpeech(string text)
    {
        var cleaned = Asides().Replace(text ?? string.Empty, string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            return [];
        }

        var parts = new List<string>();
        foreach (var piece in SentenceBreaks().Split(cleaned))
        {
            var item = piece.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (parts.Count > 0 && ContentLength(item) < 4)
            {
                parts[^1] += item;
            }
            else
            {
                parts.Add(item);
            }
        }

        if (parts.Count <= 1)
        {
            return [cleaned];
        }

        if (parts.Count > 8)
        {
            var tail = string.Concat(parts.Skip(7));
            parts.RemoveRange(7, parts.Count - 7);
            parts.Add(tail);
        }

        return parts;
    }

    internal static bool BelongsTo(string? piece, string? requested)
    {
        if (requested is null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(piece))
        {
            return false;
        }

        if (string.Equals(piece, requested, StringComparison.Ordinal))
        {
            return true;
        }

        return SplitForSpeech(requested).Contains(piece);
    }

    private static int ContentLength(string text) =>
        text.Count(character => char.IsLetterOrDigit(character) || character is >= '\u4e00' and <= '\u9fff');

    [GeneratedRegex(@"\*[^*]*\*|＊[^＊]*＊|（[^）]*）|\([^)]*\)")]
    private static partial Regex Asides();

    [GeneratedRegex(@"(?<=[。！？!?])")]
    private static partial Regex SentenceBreaks();
}
