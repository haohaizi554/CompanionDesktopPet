using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal sealed class PetReminderStore
{
    public const int Limit = 4;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly List<Entry> _items = [];

    public PetReminderStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "reminders.json");
        Load();
    }

    public int Count
    {
        get
        {
            lock (_items)
            {
                return _items.Count;
            }
        }
    }

    public void Accept(string? actionsJson, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(actionsJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(actionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var changed = false;
            foreach (var action in document.RootElement.EnumerateArray())
            {
                changed |= TryAdd(action, nowUtc);
            }

            if (changed)
            {
                Save();
            }
        }
        catch (JsonException)
        {
        }
    }

    public string? TakeDue(DateTime nowUtc)
    {
        string? spoken = null;
        var changed = false;
        lock (_items)
        {
            for (var index = 0; index < _items.Count;)
            {
                var item = _items[index];
                if (item.DueUtc > nowUtc)
                {
                    index++;
                    continue;
                }

                var missed = nowUtc - item.DueUtc > StaleAfter;
                if (missed)
                {
                    _items.RemoveAt(index);
                    changed = true;
                    continue;
                }

                if (spoken is null)
                {
                    spoken = item.Text;
                    _items.RemoveAt(index);
                    changed = true;
                    continue;
                }

                index++;
            }
        }

        if (changed)
        {
            Save();
        }

        return spoken;
    }

    private bool TryAdd(JsonElement action, DateTime nowUtc)
    {
        if (action.ValueKind != JsonValueKind.Object
            || !action.TryGetProperty("skill", out var skill)
            || skill.GetString() != "remind"
            || !action.TryGetProperty("minutes", out var minutesValue)
            || !minutesValue.TryGetInt32(out var minutes)
            || minutes is < 1 or > 24 * 60
            || !action.TryGetProperty("text", out var textValue)
            || textValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = textValue.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
        {
            return false;
        }

        var due = nowUtc.AddMinutes(minutes);
        lock (_items)
        {
            _items.Add(new Entry(due, text));
            while (_items.Count > Limit)
            {
                _items.RemoveAt(0);
            }
        }

        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("due", out var dueValue)
                    || dueValue.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("text", out var textValue)
                    || textValue.ValueKind != JsonValueKind.String
                    || !DateTime.TryParse(
                        dueValue.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var due))
                {
                    continue;
                }

                var text = textValue.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
                {
                    continue;
                }

                _items.Add(new Entry(due.ToUniversalTime(), text));
                if (_items.Count == Limit)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
        }
    }

    private void Save()
    {
        List<Entry> copy;
        lock (_items)
        {
            copy = _items.ToList();
        }

        var payload = copy.Select(item => new StoredReminder(
            item.DueUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            item.Text)).ToArray();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    private sealed record Entry(DateTime DueUtc, string Text);

    private sealed record StoredReminder(string Due, string Text);
}
