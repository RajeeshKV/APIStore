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

public sealed record UpdateGoogleOAuthConfigCommand(
    bool Enabled, string ClientId, string ClientSecret) : ICommand;

public sealed record UpdateSmsConfigCommand(
    bool Enabled, string Provider, Dictionary<string, string>? ProviderSettings) : ICommand;

public sealed record UpdateEmailConfigCommand(
    bool Enabled, string Mode, string SenderName, string? SenderEmail, string? ApiKey) : ICommand;

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

internal sealed class UpdateSmsConfigHandler(
    IApplicationDbContext db,
    ILogger<UpdateSmsConfigHandler> logger)
    : ICommandHandler<UpdateSmsConfigCommand>
{
    public async Task<Result> Handle(UpdateSmsConfigCommand cmd, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            cmd.Enabled,
            cmd.Provider,
            ProviderSettings = cmd.ProviderSettings ?? [],
            IntegrationType = "Sms"
        });

        db.OutboxEvents.Add(OutboxEvent.Create("IntegrationConfigUpdated", payload));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SMS configuration updated. Provider: {Provider} Enabled: {Enabled}",
            cmd.Provider, cmd.Enabled);
        return Result.Success();
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
