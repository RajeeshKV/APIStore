namespace KromicCommerce.Contracts.Catalog;

public sealed record ProductImageDto(
    Guid Id,
    MediaAssetDto Asset,
    int SortOrder,
    bool IsPrimary);
