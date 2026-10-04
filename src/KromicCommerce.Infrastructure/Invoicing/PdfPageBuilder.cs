using System.Globalization;
using System.Text;

namespace KromicCommerce.Infrastructure.Invoicing;

/// <summary>
/// Minimal PDF 1.4 writer: pages, a content stream per page, the base-14 standard fonts, and
/// filled rectangles.
///
/// Written by hand rather than taken from a package, for three reasons that all matter here:
///
///   - <b>Licensing.</b> The popular .NET PDF libraries are AGPL, commercially licensed, or
///     carry a company-size condition. Shipping a storefront feature should not put that on
///     the deployment's plate.
///   - <b>Footprint.</b> A third-party PDF library typically drags in font shaping, image
///     codecs and a rendering stack. An invoice needs text and rules.
///   - <b>Determinism.</b> Same input, same bytes, every time — which is what makes the
///     SHA-256 checksum on a stored invoice meaningful.
///
/// Text is drawn with the standard-14 fonts (Helvetica, Helvetica-Bold), which every PDF
/// viewer has built in. Nothing is embedded, so no font files are needed at build time and
/// no font licensing applies.
///
/// WinAnsiEncoding maps the Latin-1 range, which is enough for an invoice. Characters outside
/// it are transliterated rather than dropped: a mangled character in a financial document is
/// worse than an approximate one, and a transliteration keeps the amount readable.
/// </summary>
internal sealed class PdfPageBuilder
{
    /// <summary>US Letter at 72 dpi. 612 x 792 points.</summary>
    public const double PageWidth = 612;
    public const double PageHeight = 792;

    public const double MarginLeft = 48;
    public const double MarginRight = 48;
    public const double MarginTop = 52;

    private readonly StringBuilder _content = new();
    private readonly List<byte[]> _pageStreams = new();

    private PdfPageBuilder()
    {
    }

    /// <summary>Number of pages written so far.</summary>
    public int PageCount => _pageStreams.Count;

    /// <summary>Creates a page and makes it current.</summary>
    public static PdfPageBuilder Create() => new();

    /// <summary>
    /// Ends the current page and appends its content stream.
    ///
    /// Coordinates in PDF grow upward from the bottom-left. Callers think in "y from the top
    /// of the page", which is how a layout is normally described, so the conversion happens
    /// once here rather than at every call site.
    /// </summary>
    public void FinishPage()
    {
        _pageStreams.Add(Latin1(_content.ToString()));
        _content.Clear();
    }

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    /// <summary>Draws a filled rectangle. Used for accent bars, table row shading and totals.</summary>
    public void FillRect(double x, double top, double width, double height, PdfColor color)
    {
        var y = PageHeight - top - height;
        _content.Append($"{F(x)} {F(y)} {F(width)} {F(height)} re\n");
        _content.Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg\n");
        _content.Append("f\n");
    }

    /// <summary>Draws a 0.75pt stroke between two points given in top-down coordinates.</summary>
    public void Line(double x1, double top1, double x2, double top2, PdfColor color, double lineWidth = 0.75)
    {
        var y1 = PageHeight - top1;
        var y2 = PageHeight - top2;
        _content.Append($"{F(lineWidth)} w\n");
        _content.Append($"{F(color.R)} {F(color.G)} {F(color.B)} RG\n");
        _content.Append($"{F(x1)} {F(y1)} m\n{F(x2)} {F(y2)} l\nS\n");
    }

    /// <summary>
    /// Draws a single line of text with its baseline at <paramref name="top"/>.
    /// Returns the advance width, so callers can chain and align without measuring twice.
    /// </summary>
    public double Text(
        string? value,
        double x,
        double top,
        double fontSize,
        PdfFont font,
        PdfColor color)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        var encoded = Transliterate(value);

        _content.Append("BT\n");
        _content.Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg\n");
        _content.Append($"/{font.NameOf()} {F(fontSize)} Tf\n");
        _content.Append($"{F(PageHeight - top)} Td\n");
        _content.Append($"({EscapeText(encoded)}) Tj\n");
        _content.Append("ET\n");

