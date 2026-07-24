using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class BubblePlacementServiceTests
{
    [Theory]
    [InlineData(250)]
    [InlineData(320)]
    [InlineData(390)]
    public void Place_CharacterAtTop_FlipsBubbleBelowWithThirtyDipGap(double characterSize)
    {
        var character = new ScreenRect(600, 0, characterSize, characterSize);

        var actual = BubblePlacementService.Place(
            character,
            new ScreenSize(276, 100),
            new ScreenRect(0, 0, 1920, 1040),
            BubblePlacementSide.Above);

        Assert.Equal(BubblePlacementSide.Below, actual.Side);
        Assert.Equal(character.Bottom + 30, actual.Origin.Y);
    }

    [Fact]
    public void Place_CharacterAtBottom_PlacesBubbleAboveWithThirtyDipGap()
    {
        var character = new ScreenRect(600, 720, 320, 320);

        var actual = BubblePlacementService.Place(
            character,
            new ScreenSize(276, 100),
            new ScreenRect(0, 0, 1920, 1040),
            BubblePlacementSide.Below);

        Assert.Equal(BubblePlacementSide.Above, actual.Side);
        Assert.Equal(character.Top - 30 - 100, actual.Origin.Y);
    }

    [Fact]
    public void Place_AfterFlippingBelow_RequiresTwelveExtraDipsBeforeReturningAbove()
    {
        var workArea = new ScreenRect(0, 0, 1920, 1040);
        var bubble = new ScreenSize(276, 100);

        var staysBelow = BubblePlacementService.Place(
            new ScreenRect(600, 141, 320, 320),
            bubble,
            workArea,
            BubblePlacementSide.Below);
        var returnsAbove = BubblePlacementService.Place(
            new ScreenRect(600, 142, 320, 320),
            bubble,
            workArea,
            BubblePlacementSide.Below);

        Assert.Equal(BubblePlacementSide.Below, staysBelow.Side);
        Assert.Equal(BubblePlacementSide.Above, returnsAbove.Side);
    }

    [Theory]
    [InlineData(0, 250, 0)]
    [InlineData(1800, 320, 1644)]
    public void Place_NearHorizontalCorners_ClampsTheWholeBubbleToTheWorkArea(
        double characterLeft,
        double characterWidth,
        double expectedBubbleLeft)
    {
        var actual = BubblePlacementService.Place(
            new ScreenRect(characterLeft, 400, characterWidth, characterWidth),
            new ScreenSize(276, 100),
            new ScreenRect(0, 0, 1920, 1040),
            BubblePlacementSide.Above);

        Assert.Equal(expectedBubbleLeft, actual.Origin.X);
    }

    [Fact]
    public void Place_LongBubbleAtTop_UsesAvailableSpaceBelowWithoutClipping()
    {
        var workArea = new ScreenRect(0, 0, 1365, 768);
        var bubble = new ScreenSize(300, 260);
        var character = new ScreenRect(500, 0, 250, 250);

        var actual = BubblePlacementService.Place(
            character,
            bubble,
            workArea,
            BubblePlacementSide.Above);

        Assert.Equal(BubblePlacementSide.Below, actual.Side);
        Assert.True(actual.Origin.Y >= workArea.Top);
        Assert.True(actual.Origin.Y + bubble.Height <= workArea.Bottom);
    }

    [Theory]
    [InlineData(BubblePlacementSide.Above)]
    [InlineData(BubblePlacementSide.Below)]
    public void Place_WhenNeitherSideHasTheFullGap_StaysInsideAndPreservesThePreviousSide(
        BubblePlacementSide previousSide)
    {
        var workArea = new ScreenRect(10, 10, 780, 580);
        var character = new ScreenRect(205, 105, 390, 390);
        var bubble = new ScreenSize(276, 100);

        var actual = BubblePlacementService.Place(
            character,
            bubble,
            workArea,
            previousSide);

        Assert.Equal(previousSide, actual.Side);
        Assert.InRange(actual.Origin.Y, workArea.Top, workArea.Bottom - bubble.Height);
        Assert.InRange(
            actual.Origin.Y + bubble.Height,
            workArea.Top + bubble.Height,
            workArea.Bottom);
    }

    [Theory]
    [InlineData(0, 250)]
    [InlineData(1800, 320)]
    public void Place_NearHorizontalEdges_PointsTheArrowAtTheCharacter(
        double characterLeft,
        double characterWidth)
    {
        var character = new ScreenRect(characterLeft, 400, characterWidth, characterWidth);
        var bubble = new ScreenSize(276, 100);

        var actual = BubblePlacementService.Place(
            character,
            bubble,
            new ScreenRect(0, 0, 1920, 1040),
            BubblePlacementSide.Above);

        var characterCenter = character.Left + (character.Width / 2);
        var expectedArrowCenter = Math.Clamp(
            characterCenter - actual.Origin.X,
            32,
            bubble.Width - 32);
        Assert.Equal(expectedArrowCenter, actual.ArrowCenterX, 6);
        Assert.InRange(actual.ArrowCenterX, 32, bubble.Width - 32);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Place_PhysicalPixelCoordinates_ScalesGapAndHysteresisWithDpi(double dpiScale)
    {
        var character = new ScreenRect(600 * dpiScale, 0, 320 * dpiScale, 320 * dpiScale);
        var bubble = new ScreenSize(276 * dpiScale, 100 * dpiScale);
        var workArea = new ScreenRect(0, 0, 1920 * dpiScale, 1040 * dpiScale);

        var actual = BubblePlacementService.Place(
            character,
            bubble,
            workArea,
            BubblePlacementSide.Above,
            dpiScale);

        Assert.Equal(character.Bottom + (30 * dpiScale), actual.Origin.Y, 6);
    }
}
