namespace KromicCommerce.Infrastructure.Invoicing;

/// <summary>The two base-14 text faces the invoice layout uses.</summary>
internal enum PdfFont
{
    Regular = 0,
    Bold = 1
}

/// <summary>
/// Maps a font face to the resource name it is registered under.
///
/// A live member cannot live on the enum — C# enums cannot carry properties — and the
/// mapping has to agree exactly with the font dictionary <see cref="PdfPageBuilder.Build"/>
/// writes, or a viewer silently renders nothing where text should be. Keeping it in one
/// place next to the metric table makes the agreement easy to check.
/// </summary>
internal static class PdfFontResources
{
    public static string NameOf(this PdfFont font) => font == PdfFont.Bold ? "F2" : "F1";
}

internal sealed record PdfColor(double R, double G, double B)
{
    /// <summary>Near-black body text. Not pure black — pure black on white reads harsh on screen.</summary>
    public static PdfColor Ink => new(0.10, 0.10, 0.10);

    public static PdfColor Muted => new(0.45, 0.45, 0.45);

    public static PdfColor Paper => new(1, 1, 1);

    public static PdfColor Shaded => new(0.96, 0.96, 0.96);

    public static PdfColor Rule => new(0.85, 0.85, 0.85);

    public static PdfColor Positive => new(0.05, 0.45, 0.25);

    /// <summary>Parses a six-digit hex colour, optionally prefixed with '#'. Returns null when unusable.</summary>
    public static PdfColor? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        var trimmed = hex.Trim();
        if (trimmed.StartsWith('#')) trimmed = trimmed[1..];
        if (trimmed.Length != 6) return null;

        static double Component(string s, int index) =>
            Convert.ToInt32(s.Substring(index, 2), 16) / 255d;

        try
        {
            return new PdfColor(
                Component(trimmed, 0),
                Component(trimmed, 2),
                Component(trimmed, 4));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Advance widths for Helvetica and Helvetica-Bold, in 1/1000 em.
///
/// These are the Adobe standard-14 metrics, so the widths here match what a viewer uses to
/// render the same glyphs. That exactness is the whole reason right-aligning an amount works:
/// the number of trailing characters that fit in a column is a layout decision, and a metric
/// table that disagreed with the renderer by a percent would eventually push a total past the
/// margin.
///
/// Only characters that can appear in invoice text are listed. Anything else falls back to the
/// width of '?', which is what the caller already substitutes during transliteration.
/// </summary>
internal static class HelveticaMetrics
{
    private const int DefaultWidth = 556;

    private static readonly Dictionary<(PdfFont Font, char Ch), int> Widths = Build();

    public static int WidthOf(char ch, PdfFont font) =>
        Widths.TryGetValue((font, ch), out var width) ? width : DefaultWidth;

    /// <summary>Space is one entry for both faces; the rest come from the AFM metrics.</summary>
    private static Dictionary<(PdfFont, char), int> Build()
    {
        var map = new Dictionary<(PdfFont, char), int>();

        void Add(PdfFont font, string chars, int width)
        {
            foreach (var ch in chars) map[(font, ch)] = width;
        }

        foreach (var font in new[] { PdfFont.Regular, PdfFont.Bold })
            Add(font, " ", 278);

        // Regular
        Add(PdfFont.Regular, "!", 278);
        Add(PdfFont.Regular, "\"", 355);
        Add(PdfFont.Regular, "#", 556);
        Add(PdfFont.Regular, "$", 556);
        Add(PdfFont.Regular, "%", 889);
        Add(PdfFont.Regular, "&", 667);
        Add(PdfFont.Regular, "'", 191);
        Add(PdfFont.Regular, "(", 333);
        Add(PdfFont.Regular, ")", 333);
        Add(PdfFont.Regular, "*", 389);
        Add(PdfFont.Regular, "+", 584);
        Add(PdfFont.Regular, ",", 278);
        Add(PdfFont.Regular, "-", 333);
        Add(PdfFont.Regular, ".", 278);
        Add(PdfFont.Regular, "/", 278);
        Add(PdfFont.Regular, "0123456789", 556);
        Add(PdfFont.Regular, ":", 278);
        Add(PdfFont.Regular, ";", 278);
        Add(PdfFont.Regular, "<", 584);
        Add(PdfFont.Regular, "=", 584);
        Add(PdfFont.Regular, ">", 584);
        Add(PdfFont.Regular, "?", 556);
        Add(PdfFont.Regular, "@", 1015);
        Add(PdfFont.Regular, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", 667);
        Add(PdfFont.Regular, "[", 278);
        Add(PdfFont.Regular, "\\", 278);
        Add(PdfFont.Regular, "]", 278);
        Add(PdfFont.Regular, "^", 469);
        Add(PdfFont.Regular, "_", 556);
        Add(PdfFont.Regular, "`", 333);
        Add(PdfFont.Regular, "abcdefghijklmnopqrstuvwxyz", 556);
        Add(PdfFont.Regular, "{", 334);
        Add(PdfFont.Regular, "|", 260);
        Add(PdfFont.Regular, "}", 334);
        Add(PdfFont.Regular, "~", 584);

        // Bold
        Add(PdfFont.Bold, "!", 333);
        Add(PdfFont.Bold, "\"", 474);
        Add(PdfFont.Bold, "#", 556);
        Add(PdfFont.Bold, "$", 556);
        Add(PdfFont.Bold, "%", 889);
        Add(PdfFont.Bold, "&", 722);
        Add(PdfFont.Bold, "'", 238);
        Add(PdfFont.Bold, "(", 333);
        Add(PdfFont.Bold, ")", 333);
        Add(PdfFont.Bold, "*", 389);
        Add(PdfFont.Bold, "+", 584);
        Add(PdfFont.Bold, ",", 278);
        Add(PdfFont.Bold, "-", 333);
        Add(PdfFont.Bold, ".", 278);
        Add(PdfFont.Bold, "/", 278);
        Add(PdfFont.Bold, "0123456789", 556);
        Add(PdfFont.Bold, ":", 333);
        Add(PdfFont.Bold, ";", 333);
        Add(PdfFont.Bold, "<", 584);
        Add(PdfFont.Bold, "=", 584);
        Add(PdfFont.Bold, ">", 584);
        Add(PdfFont.Bold, "?", 611);
        Add(PdfFont.Bold, "@", 975);
        Add(PdfFont.Bold, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", 722);
        Add(PdfFont.Bold, "[", 333);
        Add(PdfFont.Bold, "\\", 278);
        Add(PdfFont.Bold, "]", 333);
        Add(PdfFont.Bold, "^", 584);
        Add(PdfFont.Bold, "_", 556);
        Add(PdfFont.Bold, "`", 333);
        Add(PdfFont.Bold, "abcdefghijklmnopqrstuvwxyz", 556);
        Add(PdfFont.Bold, "{", 389);
        Add(PdfFont.Bold, "|", 280);
        Add(PdfFont.Bold, "}", 389);
        Add(PdfFont.Bold, "~", 584);

        return map;
    }
}