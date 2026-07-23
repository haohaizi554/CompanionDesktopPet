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
    public void BlinkOverlay_ContainsExactlyTwoSeparatedSymmetricEyeRegions()
    {
        var overlay = DecodePackResource("character-blink-closed.png");
        var converted = new FormatConvertedBitmap(overlay, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        var visible = new bool[width * height];
        for (var index = 0; index < visible.Length; index++)
        {
            visible[index] = pixels[(index * 4) + 3] >= 8;
        }

        var regions = FindFourConnectedRegions(visible, width, height)
            .Where(region => region.Area >= 200)
            .OrderBy(region => region.Left)
            .ToArray();

        Assert.Equal(2, regions.Length);
        AssertRegionInside(regions[0], 0.35, 0.48, 0.30, 0.38, width, height);
        AssertRegionInside(regions[1], 0.50, 0.63, 0.30, 0.38, width, height);
        Assert.All(regions, region =>
        {
            Assert.InRange(region.Area, 2_500, 10_000);
            Assert.InRange(region.Width, 70, 150);
            Assert.InRange(region.Height, 30, 75);
        });
        Assert.InRange(regions[0].Area / (double)regions[1].Area, 0.8, 1.25);
        Assert.True(regions[0].Right < regions[1].Left, "The eye regions must not bridge across the nose.");
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

    private static IReadOnlyList<PixelRegion> FindFourConnectedRegions(
        bool[] visible,
        int width,
        int height)
    {
        var visited = new bool[visible.Length];
        var regions = new List<PixelRegion>();
        var queue = new Queue<int>();
        for (var start = 0; start < visible.Length; start++)
        {
            if (!visible[start] || visited[start])
            {
                continue;
            }

            visited[start] = true;
            queue.Enqueue(start);
            var area = 0;
            var left = width;
            var right = 0;
            var top = height;
            var bottom = 0;
            while (queue.TryDequeue(out var index))
            {
                var x = index % width;
                var y = index / width;
                area++;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);

                Visit(index - 1, x > 0);
                Visit(index + 1, x + 1 < width);
                Visit(index - width, y > 0);
                Visit(index + width, y + 1 < height);
            }

            regions.Add(new PixelRegion(area, left, right, top, bottom));

            void Visit(int candidate, bool inBounds)
            {
                if (inBounds && visible[candidate] && !visited[candidate])
                {
                    visited[candidate] = true;
                    queue.Enqueue(candidate);
                }
            }
        }

        return regions;
    }

    private static void AssertRegionInside(
        PixelRegion region,
        double left,
        double right,
        double top,
        double bottom,
        int width,
        int height)
    {
        Assert.InRange(region.Left / (double)width, left, right);
        Assert.InRange(region.Right / (double)width, left, right);
        Assert.InRange(region.Top / (double)height, top, bottom);
        Assert.InRange(region.Bottom / (double)height, top, bottom);
    }

    private sealed record PixelRegion(int Area, int Left, int Right, int Top, int Bottom)
    {
        public int Width => Right - Left + 1;

        public int Height => Bottom - Top + 1;
    }
}
