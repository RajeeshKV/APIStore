using KromicCommerce.Domain.Sms;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// 2Factor (2factor.in) SMS credentials and route configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Documentation sources and what could not be verified.</b> 2Factor publishes more than one
/// generation of this API, and the shapes differ between them. These defaults follow the
/// dynamic-template OTP flow described on 2Factor's own product pages
/// (<c>POST https://2factor.in/API/V1/OTP/SEND</c>, authenticated with an <c>X-API-Key</c>
/// header, body <c>{ to, channel, template_name, var1 }</c>, answering
/// <c>{ "status": "sent", "session_id": "…" }</c>). Their machine-readable API reference is hosted
/// on a JavaScript-rendered Stoplight workspace (<c>2fa.api-docs.io</c>) and
/// <c>docs-dev.2factor.in</c> answers 403, so it could not be read and quoted verbatim.
/// </para>
/// <para>
/// Because the published generations disagree on the template field name — <c>template_name</c>
/// in the product-page example, <c>template</c> in another, and <c>templateName</c> in 2Factor's
/// own JavaScript SDK — <b>every one of those names is configuration, not a constant</b>
/// (<see cref="TemplateNameField"/>, <see cref="OtpPath"/>, <see cref="TransactionalPath"/>,
/// <see cref="ApiKeyHeader"/>). An account whose reference differs is corrected from the admin
/// surface or configuration, with no rebuild. This is the property that lets the integration be
/// exercised against a sandbox without trusting an unfetchable spec.
/// </para>
/// <para>
/// <b>Why the OTP code is still ours.</b> 2Factor also exposes a hosted-OTP product in which it
/// generates and validates the code. That is available via <see cref="OtpDeliveryMode"/> on the
/// provider, but it is not the default: it would move expiry, hashing, attempt limits and resend
/// cooldown to the vendor, so a customer would meet different verification behaviour depending on
/// which gateway the store happens to run.
/// </para>
/// </remarks>
public sealed class TwoFactorOptions
{
    /// <summary>
    /// The 2Factor API key (the "secret" from the account's API settings). Sent as a header or
    /// request field, never logged, never returned by the status endpoints.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// API root. The documented <c>/API/V1/…</c> routes are served from <c>2factor.in</c>;
    /// override for a proxy, a sandbox host, or an account provisioned on <c>api.2factor.in</c>.
    /// </summary>
    public string BaseUrl { get; init; } = "https://2factor.in";

    /// <summary>
    /// Preferred OTP route, appended to <see cref="BaseUrl"/>. This is 2Factor's OTP-specific
    /// endpoint and is attempted first.
    /// </summary>
    public string OtpPath { get; init; } = "/API/V1/OTP/SEND";

    /// <summary>
    /// Transactional fallback, appended to <see cref="BaseUrl"/}. Supports two placeholders:
    /// <c>{apiKey}</c> and <c>{template}</c>, the latter receiving the administrator's template
    /// name. Leave the placeholder out for an account whose reference takes the template elsewhere.
    /// </summary>
    public string TransactionalPath { get; init; } = "/sms/{apiKey}/{template}";

    /// <summary>
    /// Header carrying the API key. Empty sends it in the request body as <c>apiKey</c> instead,
    /// for the account generations that authenticate that way.
    /// </summary>
    public string ApiKeyHeader { get; init; } = "X-API-Key";

    /// <summary>
    /// Request field carrying the template name. Must match the name 2Factor has the approved
    /// template registered under.
    /// </summary>
    public string TemplateNameField { get; init; } = "template_name";

    /// <summary>Delivery channel field. 2Factor accepts <c>SMS</c>, <c>VOICE</c> or <c>auto</c>.</summary>
    public string Channel { get; init; } = "SMS";

    /// <summary>
    /// Variable that receives the OTP. 2Factor addresses template variables positionally, so the
    /// first variable in the approved template is <c>var1</c>.
    /// </summary>
    public string OtpVariableName { get; init; } = "var1";

    /// <summary>Variable that receives the OTP lifetime in minutes, when the template uses one.</summary>
    public string ExpiryVariableName { get; init; } = "var2";

    /// <summary>Approved sender id registered with the 2Factor account, when the account has one.</summary>
    public string? SenderId { get; init; }

    /// <summary>
    /// Which route to use. Defaults to <see cref="SmsOtpDeliveryMode.Auto"/>, which tries
    /// <see cref="OtpPath"/> first and falls back to <see cref="TransactionalPath"/>.
    /// </summary>
    public SmsOtpDeliveryMode DeliveryMode { get; init; } = SmsOtpDeliveryMode.Auto;
}
