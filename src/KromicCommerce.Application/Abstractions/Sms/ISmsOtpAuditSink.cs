namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// One audited attempt to deliver an OTP, from resolution through to the gateway's answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>What must never appear here.</b> The OTP itself, the API key, the auth token, the rendered
/// message body, and the full destination number. An audit record is retained for compliance and
/// read by people who should not be able to complete a login, so it carries
/// <see cref="MaskedPhone"/> (<c>...9876</c>) rather than the number, and identifies the template
/// by name only. The masked number is enough to correlate a complaint ("the code never arrived")
/// with a send without making the record reusable.
/// </para>
/// <para>
/// The record is deliberately flat and provider-agnostic so the same sink serves every gateway
/// and any future durable store, without the adapters knowing which one is in use.
/// </para>
/// </remarks>
/// <param name="Provider">Gateway that handled the send.</param>
/// <param name="Mode">Delivery mode resolved from configuration.</param>
/// <param name="AttemptedRoute">
/// Which route was actually called — <c>native</c> or <c>transactional</c>. Differs from
/// <paramref name="Mode"/> under <see cref="Domain.Sms.SmsOtpDeliveryMode.Auto"/> after a
/// fallback, which is exactly what an operator needs to see.
/// </param>
/// <param name="UsedFallback">True when the native route was abandoned for the transactional one.</param>
/// <param name="TemplateName">Admin-facing template label, or <c>null</c> when none applied.</param>
/// <param name="TemplateReference">
/// Vendor-side template reference in use (a 2Factor template name, a Twilio <c>TemplateSid</c>,
/// a Free2SMS DLT id). Useful when a gateway rejects a template; it is an identifier, not a secret.
/// </param>
/// <param name="MaskedPhone">Last four digits only.</param>
/// <param name="Success">Whether the gateway accepted the send.</param>
/// <param name="ProviderMessageId">Gateway's own message/session identifier, for support lookups.</param>
/// <param name="ErrorCode">Machine-readable failure code, or <c>null</c> on success.</param>
/// <param name="Retryable">Whether the application considered the failure worth retrying.</param>
/// <param name="DurationMs">Wall-clock time spent in the gateway call.</param>
public sealed record SmsOtpAudit(
    string Provider,
    string Mode,
    string AttemptedRoute,
    bool UsedFallback,
    string? TemplateName,
    string? TemplateReference,
    string MaskedPhone,
    bool Success,
    string? ProviderMessageId,
    string? ErrorCode,
    bool Retryable,
    long DurationMs);

/// <summary>
/// Destination for <see cref="SmsOtpAudit"/> records.
/// </summary>
/// <remarks>
/// Kept as an interface so the adapters can be tested for what they report without asserting on
/// log output, and so a deployment that needs a durable, queryable trail can replace the default
/// logging sink without touching a provider adapter.
/// </remarks>
public interface ISmsOtpAuditSink
{
    /// <summary>
    /// Records one completed send attempt. Implementations must not throw: a failure to audit must
    /// never turn a delivered code into a failed request.
    /// </summary>
    void Record(in SmsOtpAudit audit);
}
