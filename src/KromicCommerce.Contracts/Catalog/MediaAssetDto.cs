namespace KromicCommerce.Contracts.Catalog;

public sealed record MediaAssetDto(
    string PublicId,
    string SecureUrl,
    string? Format,
    int? Width,
    int? Height,
    string? AltText);
