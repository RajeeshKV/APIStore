namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Provider-agnostic SMS interface.
/// OTP generation/verification logic stays in the Application layer.
/// Provider adapters translate to/from provider-specific APIs.
/// </summary>
/// <remarks>
/// Obtain instances from <see cref="ISmsProviderFactory"/>, never by injecting this
/// interface directly — the factory is what enforces the single-active-provider rule.
/// </remarks>
public interface ISmsProvider
{
    /// <summary>Provider name for logging and diagnostics (e.g. "TechTo"). Never logs the OTP itself.</summary>
    string ProviderName { get; }

    /// <summary>Which integration backs this adapter.</summary>
    SmsProviderKind Kind { get; }

    /// <summary>
    /// False only for the no-op stand-in. Callers must check this before doing any work so an
    /// unconfigured system never reports a verification code as "sent".
    /// </summary>
    bool IsOperational => Kind != SmsProviderKind.None;

    /// <summary>
    /// Sends an OTP SMS to the given phone number. Accepts any common phone format
    /// (E.164 or bare national); adapters normalise via <see cref="SmsPhoneNumber"/>.
    /// Implementations never throw for a provider-side failure — failures are returned
    /// as a failed <see cref="SmsSendResult"/>.
    /// </summary>
    Task<SmsSendResult> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalised result returned by every SMS provider adapter.
/// Application code must not depend on provider-specific models.
/// </summary>
public sealed record SmsSendResult(
    bool Success,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable);
