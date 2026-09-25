namespace KromicCommerce.Contracts.Catalog;

public sealed record UpdateVariantRequest(
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    bool IsActive,
    List<Guid>? AttributeValueIds = null);
