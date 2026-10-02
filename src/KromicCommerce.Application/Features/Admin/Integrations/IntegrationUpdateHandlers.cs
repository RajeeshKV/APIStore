using System.Text.Json;
using KromicCommerce.Application.Options;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Application.Features.Admin.Integrations;

/// <summary>
/// Integration update commands write encrypted secrets to the OutboxEvent payload.
/// The OutboxProcessor (or a dedicated background job in future) will update the
/// relevant environment/configuration store. In this phase we persist the config
/// change as an OutboxEvent so it can be audited and retried.
///
/// IMPORTANT: Raw secrets are accepted from the admin request, encrypted immediately
/// via ISecretProtectionService, and never logged or returned.
/// </summary>
public sealed record UpdateRazorpayConfigCommand(
    bool Enabled, string KeyId, string KeySecret, string WebhookSecret) : ICommand;

// Cash-on-delivery is deliberately absent from this file. COD is a shipping concern, so it is
// mutated only by UpdateDeliverySettingsCommand (Admin → Shipping), which sets both
// CodEnabled and CodExtraFee together. A dedicated UpdateCashOnDeliveryCommand used to live
// here and was reachable at PUT /admin/integrations/payment/cod; having two surfaces for one
// switch is what let the Integrations and Shipping screens disagree about availability.

public sealed record UpdateGoogleOAuthConfigCommand(
    bool Enabled, string ClientId, string ClientSecret) : ICommand;

public sealed record UpdateSmsConfigCommand(
    bool Enabled, string Provider, Dictionary<string, string>? ProviderSettings)
    : ICommand<IntegrationStatusResponse>;

/// <summary>
/// Restricts an SMS configuration write to the three supported gateways and to the setting
/// names that actually apply to the selected one.
/// </summary>
internal sealed class UpdateSmsConfigValidator : AbstractValidator<UpdateSmsConfigCommand>
{
    public UpdateSmsConfigValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("An SMS provider must be selected.")
            .Must(BeSupported).WithMessage(
                "Provider must be one of: " +
                string.Join(", ", SmsProviderKinds.Selectable.Select(p => p.ToName())) + ".");

        // Only setting keys declared by the selected provider's schema are accepted. Unknown or
        // misspelled keys are rejected so the save cannot store credentials under a typo that the
        // adapter will never read.
        RuleFor(x => x.ProviderSettings)
            .Must((cmd, settings) => HasOnlyKnownKeys(cmd, settings))
            .WithMessage(
                "Provider settings must contain only the setting names the selected provider " +
                "supports.")
            .When(x => SmsProviderKinds.Parse(x.Provider) is not null);

        // Each value is validated against the descriptor the admin form was rendered from.
        RuleFor(x => x.ProviderSettings)
            .Must((cmd, settings) => HasValidValues(cmd, settings))
            .WithMessage(
                "One or more provider settings are invalid. See GET " +
                "/admin/integrations/sms/providers for the accepted values and formats.")
            .When(x => SmsProviderKinds.Parse(x.Provider) is not null);

        RuleFor(x => x.ProviderSettings)
            .Must(s => s is null || s.Values.All(v => string.IsNullOrEmpty(v) || v.Length <= 2000))
            .WithMessage("A setting value must not exceed 2000 characters.");
    }

    private static bool HasValidValues(
        UpdateSmsConfigCommand command, Dictionary<string, string>? settings)
    {
        if (SmsProviderKinds.Parse(command.Provider) is not { } provider || settings is null)
            return true;

        return settings.All(pair =>
            SmsProviderFieldSchema.ValidateSetting(provider, pair.Key, pair.Value) is null);
    }

    private static bool BeSupported(string? provider) => SmsProviderKinds.Parse(provider) is not null;

    /// <summary>
    /// Every submitted key must be a real setting for the selected provider, and every required
    /// setting for that provider must be present with a non-empty value. Case is ignored, so
    /// "apikey" and "ApiKey" behave the same.
    ///
    /// When SMS is being disabled, only the key names are checked: turning SMS off must not
    /// require re-supplying credentials that were never saved in the first place.
    /// </summary>
    private static bool HasOnlyKnownKeys(
        UpdateSmsConfigCommand command, Dictionary<string, string>? settings)
    {
        if (SmsProviderKinds.Parse(command.Provider) is not { } provider)
            return true;

        if (settings is not null
            && !settings.Keys.All(k => SmsSettingNames.IsKnown(provider, k)))
        {
            return false;
        }

        if (!command.Enabled)
            return true;

        var supplied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings ?? [])
            supplied[key.Trim()] = value;

        return SmsSettingNames.Required(provider)
            .All(required => supplied.TryGetValue(required, out var value)
                             && !string.IsNullOrWhiteSpace(value));
    }
}

