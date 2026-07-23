using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompanionDesktopPet.Tests;

public sealed class CharacterAssetTests
{
    [Fact]
    public void BlinkOverlay_MatchesCharacterAndStaysInsideEyeBounds()
    {
        var baseFrame = DecodePackResource("character.png");
        var overlay = DecodePackResource("character-blink-closed.png");

        Assert.Equal(baseFrame.PixelWidth, overlay.PixelWidth);
        Assert.Equal(baseFrame.PixelHeight, overlay.PixelHeight);

        var converted = new FormatConvertedBitmap(overlay, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);

        var visiblePixelCount = 0;
        var visiblePixelsOutsideEyeBounds = 0;
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                var alpha = pixels[((y * converted.PixelWidth) + x) * 4 + 3];
                if (alpha < 8)
                {
                    continue;
                }

                visiblePixelCount++;
                var normalizedX = x / (double)converted.PixelWidth;
                var normalizedY = y / (double)converted.PixelHeight;
                if (normalizedX < 0.34 || normalizedX > 0.64 ||
                    normalizedY < 0.28 || normalizedY > 0.39)
                {
                    visiblePixelsOutsideEyeBounds++;
                }
            }
        }

        Assert.InRange(visiblePixelCount, 200, 45_000);
        Assert.Equal(0, visiblePixelsOutsideEyeBounds);
    }

    [Fact]
    public void CharacterPng_ContainsVisibleAndTransparentPixels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "character.png");
        var bitmap = new BitmapImage(new Uri(path));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var alpha = Enumerable.Range(0, pixels.Length / 4)
            .Select(index => pixels[index * 4 + 3])
            .ToArray();

        Assert.Contains(alpha, value => value == 0);
        Assert.Contains(alpha, value => value >= 250);
        Assert.Equal(1024, bitmap.PixelWidth);
        Assert.Equal(1024, bitmap.PixelHeight);
    }

    [Fact]
    public void PetIcon_HasValidIcoHeader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "pet.ico");
        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length > 1_000);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, bytes[..4]);
    }

    private static BitmapFrame DecodePackResource(string fileName)
    {
        var uri = new Uri(
            $"pack://application:,,,/CompanionDesktopPet;component/Assets/{fileName}",
            UriKind.Absolute);
        using var stream = Application.GetResourceStream(uri).Stream;
        return BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
    }
}
