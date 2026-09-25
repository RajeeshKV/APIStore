using KromicCommerce.Application.Options;

namespace KromicCommerce.Application.Features.Store.GetPublicSettings;

internal sealed class GetPublicSettingsHandler(
    IBusinessSettingsService settingsService,
    IOptions<TrackingPublicOptions> trackingOptions)
    : IQueryHandler<GetPublicSettingsQuery, PublicBusinessSettingsResponse>
{
    public async Task<Result<PublicBusinessSettingsResponse>> Handle(
        GetPublicSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);

        if (settings is null)
            return Result.Failure<PublicBusinessSettingsResponse>(
                Error.NotFound("BUSINESS_SETTINGS_NOT_FOUND",
                    "Store configuration has not been set up yet."));

        return Result.Success(MapToPublic(settings, trackingOptions.Value));
    }

    internal static PublicBusinessSettingsResponse MapToPublic(
        BusinessSettings s, TrackingPublicOptions tracking) =>
        new(
            BusinessName: s.BusinessName,
            LegalName: s.LegalName,
            WebsiteUrl: s.WebsiteUrl,
            SupportEmail: s.SupportEmail,
            SupportPhone: s.SupportPhone,
            LogoUrl: s.LogoUrl,
            Address: s.Address,
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
            WhatsAppNumber: s.WhatsAppNumber,
            LinkedInUrl: s.LinkedInUrl,
            Seo: new SeoSettingsDto(
                s.Seo.MetaTitle,
                s.Seo.MetaDescription,
                s.Seo.MetaKeywords,
                s.Seo.FaviconUrl,
                s.Seo.OgImageUrl),
            Delivery: new DeliverySettingsDto(
                s.Delivery.FlatFeeAmount,
                s.Delivery.FreeShippingThreshold,
                s.Delivery.CodEnabled,
                s.Delivery.CodExtraFee,
                s.Delivery.ProcessingDays,
                s.Delivery.MinDeliveryDays,
                s.Delivery.MaxDeliveryDays),
            Tracking: new TrackingSettingsDto(
                tracking.GoogleAnalyticsMeasurementId,
                tracking.MetaPixelId));
}
