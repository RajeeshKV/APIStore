namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Read side of SMS template management: resolves the template a provider should use for a send.
/// </summary>
/// <remarks>
/// Kept separate from template administration so the provider adapters depend only on the
/// lookup they perform per send, not on create/update/delete.
/// </remarks>
public interface ISmsTemplateStore
{
    /// <summary>
    /// Returns the active template for <paramref name="provider"/>, or <c>null</c> when none is
    /// configured. A <c>null</c> result is normal, not an error — every provider has a
    /// documented behaviour without a pre-registered template, and deployments routinely start
    /// before a template has been registered with a vendor.
    /// </summary>
    Task<SmsTemplateSnapshot?> GetActiveAsync(
        SmsProviderKind provider, CancellationToken cancellationToken = default);
}

/// <summary>
/// The parts of an <see cref="Domain.Sms.SmsTemplate"/> a provider adapter needs, detached from
/// EF so the adapters do not take a dependency on persistence or track entities across a send.
/// </summary>
/// <param name="Provider">The gateway this template is registered with.</param>
/// <param name="Name">Admin-facing label, used in logs.</param>
/// <param name="ExternalTemplateId">
/// Vendor-side template registration identifier, or <c>null</c>. Non-null for Twilio Verify
/// (<c>TemplateSid</c>), Free2SMS (DLT template ID) and 2Factor (approved template name).
/// </param>
/// <param name="Body">
/// The configured message body containing placeholder tokens, or empty when the gateway renders
/// the message from its own registered template.
/// </param>
public sealed record SmsTemplateSnapshot(
    SmsProviderKind Provider,
    string Name,
    string? ExternalTemplateId,
    string Body)
{
    /// <summary>True when a vendor template identifier is available to send against.</summary>
    public bool HasExternalTemplate => !string.IsNullOrWhiteSpace(ExternalTemplateId);

    /// <summary>True when this template supplies the message body itself.</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    /// <summary>
    /// Substitutes delivery-time values into <see cref="Body"/>. Returns empty when the template
    /// carries no body, which is the signal to let the gateway render its own registered copy.
    /// </summary>
    public string Render(string otp, int expiryMinutes, string? storeName = null) => HasBody
        ? Body
            .Replace("{OTP}", otp, StringComparison.Ordinal)
            .Replace("{EXPIRY_MINUTES}", expiryMinutes.ToString(
                System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{STORE_NAME}", storeName ?? string.Empty, StringComparison.Ordinal)
        : string.Empty;
}