public sealed record UpdateEmailConfigCommand(
    bool Enabled, string Mode, string SenderName, string? SenderEmail, string? ApiKey) : ICommand;

/// <summary>
/// Validation for the integrations email configuration.
/// </summary>
internal sealed class UpdateEmailConfigValidator : AbstractValidator<UpdateEmailConfigCommand>
{
    public UpdateEmailConfigValidator()
    {
        RuleFor(x => x.Mode)
            .NotEmpty().WithMessage("Email mode is required.")
            .Must(BeKnownMode).WithMessage(
                "Mode must be either KromicManaged or CustomerBrevo.");

        RuleFor(x => x.SenderName)
            .NotEmpty().WithMessage("Sender name is required.")
            .MaximumLength(100).WithMessage("Sender name must not exceed 100 characters.");

        RuleFor(x => x.SenderEmail)
            .NotEmpty().WithMessage("Sender email is required in CustomerBrevo mode.")
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("Sender email is required in CustomerBrevo mode.")
            .EmailAddress().WithMessage("Sender email must be a valid email address.")
            .MaximumLength(256).WithMessage("Sender email must not exceed 256 characters.")
            .When(x => BeKnownMode(x.Mode) && Parse(x.Mode) == EmailMode.CustomerBrevo);

        RuleFor(x => x.SenderEmail)
            .Must(v => string.IsNullOrWhiteSpace(v)).WithMessage(
                "Sender email is not used in KromicManaged mode. Omit it, or switch to CustomerBrevo.")
            .When(x => BeKnownMode(x.Mode) && Parse(x.Mode) == EmailMode.KromicManaged
                       && !string.IsNullOrWhiteSpace(x.SenderEmail));

        RuleFor(x => x.ApiKey)
            .MaximumLength(500).WithMessage("API key must not exceed 500 characters.")
            .When(x => x.ApiKey is not null);
    }

    private static bool BeKnownMode(string? mode) =>
        mode is not null && Enum.TryParse<EmailMode>(mode, ignoreCase: true, out _);

    private static EmailMode Parse(string? mode) =>
        Enum.TryParse<EmailMode>(mode, ignoreCase: true, out var parsed) ? parsed : EmailMode.KromicManaged;
}

// -------------------------------------------------------------------------

internal sealed class UpdateRazorpayConfigHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ISecretProtectionService secrets,
    ILogger<UpdateRazorpayConfigHandler> logger)
    : ICommandHandler<UpdateRazorpayConfigCommand>
{
    public async Task<Result> Handle(UpdateRazorpayConfigCommand cmd, CancellationToken ct)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], ct);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var encryptedSecret = secrets.Protect(cmd.KeySecret);
        var encryptedWebhook = secrets.Protect(cmd.WebhookSecret);

        settings.UpdatePaymentCredentials(cmd.KeyId, encryptedSecret, encryptedWebhook, cmd.Enabled);

        await db.SaveChangesAsync(ct);
        settingsService.Invalidate();

        db.OutboxEvents.Add(OutboxEvent.Create("RazorpayConfigurationUpdated",
            JsonSerializer.Serialize(new { cmd.Enabled, ConfiguredAt = DateTime.UtcNow })));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Razorpay configuration updated. Enabled: {Enabled}", cmd.Enabled);
        return Result.Success();
    }
}

internal sealed class UpdateGoogleOAuthConfigHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ISecretProtectionService secrets,
    IOptions<AppPublicOptions> appOptions,
    ILogger<UpdateGoogleOAuthConfigHandler> logger)
    : ICommandHandler<UpdateGoogleOAuthConfigCommand>
{
    public async Task<Result> Handle(UpdateGoogleOAuthConfigCommand cmd, CancellationToken ct)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], ct);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var encryptedSecret = secrets.Protect(cmd.ClientSecret);
        var redirectUri = appOptions.Value.GoogleRedirectUri;

        settings.UpdateGoogleCredentials(cmd.ClientId, encryptedSecret, redirectUri, cmd.Enabled);

        await db.SaveChangesAsync(ct);
        settingsService.Invalidate();

        db.OutboxEvents.Add(OutboxEvent.Create("GoogleOAuthConfigurationUpdated",
            JsonSerializer.Serialize(new
            {
                cmd.Enabled,
                ConfiguredAt = DateTime.UtcNow
            })));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Google OAuth configuration updated. Enabled: {Enabled}", cmd.Enabled);
        return Result.Success();
    }
}

