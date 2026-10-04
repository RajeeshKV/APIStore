namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Builds the frozen <see cref="InvoiceContent"/> for a ticket from its order.
///
/// SNAPSHOT SEMANTICS ARE THE POINT. Every figure is copied from the order's own historical
/// row — the prices, names and quantities captured at checkout — never re-read from the live
/// catalog. An order line's <c>UnitPrice</c> exists precisely so that a later price change
/// cannot rewrite what a customer was charged, and an invoice has the same requirement.
///
/// The template contributes wording and colour only. Amounts are never templatable, so
/// editing a template cannot alter a financial figure.
///
/// A ticket with no order produces no invoice. "Where is my parcel" is a real ticket and must
/// not generate a zero-value document; the caller checks this before composing.
/// </summary>
public sealed class InvoiceContentComposer(
    IBusinessSettingsService businessSettings,
    ILogger<InvoiceContentComposer> logger)
{
    public async Task<InvoiceContent?> ComposeAsync(
        Ticket ticket,
        Order? order,
        User customer,
        InvoiceTemplate? template,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(customer);

        // No financial context means there is nothing to bill. The resolve handler checks
        // ShouldGenerateInvoice before calling, so reaching this is a programming error
        // rather than a business case.
        if (order is null)
        {
            logger.LogWarning(
                "Ticket {TicketNumber} has no linked order; skipping invoice composition.", ticket.TicketNumber);
            return null;
        }

        var settings = await businessSettings.GetAsync(ct);

        var templateNotes = template?.Notes ?? DefaultNotes;
        var templateTerms = template?.Terms ?? DefaultTerms;
        // The legal entity name is what belongs on a tax document, so it wins over the trading name
// when the merchant has supplied one.
        var issuerName = FirstNonBlank(
            template?.IssuerNameOverride,
            settings?.LegalName,
            settings?.BusinessName) ?? "Store";

        var lineItems = order.Items
            .Select(i => new InvoiceLineItem(
                i.ProductName,
                i.Sku,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal))
            .ToArray();

        // An order with no lines would produce an empty table. Fall back to a single
        // "goods and services" line carrying the recorded subtotal rather than refusing.
        if (lineItems.Length == 0)
        {
            lineItems =
            [
                new InvoiceLineItem("Goods and services", null, 1, order.Subtotal, order.Subtotal)
            ];
        }

        return InvoiceContent.Create(
            issuerName: issuerName,
            issuerAddress: settings?.Address,
            issuerEmail: settings?.SupportEmail,
            issuerTaxId: template?.TaxId,
            billToName: customer.FullName is { Length: > 0 } ? customer.FullName : customer.Email,
            billToEmail: customer.Email,
            billToAddress: FormatAddress(order),
            invoiceDateUtc: order.PaidAt ?? order.OrderPlacedAt ?? ticket.CreatedAtUtc,
            currencyCode: order.CurrencyCode,
            subtotal: order.Subtotal,
            discountAmount: order.DiscountAmount,
            taxAmount: order.TaxAmount,
            shippingAmount: order.ShippingAmount,
            codFee: order.CodFee,
            grandTotal: order.GrandTotal,
            lineItems: lineItems,
            ticketNumber: ticket.TicketNumber,
            ticketSubject: ticket.Subject,
            orderNumber: order.OrderNumber,
            notes: templateNotes,
            terms: templateTerms,
            footerNote: template?.FooterNote ?? DefaultFooter,
            accentColor: template?.AccentColor ?? "1A1A1A");
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }
        return null;
    }

    private const string DefaultNotes = "Thank you for your business.";
    private const string DefaultTerms = "Payment is due as per the agreed terms. Please quote the invoice number on any remittance.";
    private const string DefaultFooter = "This is a computer-generated invoice and does not require a signature.";

    /// <summary>
    /// Renders the order's address snapshot as a single line block. The snapshot is used
    /// rather than the customer's current address so the invoice matches the delivery.
    /// </summary>
    private static string? FormatAddress(Order order)
    {
        var address = order.ShippingAddress;
        if (address is null) return null;

        var parts = new[]
        {
            address.FullName,
            address.AddressLine1,
            address.AddressLine2,
            address.City,
            address.State,
            address.PostalCode,
            address.Country
        };

        var joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}