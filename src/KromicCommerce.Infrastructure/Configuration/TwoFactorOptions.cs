namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// 2Factor (2factor.in) SMS credentials.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract verification status.</b> 2Factor's public developer documentation could not be
/// read programmatically — <c>2factor.in/api-docs</c> returns a JavaScript shell and the
/// documentation routes return 403/404 — so the request shape below is based on the
/// template-driven transactional send described in 2Factor's own product documentation, not on
/// a page that could be fetched and quoted verbatim. The path segments are therefore
/// <b>configuration</b> rather than constants: confirm them against the API reference included
/// with your 2Factor account and correct them here if they differ. No redeploy is needed to do
/// so, and a wrong path fails the send cleanly as a reported error — it can never take the API
/// down or leak a credential.
/// </para>
/// <para>
/// The implementation deliberately uses 2Factor's <i>transactional template send</i> rather than
/// its hosted OTP product. That keeps this application authoritative for code generation, hashing,
/// expiry, attempt limits and cooldown, so a customer verifying an OTP sees exactly the same
/// behaviour regardless of which gateway is active — the property
/// <see cref="SmsProviderKind"/> selection exists to provide.
/// </para>
/// </remarks>
public sealed class TwoFactorOptions
{
    /// <summary>
    /// The 2Factor API key (the "secret" from the account's API settings). Sent as a request
    /// field, never logged, never returned by the status endpoints.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>API root. Override for a proxy; the default is the documented host.</summary>
    public string BaseUrl { get; init; } = "https://api.2factor.in";

    /// <summary>
    /// Path of the template-driven send endpoint, appended to <see cref="BaseUrl"/>.
    /// Configurable because the published documentation is not fetchable — see remarks.
    /// </summary>
    public string SendPath { get; init; } = "/v1/sms/otp";

    /// <summary>Approved sender id registered with the 2Factor account, when the account has one.</summary>
    public string? SenderId { get; init; } = null;

    /// <summary>
    /// Name of the dynamic-template variable that receives the OTP, as registered with 2Factor.
    /// Must match the variable name in the approved template body exactly.
    /// </summary>
    public string OtpVariableName { get; init; } = "otp";

    /// <summary>Name of the dynamic-template variable that receives the OTP lifetime in minutes.</summary>
    public string ExpiryVariableName { get; init; } = "expiry_minutes";
}
