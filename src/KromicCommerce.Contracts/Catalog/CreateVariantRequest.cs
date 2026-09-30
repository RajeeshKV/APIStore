namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Request to create a product variant.
///
/// A variant is identified by its attribute values — one per axis the product varies on
/// (for example Storage: 128GB + Colour: Black). Omit <see cref="AttributeValueIds"/> to
/// create an unconfigured variant, which is how products that vary on a single implicit
/// dimension keep working.
/// </summary>
public sealed record CreateVariantRequest(
    string? Sku,
    decimal? PriceOverride,

    /// <summary>
    /// Optional display position. Omit or leave null to have the backend append the variant
    /// after the current maximum — the recommended path, since it means the admin UI never
    /// has to manage ordering when adding variants.
    /// </summary>
    int? SortOrder = null,

    List<Guid>? AttributeValueIds = null);
