namespace CompanionDesktopPet.Services;

public sealed class DialogueSendQueue
{
    public const int Capacity = 8;

    private readonly Queue<string> _items = new();

    public int Count => _items.Count;

    public bool TryEnqueue(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _items.Count >= Capacity)
        {
            return false;
        }

        _items.Enqueue(text.Trim());
        return true;
    }

    public bool TryDequeue(out string text)
    {
        if (_items.Count == 0)
        {
            text = string.Empty;
            return false;
        }

        text = _items.Dequeue();
        return true;
    }

    public void Clear() => _items.Clear();
}
