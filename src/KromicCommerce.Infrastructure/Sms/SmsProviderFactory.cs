using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Resolves the single active SMS provider from configuration.
/// </summary>
/// <remarks>
/// This replaces an eager provider switch that read configuration once at startup and
/// <c>throw</c> at resolution time on an unknown name — which turned a typo into a runtime
/// 500 on the OTP path. Selection now happens per request, a bad value degrades to a reported
/// "not configured" state, and an unrecognised provider name can never take the API down.
///
/// Scoped, because the adapters resolve their message template from the database per send.
/// </remarks>
internal sealed class SmsProviderFactory(
    IOptions<SmsOptions> options,
    IOptions<TwoFactorOptions> twoFactor,
    IOptions<Free2SmsOptions> free2Sms,
    IOptions<TwilioOptions> twilio,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    ISmsProviderSettings savedSettings,
    ISmsTemplateStore templates,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory) : ISmsProviderFactory
{
    private readonly SmsOptions _options = options.Value;

    public SmsProviderStatus Status => BuildStatus(_options);

    public ISmsProvider Create()
    {
        if (!_options.IsConfigured)
            return new NoOpSmsProvider();

        return _options.SelectedProvider switch
        {
            SmsProviderKind.TwoFactor => new TwoFactorProvider(
                twoFactor,
                otpPolicy,
                savedSettings,
                templates,
                httpClientFactory,
                loggerFactory.CreateLogger<TwoFactorProvider>()),

            SmsProviderKind.Free2Sms => new Free2SmsProvider(
                free2Sms,
                otpPolicy,
                savedSettings,
                templates,
                httpClientFactory,
                loggerFactory.CreateLogger<Free2SmsProvider>()),

            SmsProviderKind.Twilio => new TwilioProvider(
                twilio,
                otpPolicy,
                savedSettings,
                templates,
                httpClientFactory,
                loggerFactory.CreateLogger<TwilioProvider>()),

            _ => new NoOpSmsProvider()
        };
    }

    internal static SmsProviderStatus BuildStatus(SmsOptions options)
    {
        var missing = options.GetMissingSettings();
        return new SmsProviderStatus(
            Enabled: options.Enabled,
            Provider: options.SelectedProvider,
            IsConfigured: missing.Count == 0,
            MissingSettings: missing);
    }
}
