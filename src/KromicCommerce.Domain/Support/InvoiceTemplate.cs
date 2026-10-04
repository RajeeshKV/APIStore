namespace KromicCommerce.Domain.Support;

/// <summary>
/// The standardised invoice layout applied to every automatically generated invoice.
///
/// Exactly one row carries <see cref="IsDefault"/> = true; it is seeded by migration and is
/// the template used whenever no explicit selection is supplied. Administrators may edit it
/// and may create additional variants (e.g. "export invoice" vs "retail invoice") — those
/// are opt-in per resolution rather than replacements for the default.
///
/// Only presentation and wording live here. Amounts, dates and line items are never
/// templated: they come from the order snapshot captured into <see cref="InvoiceContent"/>,
/// so a template edit cannot alter a financial figure.
/// </summary>
public sealed class InvoiceTemplate : AuditableEntity
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 300;
    public const int IssuerNameMaxLength = 200;
    public const int TaxIdMaxLength = 120;
    public const int NotesMaxLength = TicketInvoice.NotesMaxLength;
    public const int TermsMaxLength = TicketInvoice.TermsMaxLength;
    public const int FooterMaxLength = TicketInvoice.FooterMaxLength;

    private InvoiceTemplate() { } // EF constructor

    /// <summary>Builds the built-in default. Called by the seeder; never used to mutate a live row.</summary>
    public static InvoiceTemplate CreateDefault(string name, DateTime? now = null)
    {
        var template = new InvoiceTemplate
        {
            IsDefault = true,
            AccentColor = "1A1A1A",
            ShowIssuerIdentity = true,
            ShowLineItemTable = true,
            ShowTerms = true,
            ShowNotes = true,
            FooterNote = "This is a computer-generated invoice and does not require a signature."
        };

        template.Update(
            name,
            "Standard store invoice. Applied automatically to every generated invoice.",
            null, null,
            "Thank you for your business.",
            "Payment is due as per the agreed terms. Please quote the invoice number on any remittance.",
            template.FooterNote,
            template.AccentColor,
            template.ShowIssuerIdentity,
            template.ShowLineItemTable,
            template.ShowTerms,
            template.ShowNotes,
            now);

        return template;
    }

    public static InvoiceTemplate Create(
        string name,
        bool isDefault = false,
        DateTime? now = null)
    {
        var template = new InvoiceTemplate
        {
            IsDefault = isDefault,
            AccentColor = "1A1A1A",
            ShowIssuerIdentity = true,
            ShowLineItemTable = true,
            ShowTerms = true,
            ShowNotes = true
        };

        template.Update(name, null, null, null, null, null, null,
            template.AccentColor, true, true, true, true, now);

        return template;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Exactly one row is the default; resolution falls back to it when none is named.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Overrides the business name printed in the header. Null uses BusinessSettings.</summary>
    public string? IssuerNameOverride { get; private set; }

    /// <summary>Printed under the issuer block. Supposed to be the GSTIN / VAT / ABN.</summary>
    public string? TaxId { get; private set; }

    public string? Notes { get; private set; }
    public string? Terms { get; private set; }
    public string? FooterNote { get; private set; }

    /// <summary>Six-digit hex colour without '#', used for rules and headings.</summary>
    public string AccentColor { get; private set; } = "1A1A1A";

    public bool ShowIssuerIdentity { get; private set; } = true;
    public bool ShowLineItemTable { get; private set; } = true;
    public bool ShowTerms { get; private set; } = true;
    public bool ShowNotes { get; private set; } = true;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(
        string name,
        string? description,
        string? issuerNameOverride,
        string? taxId,
        string? notes,
        string? terms,
        string? footerNote,
        string accentColor,
        bool showIssuerIdentity,
        bool showLineItemTable,
        bool showTerms,
        bool showNotes,
        DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        Name = name.Trim()[..Math.Min(NameMaxLength, name.Trim().Length)];
        Description = Clamp(description, DescriptionMaxLength);
        IssuerNameOverride = Clamp(issuerNameOverride, IssuerNameMaxLength);
        TaxId = Clamp(taxId, TaxIdMaxLength);
        Notes = Clamp(notes, NotesMaxLength);
        Terms = Clamp(terms, TermsMaxLength);
        FooterNote = Clamp(footerNote, FooterMaxLength);
        AccentColor = NormalizeHex(accentColor);
        ShowIssuerIdentity = showIssuerIdentity;
        ShowLineItemTable = showLineItemTable;
        ShowTerms = showTerms;
        ShowNotes = showNotes;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    private static string? Clamp(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeHex(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.StartsWith('#')) trimmed = trimmed[1..];

        if (trimmed.Length != 6 || !trimmed.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "Accent colour must be a six-digit hex value such as 1A1A1A or #1A1A1A.", nameof(value));

        return trimmed.ToUpperInvariant();
    }
}