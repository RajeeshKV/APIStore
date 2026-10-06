using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontVariantGrid;

/// <summary>
/// Variant-level product grid query. Reuses the same filter surface as the product list
/// so the storefront can swap the endpoint without changing the query string.
/// </summary>
public sealed record GetStorefrontVariantGridQuery(
    StorefrontProductQueryRequest Request)
    : IQuery<PagedResponse<StorefrontVariantRowResponse>>;
