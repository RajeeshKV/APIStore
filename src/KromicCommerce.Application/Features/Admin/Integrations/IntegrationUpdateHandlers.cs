using System.Text.Json;

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
/// <remarks>
/// The endpoint previously accepted any string as the provider name and any dictionary as its
/// settings, so a request could name a gateway that does not exist, or carry misspelled setting
/// keys that were silently stored and never used — the save reported success and nothing changed.
/// Both are now rejected with a message naming the valid values.
/// </remarks>
internal sealed class UpdateSmsConfigValidator : AbstractValidator<UpdateSmsConfigCommand>
{
    public UpdateSmsConfigValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("An SMS provider must be selected.")
            .Must(BeSupported).WithMessage(
                "Provider must be one of: " +
                string.Join(", ", SmsProviderKinds.Selectable.Select(p => p.ToName())) + ".");

        RuleFor(x => x.ProviderSettings)
            .Must((cmd, settings) => HasOnlyKnownKeys(cmd, settings))
            .WithMessage(
                "Provider settings must contain only the setting names the selected provider " +
                "supports, and every required one of them.")
            .When(x => SmsProviderKinds.Parse(x.Provider) is not null);

        RuleFor(x => x.ProviderSettings)
            .Must(s => s is null || s.Values.All(v => string.IsNullOrEmpty(v) || v.Length <= 2000))
            .WithMessage("A setting value must not exceed 2000 characters.");
    }

    private static bool BeSupported(string? provider) => SmsProviderKinds.Parse(provider) is not null;

    /// <summary>
    /// Every submitted key must be a real setting for the selected provider, and every required
    /// setting for that provider must be present with a non-empty value. Case is ignored, so
    /// "apikey" and "ApiKey" behave the same, which is what an admin form should do.
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

        // Case-insensitive on purpose: the name check above accepts "apikey" as readily as
        // "ApiKey", so this lookup must too — otherwise a lower-cased admin form would be told
        // a required setting is missing while the names had already been accepted.
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
///
/// The endpoint took a raw <c>string Mode</c> with no validation, so any typo was accepted and
/// written into the audit record. Mode is parsed against the real <see cref="EmailMode"/> here,
/// and the sender address is required only when the store actually sends from its own account
/// — matching the rule enforced on the settings endpoint so the two screens cannot disagree.
///
/// The API key is optional: it is a write-once credential, and an update that only toggles a
/// flag or renames the sender must not have to resend it.
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

        // Encrypt before persistence — never store plaintext secrets
        var encryptedSecret = secrets.Protect(cmd.KeySecret);
        var encryptedWebhook = secrets.Protect(cmd.WebhookSecret);

        // Persist to BusinessSettings — authoritative configuration store.
        // Status endpoint reads from here; payment flow reads from here.
        settings.UpdatePaymentCredentials(cmd.KeyId, encryptedSecret, encryptedWebhook, cmd.Enabled);

        await db.SaveChangesAsync(ct);
        settingsService.Invalidate();

        // Audit-only Outbox event — NOT a config store, credentials excluded
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

        // Encrypt the secret before it ever touches the database — never store plaintext
        var encryptedSecret = secrets.Protect(cmd.ClientSecret);

        // Compute the redirect URI from the backend's configured public base URL.
        // The admin must register this exact URL in Google Cloud Console.
        // It is derived from App__ApiBaseUrl — deterministic, not user-supplied.
        var redirectUri = appOptions.Value.GoogleRedirectUri;

        // Persist to BusinessSettings — authoritative configuration store.
        // Status endpoint reads from here; OAuth flow reads from here.
        settings.UpdateGoogleCredentials(cmd.ClientId, encryptedSecret, redirectUri, cmd.Enabled);

        await db.SaveChangesAsync(ct);
        settingsService.Invalidate();

        // Publish audit-only Outbox event — NOT a config store, credentials excluded
        db.OutboxEvents.Add(OutboxEvent.Create("GoogleOAuthConfigurationUpdated",
            System.Text.Json.JsonSerializer.Serialize(new
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
/// This handler used to do nothing but append an audit OutboxEvent. It accepted any provider
/// name, never persisted the selection or the settings, and serialised the raw
/// <c>ProviderSettings</c> — which carry API keys and auth tokens — straight into the event
/// payload, so every save wrote live credentials into a table intended for audit records. It
/// also meant the admin screen reported a configuration that had no effect on delivery.
///
/// The selection is now persisted (encrypted, per setting) and read back by the provider
/// adapters at send time, so the save actually changes behaviour. The Outbox event records the
/// selection only.
/// </remarks>
internal sealed class UpdateSmsConfigHandler(
    IApplicationDbContext db,
    ISecretProtectionService secrets,
    ISmsProviderFactory smsProviderFactory,
    ISmsProviderSettings savedSettings,
    ILogger<UpdateSmsConfigHandler> logger)
    : ICommandHandler<UpdateSmsConfigCommand, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        UpdateSmsConfigCommand cmd, CancellationToken ct)
    {
        // The validator rejects unsupported names, but the handler must not depend on a
        // validator having run: parsing again here keeps a direct call from writing a provider
        // that does not exist.
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

        // Audit only — the setting NAMES are recorded, never the values. The raw
        // ProviderSettings used to be serialised straight into this payload, writing live API
        // keys and auth tokens into a table meant for audit records.
        db.OutboxEvents.Add(OutboxEvent.Create("SmsConfigurationUpdated",
            JsonSerializer.Serialize(new
            {
                cmd.Enabled,
                Provider = kind.ToName(),
                SettingNames = protectedSettings.Keys.ToArray(),
                ConfiguredAt = DateTime.UtcNow
            })));

        // ONE SaveChanges, which EF wraps in a single implicit transaction. The configuration
        // row and its audit event therefore commit together or not at all. Two calls would let
        // the configuration commit while the event failed, leaving a change with no audit trail
        // and no way to tell a partial write from a successful one.
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SMS configuration saved. Provider: {Provider} Enabled: {Enabled}",
            kind.ToName(), cmd.Enabled);

        // Return the resulting status rather than a bare success, so the admin form knows what was
        // actually stored without a second request. Enabling an incomplete configuration is
        // rejected by the validator, but a staged (enabled: false) save returns isConfigured:
        // false here — "saved" and "able to send" are genuinely different states.
        var status = await SmsIntegrationStatusBuilder.Build(
            smsProviderFactory, savedSettings, ct);

        return Result.Success(status);
    }

    /// <summary>
    /// Encrypts every submitted setting value.
    ///
    /// Operational settings (base URL, route, variable names) are protected alongside the
    /// credentials rather than stored in the clear. They are not secrets, but a single uniform
    /// rule means the read path has one behaviour and there is no class of configuration value
    /// that is quietly handled differently depending on which field it is.
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
            {
                // Unreachable through the API, which validates first. Ignored rather than
                // persisted so an internal caller cannot smuggle an unknown key into storage.
                continue;
            }

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
