namespace KromicCommerce.Application.Features.Storefront.Products.GetFeaturedProducts;

public sealed record GetFeaturedProductsQuery(int Limit = 12)
    : IQuery<IReadOnlyList<StorefrontProductSummaryResponse>>;
