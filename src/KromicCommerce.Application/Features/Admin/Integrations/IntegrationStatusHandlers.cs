namespace KromicCommerce.Application.Features.Admin.Integrations;

public sealed record GetPaymentIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetEmailIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetSmsIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetGoogleIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;

// -------------------------------------------------------------------------

internal sealed class GetPaymentIntegrationStatusHandler(
    IBusinessSettingsService settingsService,
    IOptions<AppPublicOptions> appOptions,
    ISecretProtectionService secretService)
    : IQueryHandler<GetPaymentIntegrationStatusQuery, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        GetPaymentIntegrationStatusQuery query, CancellationToken ct)
    {
        // Read credentials from the authoritative persistent store — not from IOptions.
        var settings = await settingsService.GetAsync(ct);
        var payment = settings?.Payment;

        var isConfigured = payment?.IsConfigured ?? false;
        var isEnabled    = payment?.Enabled ?? false;

        var publicFields = new Dictionary<string, string?>();

        // Webhook URL — always shown so admin can copy to Razorpay Dashboard on first load
        var webhookUrl = appOptions.Value.RazorpayWebhookUrl;
        if (!string.IsNullOrWhiteSpace(webhookUrl))
            publicFields["webhookUrl"] = webhookUrl;

        // Suggested webhook secret — only when not yet configured
        if (!(payment?.IsConfigured ?? false))
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var suggested = Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            publicFields["suggestedWebhookSecret"] = suggested;
        }

        var status = new IntegrationStatusResponse(
            "Razorpay",
            Enabled: isEnabled,
            IsConfigured: isConfigured,
            MaskedKeyId: string.IsNullOrWhiteSpace(payment?.RazorpayKeyId)
                ? null
                : secretService.Mask(payment.RazorpayKeyId),
            HasSecret: !string.IsNullOrWhiteSpace(payment?.EncryptedRazorpayKeySecret),
            PublicFields: publicFields.Count > 0 ? publicFields! : null);

        return Result.Success(status);
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

/// <summary>
/// Reports the live SMS provider, whether SMS counts as configured, and which settings are
/// still missing.
///
/// <para>
/// This is the operator's surface, and the only place provider identity is disclosed. The
/// customer-facing verification status endpoint deliberately omits it.
/// </para>
/// </summary>
internal sealed class GetSmsIntegrationStatusHandler(
    ISmsProviderFactory smsProviderFactory,
    ISmsProviderSettings savedSettings,
    IOptions<SmsPolicyOptions> smsPolicy)
    : IQueryHandler<GetSmsIntegrationStatusQuery, IntegrationStatusResponse>
{
    public async Task<Result<IntegrationStatusResponse>> Handle(
        GetSmsIntegrationStatusQuery query, CancellationToken cancellationToken)
        => Result.Success(
            await SmsIntegrationStatusBuilder.Build(
                smsProviderFactory, savedSettings, smsPolicy.Value.RequireVerifiedPhoneAtCheckout, cancellationToken));
}

/// <summary>
/// Builds the SMS integration status payload.
/// </summary>
/// <remarks>
/// Shared by the GET and the PUT so that saving a configuration and then reading it back can
/// never disagree. The save endpoint returns this payload instead of <c>204 No Content</c>, which
/// previously forced every admin screen into a second round trip to learn what was actually
/// stored — and to guess at the outcome of a write that had already happened.
/// </remarks>
internal static class SmsIntegrationStatusBuilder
{
    internal static async Task<IntegrationStatusResponse> Build(
        ISmsProviderFactory smsProviderFactory,
        ISmsProviderSettings savedSettings,
        bool requireVerifiedPhoneAtCheckout,
        CancellationToken ct)
    {
        var status = await smsProviderFactory.GetStatusAsync(ct);
        var saved = await savedSettings.GetEffectiveAsync(ct);

        var publicFields = new Dictionary<string, string>
        {
            ["provider"] = status.Provider.ToName(),
            ["requireVerifiedPhoneAtCheckout"] = requireVerifiedPhoneAtCheckout.ToString(),
            ["selectableProviders"] = string.Join(", ",
                SmsProviderKinds.Selectable.Select(p => p.ToName()))
        };

        if (status.MissingSettings.Count > 0)
            publicFields["missingSettings"] = string.Join(", ", status.MissingSettings);

        if (saved is not null)
        {
            publicFields["selectedProvider"] = saved.Provider.ToName();
            publicFields["selectedProviderEnabled"] = saved.Enabled.ToString();

            if (saved.Settings.Count > 0)
            {
                publicFields["configuredSettings"] = string.Join(", ",
                    saved.Settings.Keys.Order(StringComparer.OrdinalIgnoreCase));
            }
        }

        var result = new IntegrationStatusResponse(
            "SMS", status.Enabled, status.IsConfigured, null,
            HasSecret: status.IsConfigured,
            PublicFields: publicFields);

        return result;
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
