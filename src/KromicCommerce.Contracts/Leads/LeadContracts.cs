using System.ComponentModel.DataAnnotations;

namespace KromicCommerce.Contracts.Leads;

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

/// <summary>
/// A "Get Started" sign-up from the marketing site.
///
/// <para>
/// Every field except <see cref="Source"/> and the honeypot is required. There is no optional
/// half of this form: a lead with only a phone number is not actionable, and a form that lets a
/// visitor submit nothing useful just moves the failure downstream to a person reading an inbox.
/// </para>
/// </summary>
public sealed class CreateLeadRequest
{
    /// <summary>Contact person's name, e.g. "Rahul Sharma".</summary>
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Phone or WhatsApp number, e.g. "+91 98765 43210". Formatting is accepted and stripped;
    /// the stored form keeps a leading <c>+</c> and digits only.
    /// </summary>
    [Required]
    [StringLength(40, MinimumLength = 7)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Used as the Reply-To on the notification email so the team can answer the enquiry without
    /// retyping the address. It is never used as a destination: the recipient comes from server
    /// configuration, so this endpoint cannot be turned into a mail relay.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Business name, or the category the visitor picked. One field covers both because the form
    /// asks for "Business Name or Category" — a visitor who types "Retail" and one who types
    /// "Kromic Retail Pvt Ltd" are both answering the same question.
    /// </summary>
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string Business { get; set; } = string.Empty;

    /// <summary>
    /// Optional attribution: which page or campaign produced the lead. Supply it from a query
    /// string or UTM value so leads can be attributed later.
    /// </summary>
    [StringLength(200)]
    public string? Source { get; set; }

    /// <summary>
    /// Honeypot. Must be left empty.
    /// <para>
    /// Rendered by the UI as a field a human cannot see. Bots fill every input they find, so a
    /// non-empty value is a reliable signal with none of the false positives of a heuristic like
    /// a minimum-time-on-form check. A filled honeypot is accepted with <c>201</c> rather than
    /// rejected, so the bot is not told which field gave it away.
    /// </para>
    /// </summary>
    public string? Website { get; set; }
}

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

/// <summary>A lead as shown in the admin list.</summary>
public sealed record LeadResponse(
    Guid Id,
    string Name,
    string Phone,
    string PhoneRaw,
    string Email,
    string Business,
    string Status,
    string? Source,
    bool IsArchived,
    DateTime CreatedAtUtc);