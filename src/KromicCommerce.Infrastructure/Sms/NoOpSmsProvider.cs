using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Stand-in used when SMS is switched off or incompletely configured.
///
/// Delivery is refused rather than silently dropped: the caller surfaces a business error so
/// a customer is never told an OTP was sent when nothing left the building.
/// </summary>
internal sealed class NoOpSmsProvider : ISmsProvider
{
    public string ProviderName => "None";

    public SmsProviderKind Kind => SmsProviderKind.None;

    public Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
        => Task.FromResult(new SmsSendResult(
            false, null,
            "SMS_NOT_CONFIGURED",
            "No SMS provider is active. Enable and fully configure one provider to send verification codes.",
            false));
}
