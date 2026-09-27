using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialoguePlacementServiceTests
{
    [Fact]
    public void Place_RoomBelow_StaysBelowEvenWhenAboveIsOpen()
    {
        var character = new ScreenRect(800, 400, 320, 320);
        var panel = new ScreenSize(276, 64);

        var actual = DialoguePlacementService.Place(
            character,
            panel,
            new ScreenRect(0, 0, 1920, 1040));

        Assert.Equal(DialoguePlacementSide.Below, actual.Side);
        Assert.Equal(character.Bottom + BubblePlacementService.GapDips, actual.Origin.Y, 0);
    }

    [Fact]
    public void Place_NoRoomBelow_UsesTheLeftBeforeMovingAbove()
    {
        var character = new ScreenRect(1500, 900, 320, 140);
        var panel = new ScreenSize(276, 64);

        var actual = DialoguePlacementService.Place(
            character,
            panel,
            new ScreenRect(0, 0, 1920, 1040));

        Assert.Equal(DialoguePlacementSide.Left, actual.Side);
        Assert.True(actual.Origin.X + panel.Width <= character.Left);
    }

    [Fact]
    public void Place_OnlyTheRightSideFits_UsesRight()
    {
        var character = new ScreenRect(0, 900, 320, 140);
        var panel = new ScreenSize(276, 64);

        var actual = DialoguePlacementService.Place(
            character,
            panel,
            new ScreenRect(0, 0, 1920, 1040));

        Assert.Equal(DialoguePlacementSide.Right, actual.Side);
        Assert.True(actual.Origin.X >= character.Right);
    }
}
