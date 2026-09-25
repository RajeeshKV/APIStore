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
    bool Enabled, string ClientId, string ClientSecret, string RedirectUri) : ICommand;

public sealed record UpdateSmsConfigCommand(
    bool Enabled, string Provider, Dictionary<string, string>? ProviderSettings) : ICommand;

public sealed record UpdateEmailConfigCommand(
    bool Enabled, string Mode, string SenderName, string? SenderEmail, string? ApiKey) : ICommand;

// -------------------------------------------------------------------------

internal sealed class UpdateRazorpayConfigHandler(
    IApplicationDbContext db,
    ISecretProtectionService secrets,
    ILogger<UpdateRazorpayConfigHandler> logger)
    : ICommandHandler<UpdateRazorpayConfigCommand>
{
    public async Task<Result> Handle(UpdateRazorpayConfigCommand cmd, CancellationToken ct)
    {
        // Encrypt before persistence — never store plain secrets
        var encryptedSecret = secrets.Protect(cmd.KeySecret);
        var encryptedWebhook = secrets.Protect(cmd.WebhookSecret);

        var payload = JsonSerializer.Serialize(new
        {
            cmd.Enabled,
            cmd.KeyId,
            KeySecret = encryptedSecret,
            WebhookSecret = encryptedWebhook,
            IntegrationType = "Razorpay"
        });

        db.OutboxEvents.Add(OutboxEvent.Create("IntegrationConfigUpdated", payload));
        await db.SaveChangesAsync(ct);

        // Never log the raw key or secret
        logger.LogInformation("Razorpay configuration updated. Enabled: {Enabled}", cmd.Enabled);
        return Result.Success();
    }
}

internal sealed class UpdateGoogleOAuthConfigHandler(
    IApplicationDbContext db,
    ISecretProtectionService secrets,
    ILogger<UpdateGoogleOAuthConfigHandler> logger)
    : ICommandHandler<UpdateGoogleOAuthConfigCommand>
{
    public async Task<Result> Handle(UpdateGoogleOAuthConfigCommand cmd, CancellationToken ct)
    {
        var encryptedSecret = secrets.Protect(cmd.ClientSecret);
        var payload = JsonSerializer.Serialize(new
        {
            cmd.Enabled,
            cmd.ClientId,
            ClientSecret = encryptedSecret,
            cmd.RedirectUri,
            IntegrationType = "GoogleOAuth"
        });

        db.OutboxEvents.Add(OutboxEvent.Create("IntegrationConfigUpdated", payload));
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
