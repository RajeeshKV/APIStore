namespace KromicCommerce.Application.Abstractions.Catalog;

/// <summary>
/// Centralized delivery estimate calculation.
/// All storefront endpoints that display expected delivery dates must use this service.
/// Do not duplicate the calculation in controllers, handlers, or mapping helpers.
///
/// The estimate uses:
///   Today (UTC) + ProcessingDays + MinDeliveryDays  → "from" date
///   Today (UTC) + ProcessingDays + MaxDeliveryDays  → "to"   date
///
/// Holiday calendars and courier-specific rules are not implemented in Phase 4.
/// </summary>
public interface IDeliveryEstimateService
{
    /// <summary>
    /// Returns a delivery estimate using the current UTC date and the provided delivery settings.
    /// Returns null when ProcessingDays + MinDeliveryDays == 0 (immediate availability, no estimate needed).
    /// </summary>
    DeliveryEstimateDto? Calculate(
        int processingDays,
        int minDeliveryDays,
        int maxDeliveryDays,
        DateOnly? referenceDate = null);
}
