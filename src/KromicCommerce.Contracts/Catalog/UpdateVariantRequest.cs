namespace KromicCommerce.Contracts.Catalog;

public sealed record UpdateVariantRequest(
    string? Sku,
    decimal? PriceOverride,
    decimal? CompareAtPrice,
    int SortOrder,
    bool IsActive,
    List<Guid>? AttributeValueIds = null);