/// <summary>
/// Writes the administrator's SMS provider selection and credentials.
/// </summary>
/// <remarks>
/// The selection is persisted (encrypted, per setting) and read back by the provider
/// adapters at send time, so the save actually changes behaviour. The Outbox event records the
/// selection only — never the credential values.
/// </remarks>
internal sealed class UpdateSmsConfigHandler(
    IApplicationDbContext db,
    ISecretProtectionService secrets,
    ISmsProviderFactory smsProviderFactory,
    ISmsProviderSettings savedSettings,
    IOptions<SmsPolicyOptions> smsPolicy,
    ILogger<UpdateSmsConfigHandler> logger)
    : ICommandHandler<UpdateSmsConfigCommand, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        UpdateSmsConfigCommand cmd, CancellationToken ct)
    {
        var provider = SmsProviderKinds.Parse(cmd.Provider);
        if (provider is not { } kind)
        {
            return Result.Failure<IntegrationStatusResponse>(Error.Validation(
                "SMS_PROVIDER_UNSUPPORTED",
                $"'{cmd.Provider}' is not a supported SMS provider. " +
                $"Choose one of: {string.Join(", ", SmsProviderKinds.Selectable.Select(p => p.ToName()))}."));
        }

        var protectedSettings = ProtectSettings(kind, cmd.ProviderSettings, cmd.Enabled);

        var config = await db.SmsProviderConfigs
            .FirstOrDefaultAsync(c => c.Id == SmsProviderConfig.SingletonId, ct);

        if (config is null)
        {
            config = SmsProviderConfig.Create(cmd.Enabled, kind, protectedSettings);
            db.SmsProviderConfigs.Add(config);
        }
        else
        {
            config.Configure(cmd.Enabled, kind, protectedSettings);
        }

        db.OutboxEvents.Add(OutboxEvent.Create("SmsConfigurationUpdated",
            JsonSerializer.Serialize(new
            {
                cmd.Enabled,
                Provider = kind.ToName(),
                SettingNames = protectedSettings.Keys.ToArray(),
                ConfiguredAt = DateTime.UtcNow
            })));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("SMS configuration saved. Provider: {Provider} Enabled: {Enabled}",
            kind.ToName(), cmd.Enabled);

        var status = await SmsIntegrationStatusBuilder.Build(
            smsProviderFactory, savedSettings, smsPolicy.Value.RequireVerifiedPhoneAtCheckout, ct);

        return Result.Success(status);
    }

    /// <summary>
    /// Encrypts every submitted setting value. Only provider-specific config keys are encrypted
    /// and stored — never provider HTTP implementation details like endpoint paths or variable names.
    /// </summary>
    private Dictionary<string, string> ProtectSettings(
        SmsProviderKind provider,
        Dictionary<string, string>? settings,
        bool enabled)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings is null)
            return result;

        foreach (var (key, value) in settings)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!SmsSettingNames.IsKnown(provider, key))
                continue;

            result[key.Trim()] = secrets.Protect(value.Trim());
        }

        return result;
    }
}

internal sealed class UpdateEmailConfigHandler(
    IApplicationDbContext db,
    ISecretProtectionService secrets,
    ILogger<UpdateEmailConfigHandler> logger)
    : ICommandHandler<UpdateEmailConfigCommand>
{
    public async Task<Result> Handle(UpdateEmailConfigCommand cmd, CancellationToken ct)
    {
        var encryptedApiKey = cmd.ApiKey is not null ? secrets.Protect(cmd.ApiKey) : null;
        var payload = JsonSerializer.Serialize(new
        {
            cmd.Enabled,
            cmd.Mode,
            cmd.SenderName,
            cmd.SenderEmail,
            ApiKey = encryptedApiKey,
            IntegrationType = "Email"
        });

        db.OutboxEvents.Add(OutboxEvent.Create("IntegrationConfigUpdated", payload));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Email configuration updated. Mode: {Mode} Enabled: {Enabled}",
            cmd.Mode, cmd.Enabled);
        return Result.Success();
    }
}
