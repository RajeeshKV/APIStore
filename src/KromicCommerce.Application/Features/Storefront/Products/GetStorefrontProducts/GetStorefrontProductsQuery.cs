namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;

public sealed record GetStorefrontProductsQuery(
    StorefrontProductQueryRequest Request)
    : IQuery<PagedResponse<StorefrontProductSummaryResponse>>;
