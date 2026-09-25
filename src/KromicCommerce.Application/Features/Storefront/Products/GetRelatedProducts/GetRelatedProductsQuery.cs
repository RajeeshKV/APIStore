namespace KromicCommerce.Application.Features.Storefront.Products.GetRelatedProducts;

public sealed record GetRelatedProductsQuery(
    Guid ProductId,
    Guid? CategoryId,
    Guid? BrandId,
    int Limit = 8)
    : IQuery<IReadOnlyList<StorefrontProductSummaryResponse>>;
