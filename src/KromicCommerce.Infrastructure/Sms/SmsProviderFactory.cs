using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Resolves the active provider from the administrator's saved configuration, falling back to
/// deployment configuration only when nothing has been saved.
/// </summary>
internal sealed class SmsProviderFactory(
    IOptions<SmsOptions> options,
    IOptions<TwoFactorOptions> twoFactor,
    IOptions<Free2SmsOptions> free2Sms,
    IOptions<TwilioOptions> twilio,
    ISmsProviderSettings savedSettings,
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
                twoFactor, saved, httpClientFactory,
                loggerFactory.CreateLogger<TwoFactorProvider>()),

            SmsProviderKind.Free2Sms => new Free2SmsProvider(
                free2Sms, saved, httpClientFactory,
                loggerFactory.CreateLogger<Free2SmsProvider>()),

            SmsProviderKind.Twilio => new TwilioProvider(
                twilio, saved, httpClientFactory,
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
                yield return $"{SmsOptions.SectionName}:{provider.ToName()}:{setting}";
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
        SmsProviderKind.TwoFactor => setting switch
        {
            SmsSettingNames.ApiKey => twoFactor.Value.ApiKey,
            SmsSettingNames.TemplateName => twoFactor.Value.TemplateName,
            _ => null
        },

        SmsProviderKind.Free2Sms => setting switch
        {
            SmsSettingNames.ApiKey => free2Sms.Value.ApiKey,
            SmsSettingNames.SenderId => free2Sms.Value.SenderId,
            SmsSettingNames.MessageTemplate => free2Sms.Value.MessageTemplate,
            _ => null
        },

        SmsProviderKind.Twilio => setting switch
        {
            SmsSettingNames.AccountSid => twilio.Value.AccountSid,
            SmsSettingNames.AuthToken => twilio.Value.AuthToken,
            SmsSettingNames.FromNumber => twilio.Value.FromNumber,
            _ => null
        },

        _ => null
    };
}
