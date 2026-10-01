using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Resolves the active provider from the administrator's saved configuration, falling back to
/// deployment configuration only when nothing has been saved.
/// </summary>
/// <remarks>
/// <para>
/// Single source of truth: a saved <c>sms_provider_configs</c> row decides which provider is
/// active and whether SMS is enabled. <c>Sms:*</c> environment variables are the bootstrap for a
/// deployment where the admin screen has never been used. The precedence is whole-row, never
/// per-field, so the two can never disagree about the active provider.
/// </para>
/// <para>
/// Selection is resolved per request rather than cached at startup, which is what lets an
/// administrator switch or disable SMS and see it take effect immediately, with no restart.
/// </para>
/// <para>
/// Unknown or removed provider names degrade to <see cref="SmsProviderKind.None"/> instead of
/// throwing, so a typo in configuration can never turn into a runtime 500 on the OTP path.
/// </para>
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

    public async Task<SmsProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var saved = await savedSettings.GetEffectiveAsync(cancellationToken);

        var enabled = saved?.Enabled ?? _options.Enabled;
        var provider = saved?.Provider ?? _options.SelectedProvider;

        var missing = new List<string>();
        if (!enabled)
            missing.Add($"{SmsOptions.SectionName}:Enabled");
        else if (!provider.IsSelectable())
            missing.Add($"{SmsOptions.SectionName}:Provider");
        else
            missing.AddRange(MissingFor(provider, saved));

        return new SmsProviderStatus(
            Enabled: enabled,
            Provider: provider,
            IsConfigured: missing.Count == 0,
            MissingSettings: missing);
    }

    public async Task<ISmsProvider> CreateAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.IsConfigured)
            return new NoOpSmsProvider();

        var saved = await savedSettings.GetEffectiveAsync(cancellationToken);

        return status.Provider switch
        {
            SmsProviderKind.TwoFactor => new TwoFactorProvider(
                twoFactor, otpPolicy, saved, templates, httpClientFactory,
                loggerFactory.CreateLogger<TwoFactorProvider>()),

            SmsProviderKind.Free2Sms => new Free2SmsProvider(
                free2Sms, otpPolicy, saved, templates, httpClientFactory,
                loggerFactory.CreateLogger<Free2SmsProvider>()),

            SmsProviderKind.Twilio => new TwilioProvider(
                twilio, otpPolicy, saved, templates, httpClientFactory,
                loggerFactory.CreateLogger<TwilioProvider>()),

            _ => new NoOpSmsProvider()
        };
    }

    /// <summary>
    /// Required settings for <paramref name="provider"/> that resolve to nothing, reported as
    /// configuration paths so the admin screen can tell an operator exactly what to set.
    /// </summary>
    private IEnumerable<string> MissingFor(SmsProviderKind provider, SmsProviderSettingsSnapshot? saved)
    {
        foreach (var setting in SmsSettingNames.Required(provider))
        {
            if (string.IsNullOrWhiteSpace(Resolve(provider, setting, saved)))
                yield return $"{SmsOptions.SectionName}:{provider}:{setting}";
        }
    }

    /// <summary>
    /// One setting, honouring the whole-row precedence: a saved value wins, otherwise the value
    /// from deployment configuration.
    /// </summary>
    private string? Resolve(SmsProviderKind provider, string setting, SmsProviderSettingsSnapshot? saved)
        => SmsSettings.Resolve(saved, setting, FromConfiguration(provider, setting));

    /// <summary>The deployment-configured value for a setting, used as the bootstrap default.</summary>
    private string? FromConfiguration(SmsProviderKind provider, string setting) => provider switch
    {
        SmsProviderKind.TwoFactor => twoFactor.Value switch
        {
            var o when setting == SmsSettingNames.ApiKey => o.ApiKey,
            var o when setting == SmsSettingNames.SenderId => o.SenderId,
            var o when setting == SmsSettingNames.BaseUrl => o.BaseUrl,
            var o when setting == SmsSettingNames.SendPath => o.SendPath,
            var o when setting == SmsSettingNames.OtpVariableName => o.OtpVariableName,
            _ when setting == SmsSettingNames.ExpiryVariableName => twoFactor.Value.ExpiryVariableName,
            _ => null
        },

        SmsProviderKind.Free2Sms => free2Sms.Value switch
        {
            var o when setting == SmsSettingNames.ApiKey => o.ApiKey,
            var o when setting == SmsSettingNames.SenderId => o.SenderId,
            var o when setting == SmsSettingNames.BaseUrl => o.BaseUrl,
            _ when setting == SmsSettingNames.Route => free2Sms.Value.Route,
            _ => null
        },

        SmsProviderKind.Twilio => twilio.Value switch
        {
            var o when setting == SmsSettingNames.AccountSid => o.AccountSid,
            var o when setting == SmsSettingNames.AuthToken => o.AuthToken,
            var o when setting == SmsSettingNames.ServiceSid => o.ServiceSid,
            var o when setting == SmsSettingNames.MessagingServiceSid => o.MessagingServiceSid,
            _ when setting == SmsSettingNames.BaseUrl => twilio.Value.BaseUrl,
            _ => null
        },

        _ => null
    };
}