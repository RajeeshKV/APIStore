using System.Globalization;
using System.Security.Cryptography;
using KromicCommerce.Application.Abstractions.Support;

namespace KromicCommerce.Infrastructure.Invoicing;

/// <summary>
/// Renders an invoice as a PDF using the built-in standard-14 fonts and a hand-written
/// writer, so the deployment takes on no third-party dependency for this feature.
///
/// LAYOUT. Two-column header (issuer identity left, invoice reference right), a rule, a
/// "billed to / reference" block, the line-item table, a totals block right-aligned, then
/// notes and terms. The totals block and the table's money columns share one right margin, so
/// the figures line up vertically - which is what makes a document read as an invoice rather
/// than as a receipt.
///
/// PAGE BREAKS. The composer hands over line items in slices of <see cref="RowsPerPage"/> and
/// a running vertical cursor. When the cursor reaches the footer limit the page is finished
/// and a new one opened with the table header repeated. Long invoices therefore paginate
/// correctly without any measurement pass up front.
/// </summary>
internal sealed class PdfInvoiceRenderer : ITicketInvoiceRenderer
{
    private const double ContentLeft = PdfPageBuilder.MarginLeft;
    private const double ContentRight = PdfPageBuilder.PageWidth - PdfPageBuilder.MarginRight;
    private const double ContentWidth = ContentRight - ContentLeft;

    // Column geometry. Money columns are right-aligned against ContentRight.
    private const double DescriptionColumn = ContentLeft;
    private const double QtyColumn = 330;
    private const double UnitPriceColumn = 420;
    private const double LineTotalColumn = ContentRight;

    // Vertical rhythm
    private const double HeaderRuleTop = 128;
    private const double BodyStart = 150;
    private const double PageBottomLimit = 726;
    private const double FooterTop = 748;

