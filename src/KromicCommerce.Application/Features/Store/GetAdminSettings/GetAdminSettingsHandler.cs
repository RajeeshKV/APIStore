using KromicCommerce.Application.Features.Store.GetPublicSettings;

namespace KromicCommerce.Application.Features.Store.GetAdminSettings;

internal sealed class GetAdminSettingsHandler(IBusinessSettingsService settingsService)
    : IQueryHandler<GetAdminSettingsQuery, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        GetAdminSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var s = await settingsService.GetAsync(cancellationToken);

        if (s is null)
            return Result.Failure<AdminBusinessSettingsResponse>(
                Error.NotFound("BUSINESS_SETTINGS_NOT_FOUND",
                    "Store configuration has not been set up yet."));

        return Result.Success(new AdminBusinessSettingsResponse(
            BusinessName: s.BusinessName,
            LegalName: s.LegalName,
            WebsiteUrl: s.WebsiteUrl,
            SupportEmail: s.SupportEmail,
            SupportPhone: s.SupportPhone,
            Address: s.Address,
            LogoUrl: s.LogoUrl,
            CountryCode: s.CountryCode,
            CurrencyCode: s.CurrencyCode,
            TimeZoneId: s.TimeZoneId,
            Culture: s.Culture,
            IsStoreOpen: s.IsStoreOpen,
            TemporaryClosureMessage: s.TemporaryClosureMessage,
            FacebookUrl: s.FacebookUrl,
            InstagramUrl: s.InstagramUrl,
            TwitterUrl: s.TwitterUrl,
            YoutubeUrl: s.YoutubeUrl,
            UpdatedAtUtc: s.UpdatedAtUtc,
            Delivery: new DeliverySettingsDto(
                s.Delivery.FlatFeeAmount,
                s.Delivery.FreeShippingThreshold,
                s.Delivery.CodEnabled,
                s.Delivery.CodExtraFee,
                s.Delivery.ProcessingDays,
                s.Delivery.MinDeliveryDays,
                s.Delivery.MaxDeliveryDays),
            Auth: new StoreAuthSettingsDto(
                s.Auth.GoogleOAuthEnabled,
                s.Auth.EmailPasswordEnabled,
                s.Auth.MobileOtpEnabled,
                s.Auth.OtpExpiryMinutes,
                s.Auth.OtpResendCooldownSeconds,
                s.Auth.OtpMaxAttempts,
                s.Auth.SmsProvider),
            Email: new EmailSettingsDto(
                s.Email.Mode.ToString(),
                s.Email.SenderName,
                s.Email.SenderEmail),
            Seo: new SeoSettingsDto(
                s.Seo.MetaTitle,
                s.Seo.MetaDescription,
                s.Seo.MetaKeywords,
                s.Seo.FaviconUrl,
                s.Seo.OgImageUrl)));
    }
}
