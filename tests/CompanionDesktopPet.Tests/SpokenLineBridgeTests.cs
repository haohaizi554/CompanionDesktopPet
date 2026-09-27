using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SpokenLineBridgeTests
{
    [Fact]
    public void Note_keeps_recent_distinct_lines_and_restore_puts_unsent_first()
    {
        var bridge = new SpokenLineBridge();
        bridge.Note("  湿热的天气把树叶养得很绿。  ");
        bridge.Note("湿热的天气把树叶养得很绿。");
        bridge.Note("日程里留一点缓冲。");

        var taken = bridge.Take();
        Assert.Equal(
            ["湿热的天气把树叶养得很绿。", "日程里留一点缓冲。"],
            taken);
        Assert.False(bridge.HasPending);

        bridge.Note("先把错误留在日志里。");
        bridge.RestoreFront(["日程里留一点缓冲。"]);

        Assert.Equal(
            ["日程里留一点缓冲。", "先把错误留在日志里。"],
            bridge.Take());
    }

    [Fact]
    public void Note_drops_lines_beyond_the_recent_window()
    {
        var bridge = new SpokenLineBridge();
        for (var index = 0; index < SpokenLineBridge.Limit + 2; index++)
        {
            bridge.Note($"第{index}句");
        }

        var taken = bridge.Take();
        Assert.Equal(SpokenLineBridge.Limit, taken.Count);
        Assert.Equal("第2句", taken[0]);
        Assert.Equal("第7句", taken[^1]);
    }
}
