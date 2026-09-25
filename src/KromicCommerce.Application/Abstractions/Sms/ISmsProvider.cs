namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Provider-agnostic SMS interface.
/// OTP generation/verification logic stays in the Application layer.
/// Provider adapters translate to/from provider-specific APIs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Provider name for logging and diagnostics (e.g. "Fast2SMS"). Never logs the OTP itself.</summary>
    string ProviderName { get; }

    /// <summary>Sends an OTP SMS to the given phone number.</summary>
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
