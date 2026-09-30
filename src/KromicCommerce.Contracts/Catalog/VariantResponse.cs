namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Admin view of a product variant.
/// PriceOverride is the raw override; the effective price also depends on the product's base
/// price, so use the storefront projection when displaying a selling price to a customer.
/// </summary>
public sealed record VariantResponse(
    Guid Id,
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    bool IsActive,
    string? AttributeValueIds,
    int? AvailableStock,

    /// <summary>
    /// The variant's attribute values resolved to display names, e.g.
    /// <c>[{ AttributeName: "Storage", Value: "128GB" }]</c>.
    ///
    /// Added so a variant selector can be built without a second request. <see cref="AttributeValueIds"/>
    /// is retained unchanged for backward compatibility. Values that no longer exist are omitted,
    /// so a variant whose value was deleted may return fewer entries than it references.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? Attributes = null)
{
    /// <summary>
    /// DERIVED from <see cref="AvailableStock"/> — never stored or set independently, so the
    /// contradictory "AvailableStock 10 / IsOutOfStock true" state cannot exist.
    ///
    /// A null <see cref="AvailableStock"/> means the variant has no inventory record, i.e. it
    /// is not stock-tracked. That is reported as NOT out of stock, matching the storefront
    /// translation in StorefrontStockService, which treats an untracked product as purchasable.
    /// </summary>
    public bool IsOutOfStock => AvailableStock.HasValue && AvailableStock.Value <= 0;
}
