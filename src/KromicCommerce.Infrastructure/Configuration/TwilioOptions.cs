using KromicCommerce.Domain.Sms;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Twilio Verify credentials.
/// </summary>
/// <remarks>
/// Reference: https://www.twilio.com/docs/verify/api/verification
/// <code>
/// POST {BaseUrl}/Services/{ServiceSid}/Verifications
/// Authorization: Basic base64(AccountSid:AuthToken)
/// Content-Type: application/x-www-form-urlencoded
///
///   To={E164}            required
///   Channel=sms          required
///   CustomCode={OTP}     optional — we always send it, see remarks
///   TemplateSid={HJ...}  optional, only when a Verify template is used
///   TemplateCustomSubstitutions={JSON}  optional, accompanies TemplateSid
/// </code>
/// </remarks>
public sealed class TwilioOptions
{
    /// <summary>Account SID (AC…). Public identifier; safe to display.</summary>
    public string AccountSid { get; init; } = string.Empty;

    /// <summary>
    /// API/Auth token. Sent as the HTTP Basic password and grants full account access, so it is
    /// never logged and never returned by the status endpoints.
    /// </summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>Verify Service SID (VA…). Which pre-approved message set is used.</summary>
    public string ServiceSid { get; init; } = string.Empty;

    /// <summary>
    /// Optional Messaging Service SID (MG…) used to route the SMS to a real sender number.
    /// Twilio requires either this or a messaging template; without one the send is rejected.
    /// </summary>
    public string? MessagingServiceSid { get; init; } = null;

    /// <summary>API root. Override for a proxy or a regional edge; the default is the documented host.</summary>
    public string BaseUrl { get; init; } = "https://verify.twilio.com";

    /// <summary>
    /// API root for the Programmable Messaging fallback used when the Verify route is
    /// unavailable. Separate from <see cref="BaseUrl"/> because Verify and Messaging are
    /// different hosts.
    /// </summary>
    public string MessagingBaseUrl { get; init; } = "https://api.twilio.com";

    /// <summary>
    /// Programmable Messaging send route, relative to <see cref="MessagingBaseUrl"/>. Used only
    /// when <see cref="DeliveryMode"/> resolves to the transactional fallback.
    /// </summary>
    public string MessagingPath { get; init; } = "/2010-04-01/Accounts/{accountSid}/Messages.json";

    /// <summary>
    /// Sender for the Programmable Messaging fallback. A Twilio number, short code, or an
    /// alphanumeric sender ID; defaults to <see cref="MessagingServiceSid"/> when that is set.
    /// </summary>
    public string? SenderId { get; init; }

    /// <summary>
    /// Which route to use. Defaults to <see cref="SmsOtpDeliveryMode.NativeOtp"/> because the
    /// Verify API <i>is</i> Twilio's dedicated OTP endpoint, and falling back to Programmable
    /// Messaging would silently drop back to a self-built send.
    /// </summary>
    public SmsOtpDeliveryMode DeliveryMode { get; init; } = SmsOtpDeliveryMode.NativeOtp;
}
