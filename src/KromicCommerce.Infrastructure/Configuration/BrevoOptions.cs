namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Brevo email configuration. Supports KromicManaged and CustomerBrevo modes.
/// ApiKey must never be logged or returned by any API.
/// Enabled defaults to false — email notifications are sent only when configured.
/// </summary>
public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public bool Enabled { get; init; } = false;

    /// <summary>Never log or return this value.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Must be verified in the Brevo account.</summary>
    public string SenderEmail { get; init; } = string.Empty;

    public string SenderName { get; init; } = string.Empty;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SenderEmail);
}
