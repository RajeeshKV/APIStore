namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Catalog product list query parameters.
/// All filter fields are optional — omitting a field returns all products.
/// Sort fields are whitelisted server-side to prevent SQL injection.
/// </summary>
public sealed record ProductQueryRequest(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? IsFeatured = null,
    bool? InStockOnly = null,

    /// <summary>Whitelisted: "name", "price", "created_at", "updated_at"</summary>
    string? SortBy = null,

    /// <summary>"asc" or "desc". Defaults to "asc".</summary>
    string? SortDirection = null);
