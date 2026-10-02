using KromicCommerce.Domain.Sms;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Free2SMS credentials.
///
/// Reference: https://free2sms.com/api.html (REST API v1.0)
/// </summary>
public sealed class Free2SmsOptions
{
    /// <summary>
    /// API key, sent as <c>Authorization: Bearer &lt;key&gt;</c>. Shown once at creation
    /// and stored by Free2SMS only as a one-way hash, so it cannot be recovered later.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>DLT-approved 6-character header registered on the account.</summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>API root. Override for a proxy; the default is the documented host.</summary>
    public string BaseUrl { get; init; } = "https://free2sms.com/api/v1";

    /// <summary>
    /// Route recorded on the delivery report. <c>otp</c> selects the priority queue,
    /// which is what an authentication code needs.
    /// </summary>
    public string Route { get; init; } = "otp";

    /// <summary>
    /// Fixed for this gateway: Free2SMS publishes no OTP-specific endpoint, so there is no
    /// native route to attempt and the transactional DLT template is always used. The setting is
    /// still honoured so an administrator does not have to know that, and so a future gateway
    /// adding native OTP follows one code path.
    /// </summary>
    public SmsOtpDeliveryMode DeliveryMode { get; init; } = SmsOtpDeliveryMode.TransactionalTemplate;

    // The DLT content template ID and the OTP message body are NOT configured here. Both are
    // per-deployment values that must match a DLT registration, and Free2SMS rejects an
    // off-template body with TEMPLATE_MISMATCH before charging. They are managed in the database
    // as an SmsTemplate instead, so an administrator can set them from the admin surface without
    // editing environment variables and redeploying.
}
