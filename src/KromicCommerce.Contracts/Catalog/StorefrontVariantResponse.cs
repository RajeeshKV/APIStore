namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Public variant representation for storefront display.
/// Uses GetEffectivePrice — never exposes raw PriceOverride alone.
/// Stock is expressed as availability, not raw counts.
///
/// Backward compatibility: products without variants return an empty <c>Variants</c> array and
/// the product's own Price / StockAvailability / CanPurchase describe the whole product.
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
    bool CanPurchase,

    /// <summary>
    /// The variant's attribute values resolved to display names, e.g.
    /// <c>[{ AttributeName: "Storage", Value: "128GB" }]</c>.
    ///
    /// <see cref="CanPurchase"/> is false for an inactive variant regardless of stock, so a
    /// client can disable the option without re-deriving the rule.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? Attributes = null,

    /// <summary>
    /// Images uploaded specifically for this variant. Empty when none exist — the product's
    /// own <see cref="StorefrontProductResponse.Images"/> remains the authoritative gallery.
    /// </summary>
    IReadOnlyList<StorefrontImageResponse>? Images = null)
{
    /// <summary>
    /// DERIVED from <see cref="StockAvailability"/> — never stored or set independently.
    ///
    /// Per-variant, not rolled up: a product is out of stock only when ALL of its variants are.
    /// Exact unit counts are deliberately not exposed on this public projection; the admin
    /// projection (VariantResponse.AvailableStock) carries them where an operator needs them.
    /// </summary>
    public bool IsOutOfStock => StockAvailability == StockAvailability.OutOfStock;
}
