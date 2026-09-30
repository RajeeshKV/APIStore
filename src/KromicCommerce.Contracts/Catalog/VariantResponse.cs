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
    IReadOnlyList<VariantAttributeValueResponse>? Attributes = null);
