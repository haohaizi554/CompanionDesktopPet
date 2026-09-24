using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class ControlMenuPlacementServiceTests
{
    private static readonly ScreenRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void Place_CharacterAtBottomRight_PutsTheMenuToTheLeftWithoutOverlap()
    {
        var character = new ScreenRect(1576, 696, 320, 320);
        var menu = new ScreenSize(294, 460);

        var actual = ControlMenuPlacementService.Place(character, menu, WorkArea);

        Assert.Equal(ControlMenuSide.Left, actual.Side);
        Assert.False(actual.SubmenuOpensRight);
        Assert.Equal(character.Left - ControlMenuPlacementService.GapDips - menu.Width, actual.Origin.X);
        Assert.Equal(WorkArea.Bottom - menu.Height, actual.Origin.Y);
        AssertNoOverlap(character, menu, actual.Origin);
    }

    [Fact]
    public void Place_CharacterAtBottomLeft_PutsTheMenuToTheRightWithoutOverlap()
    {
        var character = new ScreenRect(24, 696, 320, 320);
        var menu = new ScreenSize(294, 460);

        var actual = ControlMenuPlacementService.Place(character, menu, WorkArea);

        Assert.Equal(ControlMenuSide.Right, actual.Side);
        Assert.True(actual.SubmenuOpensRight);
        Assert.Equal(character.Right + ControlMenuPlacementService.GapDips, actual.Origin.X);
        Assert.Equal(WorkArea.Bottom - menu.Height, actual.Origin.Y);
        AssertNoOverlap(character, menu, actual.Origin);
    }

    [Fact]
    public void Place_NeitherSideFits_UsesTheRoomAboveTheCharacter()
    {
        var workArea = new ScreenRect(0, 0, 500, 900);
        var character = new ScreenRect(100, 400, 320, 200);
        var menu = new ScreenSize(300, 150);

        var actual = ControlMenuPlacementService.Place(character, menu, workArea);

        Assert.Equal(ControlMenuSide.Above, actual.Side);
        Assert.Equal(character.Top - ControlMenuPlacementService.GapDips - menu.Height, actual.Origin.Y);
        Assert.Equal(110, actual.Origin.X);
        AssertNoOverlap(character, menu, actual.Origin);
    }

    private static void AssertNoOverlap(ScreenRect character, ScreenSize menu, ScreenPoint origin)
    {
        var menuRect = new ScreenRect(origin.X, origin.Y, menu.Width, menu.Height);
        var separated =
            menuRect.Right <= character.Left
            || menuRect.Left >= character.Right
            || menuRect.Bottom <= character.Top
            || menuRect.Top >= character.Bottom;
        Assert.True(separated);
    }
}
