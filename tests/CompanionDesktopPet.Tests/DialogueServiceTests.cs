using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueServiceTests
{
    [Theory]
    [InlineData(7, "早上好呀，今天也一起加油 ♡")]
    [InlineData(13, "下午好，要记得喝水哦 ♡")]
    [InlineData(19, "晚上好，辛苦一天啦 ♡")]
    [InlineData(1, "这么晚还没睡呀？要照顾好自己哦")]
    public void GetGreeting_UsesLocalHour(int hour, string expected)
    {
        var service = new DialogueService();
        Assert.Equal(expected, service.GetGreeting(new DateTime(2026, 7, 22, hour, 0, 0)));
    }

    [Fact]
    public void GetNextPhrase_DoesNotImmediatelyRepeat()
    {
        var service = new DialogueService();
        var random = new Random(1234);
        var previous = service.GetNextPhrase(random);

        for (var index = 0; index < 30; index++)
        {
            var next = service.GetNextPhrase(random);
            Assert.NotEqual(previous, next);
            previous = next;
        }
    }
}
