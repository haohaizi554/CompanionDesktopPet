using System.Globalization;
using System.Text;

namespace CompanionDesktopPet.Services;

internal readonly record struct SurfaceExposureProfile(
    string Opening,
    string Ending,
    string Template);

internal static class SurfaceExposure
{
    public const int RecentWindow = 20;
    private const int OpeningWidth = 4;
    private const int EndingWidth = 6;
    private const int TemplateOpeningWidth = 2;
    private const int TemplateEndingWidth = 4;

    public static SurfaceExposureProfile Profile(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return new SurfaceExposureProfile(string.Empty, string.Empty, string.Empty);
        }

        var opening = SliceStart(normalized, OpeningWidth);
        var ending = SliceEnd(normalized, EndingWidth);
        var template = SliceStart(normalized, TemplateOpeningWidth)
                       + "|"
                       + SliceEnd(normalized, TemplateEndingWidth);
        return new SurfaceExposureProfile(opening, ending, template);
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (char.IsPunctuation(character)
                || char.IsWhiteSpace(character)
                || category is UnicodeCategory.Control
                    or UnicodeCategory.Format
                    or UnicodeCategory.SpaceSeparator
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator)
            {
                continue;
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static string SliceStart(string text, int count) =>
        text[..Math.Min(count, text.Length)];

    private static string SliceEnd(string text, int count) =>
        text[Math.Max(0, text.Length - count)..];
}
