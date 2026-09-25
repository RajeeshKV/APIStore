namespace KromicCommerce.Contracts.Catalog;

public sealed record CreateVariantRequest(
    string? Sku,
    decimal? PriceOverride,
    int SortOrder = 0,
    List<Guid>? AttributeValueIds = null);
