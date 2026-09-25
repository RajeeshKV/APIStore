namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Public variant representation for storefront display.
/// Uses GetEffectivePrice — never exposes raw PriceOverride alone.
/// Stock is expressed as availability, not raw counts.
/// </summary>
public sealed record StorefrontVariantResponse(
    Guid Id,
    string? Sku,

    /// <summary>
    /// Effective selling price — Product.GetEffectivePrice(variant).
    /// Variant.PriceOverride when set, otherwise Product.Price.
    /// </summary>
    decimal EffectivePrice,

    int SortOrder,
    bool IsActive,

    /// <summary>Comma-separated attribute value IDs enabling variant selection UI.</summary>
    string? AttributeValueIds,

    StockAvailability StockAvailability,
    bool CanPurchase);
