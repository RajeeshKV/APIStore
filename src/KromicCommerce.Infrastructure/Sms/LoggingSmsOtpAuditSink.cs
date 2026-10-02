using KromicCommerce.Application.Abstractions.Sms;
using Microsoft.Extensions.Logging;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Default <see cref="ISmsOtpAuditSink"/>: writes one structured log event per send attempt.
/// </summary>
/// <remarks>
/// <para>
/// Emits a single event per attempt rather than a scatter of calls across the adapter, so a failed
/// OTP can be reconstructed from one log line: which provider, which route was actually called,
/// which template was applied, and what came back.
/// </para>
/// <para>
/// Severity follows outcome so operators can alert without parsing: a rejected send is a warning,
/// a delivered send is information, and a failure to audit at all is an error.
/// </para>
/// <para>
/// The record carries no OTP, no credential and no full phone number — see
/// <see cref="SmsOtpAudit"/> for why. The message body is never logged either: it is the one field
/// that could contain an OTP after substitution.
/// </para>
/// </remarks>
internal sealed class LoggingSmsOtpAuditSink(ILogger<LoggingSmsOtpAuditSink> logger) : ISmsOtpAuditSink
{
    private static readonly EventId Delivered = new(1, nameof(Delivered));
    private static readonly EventId NotDelivered = new(2, nameof(NotDelivered));
    private static readonly EventId AuditFailed = new(3, nameof(AuditFailed));

    public void Record(in SmsOtpAudit audit)
    {
        try
        {
            // The template's admin label and its vendor reference are recorded separately on
            // purpose: an operator diagnosing "the gateway rejected the template" needs the vendor
            // reference, and that is an identifier rather than a credential.
            if (audit.Success)
            {
                logger.Log(
                    LogLevel.Information,
                    Delivered,
                    "SMS OTP delivered. Provider: {Provider}. Mode: {Mode}. Route: {Route}. " +
                    "UsedFallback: {UsedFallback}. Template: {TemplateName} (ref {TemplateReference}). " +
                    "To: {MaskedPhone}. MessageId: {MessageId}. DurationMs: {DurationMs}.",
                    audit.Provider, audit.Mode, audit.AttemptedRoute, audit.UsedFallback,
                    NullIfEmpty(audit.TemplateName), NullIfEmpty(audit.TemplateReference),
                    audit.MaskedPhone, NullIfEmpty(audit.ProviderMessageId), audit.DurationMs);
            }
            else
            {
                logger.Log(
                    LogLevel.Warning,
                    NotDelivered,
                    "SMS OTP not delivered. Provider: {Provider}. Mode: {Mode}. Route: {Route}. " +
                    "UsedFallback: {UsedFallback}. Template: {TemplateName} (ref {TemplateReference}). " +
                    "To: {MaskedPhone}. ErrorCode: {ErrorCode}. Retryable: {Retryable}. DurationMs: {DurationMs}.",
                    audit.Provider, audit.Mode, audit.AttemptedRoute, audit.UsedFallback,
                    NullIfEmpty(audit.TemplateName), NullIfEmpty(audit.TemplateReference),
                    audit.MaskedPhone, audit.ErrorCode, audit.Retryable, audit.DurationMs);
            }
        }
        catch (Exception ex)
        {
            // Auditing must never break delivery. If the sink itself blows up, say so and let the
            // send result stand.
            logger.Log(
                LogLevel.Error,
                AuditFailed,
                ex,
                "SMS OTP audit sink failed. Provider: {Provider}. Mode: {Mode}. Route: {Route}. " +
                "UsedFallback: {UsedFallback}. Template: {TemplateName}.",
                audit.Provider, audit.Mode, audit.AttemptedRoute, audit.UsedFallback,
                NullIfEmpty(audit.TemplateName));
        }
    }

    private static string NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
}
