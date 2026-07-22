using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompanionDesktopPet.Tests;

public sealed class CharacterAssetTests
{
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
}
