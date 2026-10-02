namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Free2SMS credentials and message template.
/// </summary>
/// <remarks>
/// <para>
/// Free2SMS has no OTP-specific endpoint, so the adapter renders the message template with the
/// generated OTP and sends it through the standard <c>/api/v1/send</c> endpoint. The message
/// must match an approved DLT template exactly.
/// </para>
/// <para>
/// Reference: https://free2sms.com/api/v1/send
/// </para>
/// </remarks>
public sealed class Free2SmsOptions
{
    /// <summary>API key, sent as <c>Authorization: Bearer &lt;key&gt;</c>. Never logged or returned.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>DLT-approved 6-character sender header registered on the account.</summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>
    /// The OTP message template. Must contain {{OTP}} as the placeholder for the verification code.
    /// The message must match an approved DLT registration exactly.
    /// </summary>
    public string MessageTemplate { get; init; } = string.Empty;
}
