namespace CompanionDesktopPet.Services;

public sealed class SpokenLineBridge
{
    public const int Limit = 6;
    public const int MaxChars = 120;

    private readonly Queue<string> _pending = new();
    private string? _lastNoted;

    public bool HasPending => _pending.Count > 0;

    public void Note(string? text)
    {
        var cleaned = Clean(text);
        if (cleaned.Length == 0 || string.Equals(cleaned, _lastNoted, StringComparison.Ordinal))
        {
            return;
        }

        _lastNoted = cleaned;
        _pending.Enqueue(cleaned);
        while (_pending.Count > Limit)
        {
            _pending.Dequeue();
        }
    }

    public IReadOnlyList<string> Take()
    {
        var batch = _pending.ToArray();
        _pending.Clear();
        return batch;
    }

    public void RestoreFront(IReadOnlyList<string> lines)
    {
        var newer = _pending.ToArray();
        _pending.Clear();
        foreach (var line in lines.Concat(newer).TakeLast(Limit))
        {
            var cleaned = Clean(line);
            if (cleaned.Length > 0)
            {
                _pending.Enqueue(cleaned);
            }
        }
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= MaxChars ? cleaned : cleaned[..MaxChars].TrimEnd();
    }
}
