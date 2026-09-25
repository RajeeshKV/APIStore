namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Customer-friendly delivery window estimate derived from BusinessSettings.Delivery.
/// Dates are in ISO-8601 format (yyyy-MM-dd).
/// Does not account for holiday calendars — basic processing + delivery days only.
/// </summary>
public sealed record DeliveryEstimateDto(
    /// <summary>Earliest expected delivery date (YYYY-MM-DD).</summary>
    string From,

    /// <summary>Latest expected delivery date (YYYY-MM-DD).</summary>
    string To,

    /// <summary>Human-readable description, e.g. "Delivered in 3–5 business days".</summary>
    string Description);
