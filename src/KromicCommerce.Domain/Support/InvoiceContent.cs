using System.Text.Json;

namespace KromicCommerce.Domain.Support;

/// <summary>
/// The frozen render input for one invoice revision — everything the PDF needs, captured
/// at the moment the revision was queued.
///
/// Persisted as owned columns on <c>ticket_invoices</c>. Line items are the one variable-length
/// part, so they are held as a JSON document in a single column: an invoice line is written
/// once, read once by the renderer, and never queried across. Promoting it to child rows
/// would add a table, an ordering column and a cascade for no query benefit.
///
/// Money is decimal throughout. Totals are stored, not re-derived at render time, so a
/// rounding change in future tax code cannot retroactively alter a document already sent.
/// </summary>
public sealed class InvoiceContent : ValueObject
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private InvoiceContent() { }

    public static InvoiceContent Create(
        string issuerName,
        string? issuerAddress,
        string? issuerEmail,
        string? issuerTaxId,
        string billToName,
        string? billToEmail,
        string? billToAddress,
        DateTime invoiceDateUtc,
        string currencyCode,
        decimal subtotal,
        decimal discountAmount,
        decimal taxAmount,
        decimal shippingAmount,
        decimal codFee,
        decimal grandTotal,
        IReadOnlyList<InvoiceLineItem> lineItems,
        string ticketNumber,
        string ticketSubject,
        string? orderNumber,
        string? notes,
        string? terms,
        string? footerNote,
        string accentColor)
    {
        if (string.IsNullOrWhiteSpace(issuerName))
            throw new ArgumentException("Issuer name is required.", nameof(issuerName));
        if (string.IsNullOrWhiteSpace(billToName))
            throw new ArgumentException("Bill-to name is required.", nameof(billToName));
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        ArgumentNullException.ThrowIfNull(lineItems);
        if (lineItems.Count == 0)
            throw new ArgumentException("An invoice must have at least one line item.", nameof(lineItems));

        return new InvoiceContent
        {
            IssuerName = issuerName.Trim(),
            IssuerAddress = Clamp(issuerAddress, PartyMaxLength),
            IssuerEmail = Clamp(issuerEmail, PartyMaxLength),
            IssuerTaxId = Clamp(issuerTaxId, 120),
            BillToName = billToName.Trim(),
            BillToEmail = Clamp(billToEmail, PartyMaxLength),
            BillToAddress = Clamp(billToAddress, PartyMaxLength),
            InvoiceDateUtc = invoiceDateUtc,
            DueDateUtc = null,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            Subtotal = subtotal,
            DiscountAmount = discountAmount,
            TaxAmount = taxAmount,
            ShippingAmount = shippingAmount,
            CodFee = codFee,
            GrandTotal = grandTotal,
            LineItemsJson = JsonSerializer.Serialize(lineItems, SerializerOptions),
            TicketNumber = ticketNumber.Trim(),
            TicketSubject = Clamp(ticketSubject, 200),
            OrderNumber = Clamp(orderNumber, 100),
            Notes = Clamp(notes, TicketInvoice.NotesMaxLength),
            Terms = Clamp(terms, TicketInvoice.TermsMaxLength),
            FooterNote = Clamp(footerNote, TicketInvoice.FooterMaxLength),
            AccentColor = NormalizeHex(accentColor)
        };
    }

    public const int PartyMaxLength = 300;

    // Issuer — normally the merchant's business details
    public string IssuerName { get; private set; } = string.Empty;
    public string? IssuerAddress { get; private set; }
    public string? IssuerEmail { get; private set; }
    public string? IssuerTaxId { get; private set; }

    // Customer
    public string BillToName { get; private set; } = string.Empty;
    public string? BillToEmail { get; private set; }
    public string? BillToAddress { get; private set; }

    // Dates
    public DateTime InvoiceDateUtc { get; private set; }
    public DateTime? DueDateUtc { get; private set; }

    // Money
    public string CurrencyCode { get; private set; } = "INR";
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal ShippingAmount { get; private set; }
    public decimal CodFee { get; private set; }
    public decimal GrandTotal { get; private set; }

    /// <summary>Serialised <see cref="InvoiceLineItem"/> array. Read through <see cref="GetLineItems"/>.</summary>
    public string LineItemsJson { get; private set; } = "[]";

    // Provenance
    public string TicketNumber { get; private set; } = string.Empty;
    public string? TicketSubject { get; private set; }
    public string? OrderNumber { get; private set; }

    // Presentation
    public string? Notes { get; private set; }
    public string? Terms { get; private set; }
    public string? FooterNote { get; private set; }

    /// <summary>Six-digit hex colour, without the leading '#'. Drives rules and heading tint.</summary>
    public string AccentColor { get; private set; } = "1A1A1A";

    /// <summary>
    /// Deserialises the line items. Returns an empty list rather than throwing when the JSON
    /// is unreadable, because a corrupt line column must not make an already-sent invoice
    /// impossible to inspect — the caller renders what it can and flags the omission.
    /// </summary>
    public IReadOnlyList<InvoiceLineItem> GetLineItems()
    {
        if (string.IsNullOrWhiteSpace(LineItemsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<InvoiceLineItem[]>(LineItemsJson, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Returns a copy with the mutable presentation fields replaced by an admin's override.</summary>
    public InvoiceContent WithOverride(
        string? issuerName = null,
        string? billToName = null,
        string? notes = null,
        string? terms = null,
        string? footerNote = null,
        string? accentColor = null)
    {
        return Create(
            issuerName ?? IssuerName,
            IssuerAddress,
            IssuerEmail,
            IssuerTaxId,
            billToName ?? BillToName,
            BillToEmail,
            BillToAddress,
            InvoiceDateUtc,
            CurrencyCode,
            Subtotal,
            DiscountAmount,
            TaxAmount,
            ShippingAmount,
            CodFee,
            GrandTotal,
            GetLineItems(),
            TicketNumber,
            TicketSubject ?? string.Empty,
            OrderNumber,
            notes ?? Notes,
            terms ?? Terms,
            footerNote ?? FooterNote,
            accentColor ?? AccentColor);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IssuerName;
        yield return BillToName;
        yield return InvoiceDateUtc;
        yield return CurrencyCode;
        yield return GrandTotal;
        yield return LineItemsJson;
        yield return Notes;
        yield return Terms;
    }

    private static string? Clamp(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>Accepts <c>#1A1A1A</c> or <c>1A1A1A</c>; anything else falls back to the default ink.</summary>
    private static string NormalizeHex(string accentColor)
    {
        var trimmed = accentColor?.Trim() ?? string.Empty;
        if (trimmed.StartsWith('#')) trimmed = trimmed[1..];

        if (trimmed.Length != 6 || !trimmed.All(Uri.IsHexDigit))
            return "1A1A1A";

        return trimmed.ToUpperInvariant();
    }
}