namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Brand for storefront display.
/// ProductCount reflects active products only.
/// </summary>
public sealed record StorefrontBrandResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl,
    string? LogoUrl,

    /// <summary>Count of active (publicly visible) products in this brand.</summary>
    int ProductCount);