        return Measure(encoded, fontSize, font);
    }

    /// <summary>Right-aligns a string so its right edge lands on <paramref name="rightX"/>.</summary>
    public double TextRight(
        string? value, double rightX, double top, double fontSize, PdfFont font, PdfColor color)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        var width = Measure(Transliterate(value), fontSize, font);
        Text(value, rightX - width, top, fontSize, font, color);
        return width;
    }

    /// <summary>Centres a string horizontally within <paramref name="centerX"/>.</summary>
    public double TextCentered(
        string? value, double centerX, double top, double fontSize, PdfFont font, PdfColor color)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        var width = Measure(Transliterate(value), fontSize, font);
        Text(value, centerX - (width / 2), top, fontSize, font, color);
        return width;
    }

    /// <summary>
    /// Wraps a paragraph into lines no wider than <paramref name="maxWidth"/> and draws it.
    /// Returns the vertical position just below the last line, so a caller can keep drawing
    /// without recomputing line heights.
    /// </summary>
    public double Paragraph(
        string? value,
        double x,
        double top,
        double maxWidth,
        double fontSize,
        PdfFont font,
        PdfColor color,
        double lineHeight)
    {
        var y = top;
        foreach (var line in Wrap(value, maxWidth, fontSize, font))
        {
            Text(line, x, y, fontSize, font, color);
            y += lineHeight;
        }

        return y;
    }

    /// <summary>Greedy word wrap. A word longer than the line is hard-broken rather than clipped.</summary>
    public IReadOnlyList<string> Wrap(string? value, double maxWidth, double fontSize, PdfFont font)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;

            if (Measure(Transliterate(candidate), fontSize, font) <= maxWidth)
            {
                current.Append(candidate[current.Length..]);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            // A single token wider than the column — a URL, a SKU, a long base64 blob — is
            // broken mid-token. Losing the tail of it would silently change the document.
            foreach (var fragment in HardBreak(word, maxWidth, fontSize, font))
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                current.Append(fragment);
            }
        }

        if (current.Length > 0) lines.Add(current.ToString());

        return lines;
    }

    // -----------------------------------------------------------------------
    // Measurement
    // -----------------------------------------------------------------------

    /// <summary>Advance width of a string at a given size, from the standard-14 metric table.</summary>
    public static double Measure(string value, double fontSize, PdfFont font)
    {
        var total = 0;
        foreach (var ch in value)
            total += HelveticaMetrics.WidthOf(ch, font);
        return total * fontSize / 1000d;
    }

    // -----------------------------------------------------------------------
    // Serialisation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Assembles the complete document.
    ///
    /// Object numbering is fixed and explicit rather than allocated by a builder graph:
    ///   1 Catalog · 2 Pages · 3..N Page objects · N+1..2N content streams
    ///   then F1/F2 fonts, then Info.
    /// Because the layout is generated in one pass with no post-processing, hard-coding the
    /// map is simpler and less error-prone than tracking offsets.
    /// </summary>
    public byte[] Build(string title, string? author)
    {
        if (_pageStreams.Count == 0)
            throw new InvalidOperationException("A PDF must contain at least one page.");

        var pageCount = _pageStreams.Count;

        // 1 catalog, 2 page tree, then 2 objects per page, then 2 fonts, then info.
        var firstPageObject = 3;
        var firstContentObject = firstPageObject + pageCount;
        var regularFontObject = firstContentObject + pageCount;
        var boldFontObject = regularFontObject + 1;
        var infoObject = boldFontObject + 1;

        var objects = new List<byte[]>(infoObject);

        var kids = Enumerable.Range(firstPageObject, pageCount).Select(i => $"{i} 0 R");
        objects.Add(Latin1($"""<< /Type /Catalog /Pages 2 0 R >>"""));
        objects.Add(Latin1(
            $"""<< /Type /Pages /Count {pageCount} /Kids [{string.Join(' ', kids)}] >>"""));

        for (var i = 0; i < pageCount; i++)
        {
            objects.Add(Latin1(
                $"""<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] """ +
                $"""/Resources << /Font << /F1 {regularFontObject} 0 R /F2 {boldFontObject} 0 R >> >> """ +
                $"""/Contents {firstContentObject + i} 0 R >>"""));

            objects.Add(Latin1(
                $"""<< /Length {_pageStreams[i].Length} >>\nstream\n""")
                .Concat(_pageStreams[i])
                .Concat(Latin1("\nendstream"))
                .ToArray());
        }

        objects.Add(Latin1(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objects.Add(Latin1(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

        objects.Add(Latin1(
            $"""<< /Title ({EscapeText(Transliterate(title))}) """ +
            $"""/Author ({EscapeText(Transliterate(author ?? "Automated invoice"))}) """ +
            $"""/Producer (KromicCommerce Support) >>"""));

        // Cross-reference table: byte offset of every object, plus the trailer.
        var output = new MemoryStream();
        void Emit(string s) => output.Write(Latin1(s));

        Emit("%PDF-1.4\n");

        // Binary comment marking the file as containing binary data. Readers that sniff for
        // PDF by extension still work, and transfer programs stop mangling it.
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var offsets = new long[infoObject + 1];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = output.Position;
            Emit($"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Emit("\nendobj\n");
        }

        var xrefPosition = output.Position;
        Emit($"xref\n0 {infoObject + 1}\n");
        Emit("0000000000 65535 f \n");
        for (var i = 1; i <= infoObject; i++)
            Emit($"{offsets[i]:D10} 00000 n \n");

        Emit($"trailer\n<< /Size {infoObject + 1} /Root 1 0 R /Info {infoObject} 0 R >>\n");
        Emit($"startxref\n{xrefPosition}\n%%EOF\n");

        return output.ToArray();
    }

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    private static IEnumerable<string> HardBreak(string word, double maxWidth, double fontSize, PdfFont font)
    {
        var buffer = new StringBuilder();

        foreach (var ch in word)
        {
            var candidate = buffer.ToString() + ch;
            if (buffer.Length > 0 && Measure(Transliterate(candidate), fontSize, font) > maxWidth)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
            buffer.Append(ch);
        }

        if (buffer.Length > 0) yield return buffer.ToString();
    }

    /// <summary>Formats a number for a content stream. Invariant: a locale-dependent decimal
    /// separator would produce an invalid PDF operator.</summary>
    private static string F(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>PDF string literal escaping. Parentheses and backslashes are load-bearing.</summary>
    private static string EscapeText(string value)
    {
        var builder = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '(': builder.Append("\\("); break;
                case ')': builder.Append("\\)"); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    // Everything outside printable ASCII is replaced during Transliterate;
                    // anything left in 32..126 passes through.
                    builder.Append(ch);
                    break;
            }
        }
        return builder.ToString();
    }

    private static readonly Dictionary<char, string> Transliterations = new()
    {
        ['€'] = "EUR", ['£'] = "GBP", ['¥'] = "JPY", ['₹'] = "Rs.",
        ['–'] = "-", ['—'] = "-", ['‘'] = "'", ['’'] = "'",
        ['“'] = "\"", ['”'] = "\"", ['…'] = "...",
        ['\u00A0'] = " ", ['\u202F'] = " ", ['\u2009'] = " "
    };

    /// <summary>
    /// Maps text into printable WinAnsi. Characters the metric table does not know become a
    /// '?' rather than a silently wrong width, which would push a right-aligned total into
    /// the wrong column.
    /// </summary>
    private static string Transliterate(string value)
    {
        var needsWork = false;
        foreach (var ch in value)
        {
            if (ch is >= ' ' and <= '~') continue;
            if (Transliterations.ContainsKey(ch) || ch is >= '\u00A0' and <= '\u00FF') continue;
            needsWork = true;
            break;
        }

        if (!needsWork) return value;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= ' ' and <= '~')
            {
                builder.Append(ch);
            }
            else if (Transliterations.TryGetValue(ch, out var replacement))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(ch is >= '\u00A0' and <= '\u00FF' ? ch : '?');
            }
        }

        return builder.ToString();
    }

    /// <summary>Encodes to single-byte Latin-1, which is what the WinAnsi encoding expects.</summary>
    private static byte[] Latin1(string value)
    {
        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            bytes[i] = ch <= 0xFF ? (byte)ch : (byte)'?';
        }
        return bytes;
    }
}