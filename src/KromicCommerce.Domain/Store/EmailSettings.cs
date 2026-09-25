namespace KromicCommerce.Domain.Store;

/// <summary>
/// Email configuration for transactional emails.
/// Two modes:
///   KromicManaged  — emails sent via Kromic's shared Brevo account (no customer API key needed).
///   CustomerBrevo  — emails sent via the customer's own Brevo account (requires API key in env vars).
/// Templates always live in code. This entity only controls sender identity and mode.
/// </summary>
public sealed class EmailSettings : ValueObject
{
    public static EmailSettings Default() => new()
    {
        Mode = EmailMode.KromicManaged,
        SenderName = "Store",
        SenderEmail = null
    };

    public EmailMode Mode { get; private set; }

    /// <summary>Display name shown in the "From" field of outgoing emails.</summary>
    public string SenderName { get; private set; } = string.Empty;

    /// <summary>
    /// Reply-to / from address for customer Brevo mode.
    /// Null in KromicManaged mode (Kromic's address is used).
    /// </summary>
    public string? SenderEmail { get; private set; }

    // -----------------------------------------------------------------------
    // Factory / update
    // -----------------------------------------------------------------------

    public static EmailSettings Create(EmailMode mode, string senderName, string? senderEmail)
    {
        if (string.IsNullOrWhiteSpace(senderName))
            throw new ArgumentException("Sender name must not be empty.", nameof(senderName));

        if (mode == EmailMode.CustomerBrevo && string.IsNullOrWhiteSpace(senderEmail))
            throw new ArgumentException(
                "Sender email is required in CustomerBrevo mode.", nameof(senderEmail));

        return new EmailSettings
        {
            Mode = mode,
            SenderName = senderName.Trim(),
            SenderEmail = senderEmail?.Trim()
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Mode;
        yield return SenderName;
        yield return SenderEmail;
    }
}

public enum EmailMode
{
    KromicManaged,
    CustomerBrevo
}
