namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Twilio Programmable Messaging credentials.
/// </summary>
/// <remarks>
/// <para>
/// This adapter uses <b>Programmable Messaging</b> (the Messages API), not Twilio Verify.
/// The application generates the OTP; Twilio only delivers the SMS.
/// </para>
/// <para>
/// Reference: https://www.twilio.com/docs/messaging/api/message-resource
/// </para>
/// <para>
/// Authentication uses HTTP Basic auth: AccountSid as the username, AuthToken as the password.
/// The OTP is placed directly in the message body.
/// </para>
/// </remarks>
public sealed class TwilioOptions
{
    /// <summary>Account SID (AC…). Public identifier; safe to display.</summary>
    public string AccountSid { get; init; } = string.Empty;

    /// <summary>Auth token. Grants full account access; never logged or returned.</summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>The Twilio phone number to send from, in E.164 format.</summary>
    public string FromNumber { get; init; } = string.Empty;
}
