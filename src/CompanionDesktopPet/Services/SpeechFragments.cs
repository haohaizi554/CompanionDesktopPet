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

    internal static string DropLeadingRepeat(string? text, string? prior)
    {
        var cleaned = Collapse(text);
        var spoken = SentenceKeys(prior);
        if (cleaned.Length == 0 || spoken.Count == 0)
        {
            return cleaned;
        }

        var kept = new List<string>();
        var rest = cleaned;
        var removed = false;
        while (rest.Length > 0)
        {
            var end = FirstSentenceEnd(rest);
            var piece = rest[..end].Trim();
            var key = SentenceKey(piece);
            if (key.Length >= 4 && spoken.Contains(key))
            {
                removed = true;
            }
            else if (piece.Length > 0)
            {
                kept.Add(piece);
            }

            rest = end >= rest.Length ? string.Empty : rest[end..].TrimStart();
        }

        return removed ? string.Concat(kept) : cleaned;
    }

    private static HashSet<string> SentenceKeys(string? text)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var rest = Collapse(text);
        while (rest.Length > 0)
        {
            var end = FirstSentenceEnd(rest);
            var key = SentenceKey(rest[..end]);
            if (key.Length >= 4)
            {
                keys.Add(key);
            }

            if (end >= rest.Length)
            {
                break;
            }

            rest = rest[end..].TrimStart();
        }

        return keys;
    }

    private static string Collapse(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SentenceKey(string? text) =>
        Collapse(text).TrimEnd('。', '！', '？', '!', '?', '…').Trim();

    private static int FirstSentenceEnd(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '。' or '！' or '？' or '!' or '?')
            {
                return index + 1;
            }
        }

        return text.Length;
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
