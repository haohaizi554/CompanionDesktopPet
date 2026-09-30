using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class PetReminderStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pet-reminders-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Accept_KeepsFourAndSpeaksOnlyAFreshDueReminder()
    {
        var store = new PetReminderStore(_directory);
        var now = new DateTime(2026, 9, 29, 14, 0, 0, DateTimeKind.Utc);
        store.Accept(
            """
            [
              {"skill":"set_bubble","seconds":9},
              {"skill":"remind","minutes":10,"text":"到点了，喝水。"},
              {"skill":"remind","minutes":20,"text":"到点了，休息。"},
              {"skill":"remind","minutes":30,"text":"到点了，散步。"},
              {"skill":"remind","minutes":40,"text":"到点了，关电脑。"},
              {"skill":"remind","minutes":50,"text":"到点了，这条该被挤掉。"}
            ]
            """,
            now);

        Assert.Equal(4, store.Count);
        Assert.Null(store.TakeDue(now.AddMinutes(9)));
        Assert.Equal("到点了，休息。", store.TakeDue(now.AddMinutes(20)));
        Assert.Equal(3, store.Count);

        var later = new PetReminderStore(_directory);
        Assert.Equal("到点了，散步。", later.TakeDue(now.AddMinutes(30)));
    }

    [Fact]
    public void TakeDue_DropsAReminderThatWasMissedForTooLong()
    {
        var store = new PetReminderStore(_directory);
        var now = new DateTime(2026, 9, 29, 14, 0, 0, DateTimeKind.Utc);
        store.Accept("""[{"skill":"remind","minutes":1,"text":"到点了。"}]""", now);

        Assert.Null(store.TakeDue(now.AddHours(8)));
        Assert.Equal(0, store.Count);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
