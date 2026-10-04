namespace KromicCommerce.Domain.Leads;

/// <summary>
/// A "Get Started" sign-up from the marketing site.
///
/// <para>
/// Stored as well as emailed, on purpose. The notification email is the fast path for the
/// sales team, but an email that fails — provider outage, quota, spam filter — must not silently
/// lose a lead. The row is the durable record; the email is a notification about it.
/// </para>
///
/// <para>
/// This is intentionally <b>not</b> a user account. It carries no password and grants no access,
/// so none of the authentication, consent or deletion machinery that governs <c>users</c>
/// applies to it. It is a contact record with a lifecycle.
/// </para>
/// </summary>
public sealed class Lead : AuditableEntity
{
    private Lead() { } // EF constructor

    /// <summary>Longest accepted name. Generous, because legal business names are long.</summary>
    public const int NameMaxLength = 120;

    /// <summary>Longest accepted business / category text.</summary>
    public const int BusinessMaxLength = 120;

    /// <summary>
    /// Minimum digits in a dialled number, and the E.164 maximum. Below 7 nothing is
    /// diallable; above 15 no real numbering plan exists.
    /// </summary>
    public const int PhoneMinDigits = 7;
    public const int PhoneMaxDigits = 15;

    /// <summary>RFC 5321 maximum for a forward-path address.</summary>
    public const int EmailMaxLength = 254;

    public static Lead Create(
        string name,
        string phone,
        string email,
        string business,
        string? source = null,
        string? ipAddress = null,
        string? userAgent = null,
        DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;

        return new Lead
        {
            Name = Require(name, nameof(name), NameMaxLength),
            PhoneRaw = Require(phone, nameof(phone), 40),
            Phone = NormalisePhone(phone, nameof(phone)),
            Email = NormaliseEmail(email, nameof(email)),
            Business = Require(business, nameof(business), BusinessMaxLength),
            Source = Trim(source, 200),
            IpAddress = Trim(ipAddress, 45),
            UserAgent = Trim(userAgent, 400),
            Status = LeadStatus.New,
            CreatedAtUtc = at,
            UpdatedAtUtc = at
        };
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Exactly what the visitor typed, e.g. "+91 98765 43210". Kept for display.</summary>
    public string PhoneRaw { get; private set; } = string.Empty;

    /// <summary>
    /// Digits only, with a leading <c>+</c> when the visitor supplied one. Stored so two entries
    /// of the same number in different formats are recognisably the same person.
    /// </summary>
    public string Phone { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    /// <summary>Business name, or the category the visitor picked. Free text either way.</summary>
    public string Business { get; private set; } = string.Empty;

    /// <summary>Which page or campaign produced the lead, when the caller supplies it.</summary>
    public string? Source { get; private set; }

    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    public LeadStatus Status { get; private set; }

    /// <summary>True once someone has followed up, so the list can hide resolved entries.</summary>
    public bool IsArchived { get; private set; }

    /// <summary>
    /// Whether the notification email has been delivered. Separate from <c>OutboxEvent</c> state
    /// because the lead row is the durable record; if the outbox row is ever purged, this still
    /// answers "did we tell the team?".
    /// </summary>
    public bool NotifiedAt { get; private set; }

    public DateTime? NotifiedAtUtc { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void MarkNotified(DateTime? at = null)
    {
        NotifiedAt = true;
        NotifiedAtUtc = at ?? DateTime.UtcNow;
    }

    public void ChangeStatus(LeadStatus status, DateTime? at = null)
    {
        Status = status;
        UpdatedAtUtc = at ?? DateTime.UtcNow;
    }

    public void Archive(DateTime? at = null)
    {
        IsArchived = true;
        UpdatedAtUtc = at ?? DateTime.UtcNow;
    }

    // -----------------------------------------------------------------------
    // Normalisation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reduces a phone number to dialled digits.
    ///
    /// Strips formatting the visitor used — spaces, dashes, parentheses, dots — because
    /// "+91 98765 43210" and "+919876543210" are one number typed two ways, and a sales team
    /// searching by number should not have to try every spelling.
    ///
    /// <b>Default country code is NOT applied.</b> Prepending a guessed code would be worse than
    /// storing what was given: a wrong guess produces a number that dials to a different person,
    /// which is not a recoverable error for a sales call. Normalisation is lossless only.
    /// </summary>
    public static string NormalisePhone(string phone, string paramName = "phone")
    {
        var raw = Require(phone, paramName, 40);
        var plus = raw.TrimStart().StartsWith('+') ? "+" : string.Empty;
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length < PhoneMinDigits || digits.Length > PhoneMaxDigits)
            throw new ArgumentException(
                $"A phone number must contain between {PhoneMinDigits} and {PhoneMaxDigits} digits.",
                paramName);

        return plus + digits;
    }

    /// <summary>
    /// Lower-cases and trims the address. Case is not significant in the domain part, and a
    /// lower-cased column is what makes an exact-match uniqueness check meaningful.
    /// </summary>
    public static string NormaliseEmail(string email, string paramName = "email")
    {
        var value = Require(email, paramName, EmailMaxLength).ToLowerInvariant();

        if (!value.Contains('@') || value.StartsWith('@') || value.EndsWith('@'))
            throw new ArgumentException("A valid email address is required.", paramName);

        return value;
    }

    private static string Require(string value, string paramName, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new ArgumentException("A value is required.", paramName);

        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Must be {maxLength} characters or fewer.", paramName);

        return trimmed;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

/// <summary>Where a lead sits in the sales pipeline. Set by staff, never by the visitor.</summary>
public enum LeadStatus
{
    /// <summary>Just submitted. Nobody has looked at it yet.</summary>
    New = 0,

    /// <summary>Reached out, conversation open.</summary>
    Contacted = 1,

    /// <summary>Became a paying customer.</summary>
    Qualified = 2,

    /// <summary>Not a fit. Kept for reporting.</summary>
    Unqualified = 3
}