    public Task<InvoiceRenderResult> RenderAsync(
        InvoiceRenderRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var pdf = Render(request);
            var fileName = BuildFileName(request.Content);
            return Task.FromResult(InvoiceRenderResult.Ok(pdf, fileName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failure is returned as data. The worker records the reason on the invoice row
            // and retries; throwing here would only tear down its polling loop.
            return Task.FromResult(InvoiceRenderResult.Failed(ex.Message));
        }
    }

    private byte[] Render(InvoiceRenderRequest request)
    {
        var content = request.Content;
        var accent = PdfColor.FromHex(content.AccentColor) ?? PdfColor.Ink;

        var pages = PdfPageBuilder.Create();
        var cursor = DrawHeader(pages, request, accent);

        cursor = DrawParties(pages, request, accent, cursor);

        var lineItems = content.GetLineItems();
        if (request.ShowLineItemTable)
        {
            cursor = DrawTable(pages, request, accent, cursor, lineItems);
        }
        else
            {
                // With the table suppressed there is still an amount due, and an invoice that
                // hides it is worse than no invoice.
                cursor = DrawTotals(pages, request, accent, cursor, showLineTotal: false);
            }

        DrawFooterBlock(pages, request, cursor);

        // Chrome is drawn last so PageCount is final and "Page 2 of 3" is accurate.
        for (var i = 0; i < pages.PageCount; i++)
        {
            DrawFooterChrome(pages, request, i);
            pages.FinishPage();
        }

        return pages.Build(
            $"Invoice {request.InvoiceNumber} - {content.TicketNumber}",
            content.IssuerName);
    }

    // -----------------------------------------------------------------------
    // Header
    // -----------------------------------------------------------------------

    private static double DrawHeader(
        PdfPageBuilder pages,
        InvoiceRenderRequest request,
        PdfColor accent)
    {
        var content = request.Content;
        var y = PdfPageBuilder.MarginTop;

        if (request.ShowIssuerIdentity)
        {
            pages.Text(content.IssuerName, ContentLeft, y, 17, PdfFont.Bold, accent);
            y += 15;

            if (!string.IsNullOrWhiteSpace(content.IssuerAddress))
            {
                y = pages.Paragraph(content.IssuerAddress, ContentLeft, y,
                    260, 8.5, PdfFont.Regular, PdfColor.Muted, 11);
            }

            if (!string.IsNullOrWhiteSpace(content.IssuerEmail))
            {
                pages.Text(content.IssuerEmail, ContentLeft, y, 8.5, PdfFont.Regular, PdfColor.Muted);
                y += 11;
            }

            if (!string.IsNullOrWhiteSpace(content.IssuerTaxId))
            {
                pages.Text($"Tax ID: {content.IssuerTaxId}", ContentLeft, y, 8.5, PdfFont.Regular, PdfColor.Muted);
                y += 11;
            }
        }

        // Right column: the document reference. Printed on every page of the content rather
        // than only the first, so a page torn out of a multi-page invoice can still be filed.
        pages.TextRight("INVOICE", ContentRight, PdfPageBuilder.MarginTop - 2, 24, PdfFont.Bold, accent);
        pages.TextRight(request.InvoiceNumber, ContentRight, PdfPageBuilder.MarginTop + 26, 11,
            PdfFont.Bold, PdfColor.Ink);
        pages.TextRight($"Ticket {content.TicketNumber}", ContentRight, PdfPageBuilder.MarginTop + 41, 9,
            PdfFont.Regular, PdfColor.Muted);

        var issuedOn = content.InvoiceDateUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        pages.TextRight($"Issued {issuedOn}", ContentRight, PdfPageBuilder.MarginTop + 55, 9,
            PdfFont.Regular, PdfColor.Muted);

        if (content.DueDateUtc is { } dueOn)
            pages.TextRight($"Due {dueOn:d MMM yyyy}", ContentRight, PdfPageBuilder.MarginTop + 68, 9,
                PdfFont.Regular, PdfColor.Muted);

        pages.FillRect(ContentLeft, HeaderRuleTop, ContentWidth, 1.6, accent);

        return BodyStart;
    }

    // -----------------------------------------------------------------------
    // Parties
    // -----------------------------------------------------------------------

    private static double DrawParties(
        PdfPageBuilder pages,
        InvoiceRenderRequest request,
        PdfColor accent,
        double cursor)
    {
        var content = request.Content;
        var y = cursor;

        pages.Text("BILLED TO", ContentLeft, y, 7.5, PdfFont.Bold, accent);
        pages.Text("REFERENCE", 330, y, 7.5, PdfFont.Bold, accent);
        y += 14;

        y = Math.Max(
            DrawPartyBlock(pages, content.BillToName, content.BillToEmail, content.BillToAddress, ContentLeft, y),
            DrawReferenceBlock(pages, content, 330, y));

        pages.Line(ContentLeft, y + 4, ContentRight, y + 4, PdfColor.Rule);
        return y + 22;
    }

    private static double DrawPartyBlock(
        PdfPageBuilder pages, string name, string? email, string? address, double x, double y)
    {
        var bottom = y;

        pages.Text(name, x, bottom, 10, PdfFont.Bold, PdfColor.Ink);
        bottom += 13;

        if (!string.IsNullOrWhiteSpace(address))
            bottom = pages.Paragraph(address, x, bottom, 250, 9, PdfFont.Regular, PdfColor.Muted, 11.5);

        if (!string.IsNullOrWhiteSpace(email))
        {
            pages.Text(email, x, bottom, 9, PdfFont.Regular, PdfColor.Muted);
            bottom += 11;
        }

        return bottom;
    }

    private static double DrawReferenceBlock(PdfPageBuilder pages, InvoiceContent content, double x, double y)
    {
        var bottom = y;

        if (!string.IsNullOrWhiteSpace(content.OrderNumber))
        {
            DrawLabelledValue(pages, "Order", content.OrderNumber, x, bottom);
            bottom += 13;
        }

        DrawLabelledValue(pages, "Ticket", content.TicketNumber, x, bottom);
        bottom += 13;

        if (!string.IsNullOrWhiteSpace(content.TicketSubject))
        {
            // The subject can be long, so it wraps under the label instead of running into
            // the right margin.
            pages.Text("Subject", x, bottom, 9, PdfFont.Regular, PdfColor.Muted);
            bottom = pages.Paragraph(content.TicketSubject, x + 62, bottom,
                ContentRight - x - 62, 9, PdfFont.Regular, PdfColor.Ink, 11.5);
        }

        return bottom;
    }

    private static void DrawLabelledValue(PdfPageBuilder pages, string label, string value, double x, double y)
    {
        pages.Text(label, x, y, 9, PdfFont.Regular, PdfColor.Muted);
        pages.Text(value, x + 62, y, 9, PdfFont.Bold, PdfColor.Ink);
    }

    // -----------------------------------------------------------------------
    // Line items
    // -----------------------------------------------------------------------

    private static double DrawTable(
        PdfPageBuilder pages,
        InvoiceRenderRequest request,
        PdfColor accent,
        double cursor,
        IReadOnlyList<InvoiceLineItem> lineItems)
    {
        var y = cursor;
        DrawTableHeader(pages, accent, y);
        y += 16;

        for (var i = 0; i < lineItems.Count; i++)
        {
            // Break before the row that would cross the footer, then redraw the header so the
            // continued page is still readable.
            if (y > PageBottomLimit)
            {
                pages.FinishPage();
                y = PdfPageBuilder.MarginTop;
                DrawHeader(pages, request, accent);
                DrawTableHeader(pages, accent, y);
                y += 16;
            }

            var item = lineItems[i];

            // Alternating shading on a wide table measurably helps the eye track a row across
            // four columns. It is drawn first so the text sits on top of it.
            if (i % 2 == 1)
                pages.FillRect(ContentLeft, y - 4, ContentWidth, 14, PdfColor.Shaded);

            pages.Text(item.Description, DescriptionColumn, y, 9, PdfFont.Regular, PdfColor.Ink);
            pages.TextRight(item.Quantity.ToString(CultureInfo.InvariantCulture), QtyColumn, y, 9,
                PdfFont.Regular, PdfColor.Ink);
            pages.TextRight(Money(item.UnitPrice), UnitPriceColumn, y, 9,
                PdfFont.Regular, PdfColor.Ink);
            pages.TextRight(Money(item.LineTotal), LineTotalColumn, y, 9, PdfFont.Bold,
                PdfColor.Ink);

            y += 14;
        }

        return DrawTotals(pages, request, accent, y + 4, showLineTotal: true);
    }

    private static void DrawTableHeader(PdfPageBuilder pages, PdfColor accent, double y)
    {
        pages.Text("DESCRIPTION", DescriptionColumn, y, 7.5, PdfFont.Bold, accent);
        pages.TextRight("QTY", QtyColumn, y, 7.5, PdfFont.Bold, accent);
        pages.TextRight("UNIT PRICE", UnitPriceColumn, y, 7.5, PdfFont.Bold, accent);
        pages.TextRight("AMOUNT", LineTotalColumn, y, 7.5, PdfFont.Bold, accent);
        pages.Line(ContentLeft, y + 4, ContentRight, y + 4, accent, 1);
    }

    // -----------------------------------------------------------------------
    // Totals
    // -----------------------------------------------------------------------

    private static double DrawTotals(
        PdfPageBuilder pages,
        InvoiceRenderRequest request,
        PdfColor accent,
        double y,
        bool showLineTotal)
    {
        var content = request.Content;
        var currency = content.CurrencyCode;
        const double labelX = 330;
        const double valueX = ContentRight;

        if (showLineTotal)
        {
            pages.Line(ContentLeft, y, ContentRight, y, PdfColor.Rule);
            y += 14;
        }

        y = TotalsRow(pages, "Subtotal", Money(content.Subtotal), labelX, valueX, y);

        if (content.DiscountAmount > 0)
            y = TotalsRow(pages, "Discount", "-" + Money(content.DiscountAmount), labelX, valueX, y);

        if (content.TaxAmount > 0)
            y = TotalsRow(pages, "Tax", Money(content.TaxAmount), labelX, valueX, y);

        if (content.ShippingAmount > 0)
            y = TotalsRow(pages, "Shipping", Money(content.ShippingAmount), labelX, valueX, y);

        if (content.CodFee > 0)
            y = TotalsRow(pages, "Cash on delivery fee", Money(content.CodFee), labelX, valueX, y);

        // The grand total gets a tinted band so it reads as the figure to look at.
        pages.FillRect(labelX - 10, y - 2, valueX - labelX + 10, 20, PdfColor.Shaded);
        pages.Text("TOTAL DUE", labelX, y + 5, 10, PdfFont.Bold, accent);
        pages.TextRight($"{Money(content.GrandTotal)} {currency}", valueX, y + 5, 12,
            PdfFont.Bold, accent);

        return y + 28;
    }

    private static double TotalsRow(
        PdfPageBuilder pages, string label, string value, double labelX, double valueX, double y)
    {
        pages.Text(label, labelX, y, 9, PdfFont.Regular, PdfColor.Muted);
        pages.TextRight(value, valueX, y, 9, PdfFont.Regular, PdfColor.Ink);
        return y + 13;
    }

    // -----------------------------------------------------------------------
    // Notes, terms and chrome
    // -----------------------------------------------------------------------

    private static void DrawFooterBlock(PdfPageBuilder pages, InvoiceRenderRequest request, double cursor)
    {
        var content = request.Content;
        var y = cursor;

        if (request.ShowNotes && !string.IsNullOrWhiteSpace(content.Notes))
        {
            pages.Text("NOTES", ContentLeft, y, 7.5, PdfFont.Bold, PdfColor.Muted);
            y = pages.Paragraph(content.Notes, ContentLeft, y + 12, ContentWidth, 9,
                PdfFont.Regular, PdfColor.Ink, 11.5);
            y += 8;
        }

        if (request.ShowTerms && !string.IsNullOrWhiteSpace(content.Terms))
        {
            pages.Text("TERMS", ContentLeft, y, 7.5, PdfFont.Bold, PdfColor.Muted);
            pages.Paragraph(content.Terms, ContentLeft, y + 12, ContentWidth, 9,
                PdfFont.Regular, PdfColor.Muted, 11.5);
        }
    }

    private static void DrawFooterChrome(
        PdfPageBuilder pages, InvoiceRenderRequest request, int pageIndex)
    {
        pages.Line(ContentLeft, FooterTop, ContentRight, FooterTop, PdfColor.Rule);

        var total = pages.PageCount;
        pages.Text(request.Content.FooterNote ?? "This is a computer-generated invoice.",
            ContentLeft, FooterTop + 12, 7.5, PdfFont.Regular, PdfColor.Muted);

        // Page numbers are written after the last page exists, so PageCount is final here.
        pages.TextRight($"Page {pageIndex + 1} of {total}", ContentRight, FooterTop + 12, 7.5,
            PdfFont.Regular, PdfColor.Muted);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Formats an amount with thousands separators and exactly two decimals.
    ///
    /// Culture is pinned to invariant: the invoice must not pick up the server's locale, and
    /// grouping must not become the active culture's national convention — which would print
    /// 1,234.56 as 1.234,56 on a de-DE host and be read as a different number. The currency
    /// code is appended by the caller, so it is not baked into the amount.
    /// </summary>
    internal static string Money(decimal amount) =>
        amount.ToString("N2", CultureInfo.InvariantCulture);

    internal static string BuildFileName(InvoiceContent content)
    {
        var slug = string.Concat(content.TicketNumber.Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        if (slug.Length == 0) slug = "invoice";

        var extension = string.IsNullOrWhiteSpace(content.OrderNumber) ? string.Empty : "-order";
        return $"invoice-{slug}{extension}.pdf";
    }

    /// <summary>SHA-256 of the rendered bytes, stored on the invoice row for tamper-evidence.</summary>
    internal static string Checksum(byte[] pdf) => Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
}