using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// No-op SMS provider registered when SMS is not configured.
/// Always returns a business error rather than throwing, so the application
/// continues to function when SMS is disabled.
/// </summary>
internal sealed class NoOpSmsProvider : ISmsProvider
{
    public string ProviderName => "None";

    public Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
        => Task.FromResult(new SmsSendResult(
            false, null,
            "SMS_NOT_CONFIGURED",
            "SMS provider is not configured. Enable and configure an SMS provider in store settings.",
            false));
}
