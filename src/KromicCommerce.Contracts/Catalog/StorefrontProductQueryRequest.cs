namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Public storefront product query parameters.
/// Uses slug-based category/brand filters (customer-friendly, URL-safe).
/// Attribute filters use AND between different attribute names, OR within the same name.
/// Sort fields are server-side whitelisted.
/// </summary>
public sealed record StorefrontProductQueryRequest(
    int Page = 1,
    int PageSize = 20,

    /// <summary>Max 200 characters. Case-insensitive. Searches name, short description, SKU.</summary>
    string? Search = null,

    /// <summary>Filter by category slug (e.g. "electronics"). Only active categories.</summary>
    string? CategorySlug = null,

    /// <summary>Filter by brand slug (e.g. "apple"). Only active brands.</summary>
    string? BrandSlug = null,

    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? IsFeatured = null,

    /// <summary>When true, only return products with available inventory (Available > 0).</summary>
    bool InStockOnly = false,

    /// <summary>
    /// Attribute filters. AND between different attribute names, OR within the same name.
    /// Example: Color=Black OR White AND Size=M
    /// </summary>
    IReadOnlyList<AttributeFilterItem>? AttributeFilters = null,

    /// <summary>Whitelisted: "name", "price", "created_at". Defaults to created_at desc.</summary>
    string? SortBy = null,

    /// <summary>"asc" or "desc". Default: "desc".</summary>
    string SortDirection = "desc");

/// <summary>A single attribute filter entry. Multiple values for the same name are OR-combined.</summary>
public sealed record AttributeFilterItem(
    string AttributeName,
    string AttributeValue);
