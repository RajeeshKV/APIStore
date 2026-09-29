namespace KromicCommerce.Application.Features.Admin.Integrations;

public sealed record GetPaymentIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetEmailIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetSmsIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetGoogleIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;

// -------------------------------------------------------------------------

internal sealed class GetPaymentIntegrationStatusHandler(
    IOptions<RazorpayStatusOptions> opts,
    IOptions<AppPublicOptions> appOptions,
    ISecretProtectionService secretService)
    : IQueryHandler<GetPaymentIntegrationStatusQuery, IntegrationStatusResponse>
{
    public Task<Result<IntegrationStatusResponse>> Handle(
        GetPaymentIntegrationStatusQuery query, CancellationToken ct)
    {
        var o = opts.Value;

        // Webhook URL is computed from ApiBaseUrl — shown on every GET so admin can
        // copy it to Razorpay Dashboard before (and after) entering credentials.
        var webhookUrl = appOptions.Value.RazorpayWebhookUrl;
        var publicFields = string.IsNullOrWhiteSpace(webhookUrl)
            ? null
            : new Dictionary<string, string> { ["webhookUrl"] = webhookUrl };

        var status = new IntegrationStatusResponse(
            "Razorpay",
            o.Enabled,
            o.IsConfigured,
            string.IsNullOrWhiteSpace(o.KeyId) ? null : secretService.Mask(o.KeyId),
            HasSecret: o.HasKeySecret,
            PublicFields: publicFields!);

        return Task.FromResult(Result.Success(status));
    }
}

internal sealed class GetEmailIntegrationStatusHandler(
    IOptions<BrevoStatusOptions> opts,
    IBusinessSettingsService businessSettings)
    : IQueryHandler<GetEmailIntegrationStatusQuery, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        GetEmailIntegrationStatusQuery query, CancellationToken ct)
    {
        var o = opts.Value;
        var settings = await businessSettings.GetAsync(ct);
        var mode = settings?.Email.Mode.ToString() ?? "KromicManaged";

        var publicFields = new Dictionary<string, string> { ["mode"] = mode };
        if (!string.IsNullOrWhiteSpace(o.SenderEmail))
            publicFields["senderEmail"] = o.SenderEmail;

        var status = new IntegrationStatusResponse(
            "Brevo", o.Enabled, o.IsConfigured,
            MaskedKeyId: null, HasSecret: o.HasApiKey,
            PublicFields: publicFields);

        return Result.Success(status);
    }
}

internal sealed class GetSmsIntegrationStatusHandler(IOptions<SmsStatusOptions> opts)
    : IQueryHandler<GetSmsIntegrationStatusQuery, IntegrationStatusResponse>
{
    public Task<Result<IntegrationStatusResponse>> Handle(
        GetSmsIntegrationStatusQuery query, CancellationToken ct)
    {
        var o = opts.Value;
        var publicFields = string.IsNullOrWhiteSpace(o.Provider)
            ? null
            : new Dictionary<string, string> { ["provider"] = o.Provider };

        var status = new IntegrationStatusResponse(
            "SMS", o.Enabled, o.IsConfigured, null,
            HasSecret: o.HasProviderSettings,
            PublicFields: publicFields);

        return Task.FromResult(Result.Success(status));
    }
}

internal sealed class GetGoogleIntegrationStatusHandler(
    IBusinessSettingsService settingsService,
    IOptions<AppPublicOptions> appOptions,
    ISecretProtectionService secretService)
    : IQueryHandler<GetGoogleIntegrationStatusQuery, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        GetGoogleIntegrationStatusQuery query, CancellationToken ct)
    {
        // Read credentials from the authoritative persistent store.
        var settings = await settingsService.GetAsync(ct);
        var auth = settings?.Auth;

        var isConfigured = auth?.IsGoogleOAuthConfigured ?? false;
        var isEnabled    = auth?.GoogleOAuthEnabled ?? false;

        // Redirect URI is ALWAYS computed from ApiBaseUrl — available on first page load,
        // before the admin has entered any credentials. This is the value the admin must
        // copy into Google Cloud Console → Authorized Redirect URIs.
        var redirectUri = appOptions.Value.GoogleRedirectUri;

        var publicFields = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(redirectUri))
            publicFields["redirectUri"] = redirectUri;

        var status = new IntegrationStatusResponse(
            "GoogleOAuth",
            Enabled: isEnabled,
            IsConfigured: isConfigured,
            MaskedKeyId: string.IsNullOrWhiteSpace(auth?.GoogleClientId)
                ? null
                : secretService.Mask(auth.GoogleClientId, 6),
            HasSecret: !string.IsNullOrWhiteSpace(auth?.EncryptedGoogleClientSecret),
            PublicFields: publicFields.Count > 0 ? publicFields! : null);

        return Result.Success(status);
    }
}
