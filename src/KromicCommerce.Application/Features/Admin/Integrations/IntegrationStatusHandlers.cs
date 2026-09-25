namespace KromicCommerce.Application.Features.Admin.Integrations;

public sealed record GetPaymentIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetEmailIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetSmsIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;
public sealed record GetGoogleIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;

// -------------------------------------------------------------------------

internal sealed class GetPaymentIntegrationStatusHandler(
    IOptions<RazorpayStatusOptions> opts,
    ISecretProtectionService secretService)
    : IQueryHandler<GetPaymentIntegrationStatusQuery, IntegrationStatusResponse>
{
    public Task<Result<IntegrationStatusResponse>> Handle(
        GetPaymentIntegrationStatusQuery query, CancellationToken ct)
    {
        var o = opts.Value;
        var status = new IntegrationStatusResponse(
            "Razorpay",
            o.Enabled,
            o.IsConfigured,
            string.IsNullOrWhiteSpace(o.KeyId) ? null : secretService.Mask(o.KeyId),
            HasSecret: o.HasKeySecret);

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
    IOptions<GoogleOAuthStatusOptions> opts,
    ISecretProtectionService secretService)
    : IQueryHandler<GetGoogleIntegrationStatusQuery, IntegrationStatusResponse>
{
    public Task<Result<IntegrationStatusResponse>> Handle(
        GetGoogleIntegrationStatusQuery query, CancellationToken ct)
    {
        var o = opts.Value;
        var status = new IntegrationStatusResponse(
            "GoogleOAuth", o.Enabled, o.IsConfigured,
            string.IsNullOrWhiteSpace(o.ClientId) ? null : secretService.Mask(o.ClientId, 6),
            HasSecret: o.HasClientSecret);

        return Task.FromResult(Result.Success(status));
    }
}
