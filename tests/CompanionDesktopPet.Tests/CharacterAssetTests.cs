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

        Assert.True(
            IsNaturalBlinkMask(visible, width, height),
            "The overlay must contain exactly two separated, symmetric, eye-shaped regions.");
    }

    [Fact]
    public void BlinkMaskTopology_AcceptsTwoSyntheticAlmondEyes()
    {
        var mask = CreateSyntheticMask();
        FillTwoNaturalEyes(mask);

        Assert.True(IsNaturalBlinkMask(mask, 1_024, 1_024));
    }

    [Fact]
    public void BlinkMaskTopology_RejectsSingleWink()
    {
        var mask = CreateSyntheticMask();
        FillNaturalEye(mask, 361, 460, 325, 374);

        Assert.False(IsNaturalBlinkMask(mask, 1_024, 1_024));
    }

    [Fact]
    public void BlinkMaskTopology_RejectsNoseBridge()
    {
        var mask = CreateSyntheticMask();
        FillTwoNaturalEyes(mask);
        FillRectangle(mask, 1_024, 461, 519, 349, 349);

        Assert.False(IsNaturalBlinkMask(mask, 1_024, 1_024));
    }

    [Fact]
    public void BlinkMaskTopology_RejectsTwoSolidRectangles()
    {
        var mask = CreateSyntheticMask();
        FillTwoEyeRectangles(mask);

        Assert.False(IsNaturalBlinkMask(mask, 1_024, 1_024));
    }

    [Fact]
    public void BlinkMaskTopology_RejectsThirdSmallFragment()
    {
        var mask = CreateSyntheticMask();
        FillTwoNaturalEyes(mask);
        mask[(400 * 1_024) + 700] = true;

        Assert.False(IsNaturalBlinkMask(mask, 1_024, 1_024));
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

    private static bool IsNaturalBlinkMask(bool[] visible, int width, int height)
    {
        var regions = FindFourConnectedRegions(visible, width, height)
            .OrderBy(region => region.Left)
            .ToArray();
        if (regions.Length != 2)
        {
            return false;
        }

        return IsRegionInside(regions[0], 0.35, 0.48, 0.30, 0.38, width, height)
            && IsRegionInside(regions[1], 0.50, 0.63, 0.30, 0.38, width, height)
            && regions.All(region => region.Area is >= 2_500 and <= 10_000)
            && regions.All(region => region.Width is >= 70 and <= 150)
            && regions.All(region => region.Height is >= 30 and <= 75)
            && regions[0].Area / (double)regions[1].Area is >= 0.8 and <= 1.25
            && regions[0].Right < regions[1].Left
            && regions.All(region => HasEyeShape(visible, width, region));
    }

    private static bool HasEyeShape(bool[] visible, int width, PixelRegion region)
    {
        var boundingArea = region.Width * region.Height;
        var fillRatio = region.Area / (double)boundingArea;
        var cornersAreTransparent = !IsVisible(region.Left, region.Top)
            && !IsVisible(region.Right, region.Top)
            && !IsVisible(region.Left, region.Bottom)
            && !IsVisible(region.Right, region.Bottom);
        var topCoverage = CountRow(region.Top) / (double)region.Width;
        var bottomCoverage = CountRow(region.Bottom) / (double)region.Width;
        var widestRowCoverage = Enumerable.Range(region.Top, region.Height)
            .Max(y => CountRow(y)) / (double)region.Width;
        var leftCoverage = CountColumn(region.Left) / (double)region.Height;
        var rightCoverage = CountColumn(region.Right) / (double)region.Height;
        var tallestColumnCoverage = Enumerable.Range(region.Left, region.Width)
            .Max(x => CountColumn(x)) / (double)region.Height;

        return fillRatio is >= 0.45 and <= 0.90
            && cornersAreTransparent
            && topCoverage <= 0.50
            && bottomCoverage <= 0.50
            && widestRowCoverage >= 0.75
            && leftCoverage <= 0.35
            && rightCoverage <= 0.35
            && tallestColumnCoverage >= 0.75;

        bool IsVisible(int x, int y) => visible[(y * width) + x];

        int CountRow(int y) => Enumerable.Range(region.Left, region.Width)
            .Count(x => IsVisible(x, y));

        int CountColumn(int x) => Enumerable.Range(region.Top, region.Height)
            .Count(y => IsVisible(x, y));
    }

    private static bool IsRegionInside(
        PixelRegion region,
        double left,
        double right,
        double top,
        double bottom,
        int width,
        int height)
    {
        return region.Left / (double)width >= left
            && region.Right / (double)width <= right
            && region.Top / (double)height >= top
            && region.Bottom / (double)height <= bottom;
    }

    private static bool[] CreateSyntheticMask() => new bool[1_024 * 1_024];

    private static void FillTwoEyeRectangles(bool[] mask)
    {
        FillRectangle(mask, 1_024, 361, 460, 325, 374);
        FillRectangle(mask, 1_024, 520, 619, 325, 374);
    }

    private static void FillTwoNaturalEyes(bool[] mask)
    {
        FillNaturalEye(mask, 361, 460, 325, 374);
        FillNaturalEye(mask, 520, 619, 325, 374);
    }

    private static void FillNaturalEye(
        bool[] mask,
        int left,
        int right,
        int top,
        int bottom)
    {
        var centerX = (left + right) / 2.0;
        var centerY = (top + bottom) / 2.0;
        var radiusX = (right - left + 1) / 2.0;
        var radiusY = (bottom - top + 1) / 2.0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var normalizedX = (x - centerX) / radiusX;
                var normalizedY = (y - centerY) / radiusY;
                if ((normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1)
                {
                    mask[(y * 1_024) + x] = true;
                }
            }
        }
    }

    private static void FillRectangle(
        bool[] mask,
        int width,
        int left,
        int right,
        int top,
        int bottom)
    {
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                mask[(y * width) + x] = true;
            }
        }
    }

    private sealed record PixelRegion(int Area, int Left, int Right, int Top, int Bottom)
    {
        public int Width => Right - Left + 1;

        public int Height => Bottom - Top + 1;
    }
}
