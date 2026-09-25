namespace KromicCommerce.Contracts.Catalog;

public sealed record VariantResponse(
    Guid Id,
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    bool IsActive,
    string? AttributeValueIds,
    int? AvailableStock);
