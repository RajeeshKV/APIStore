namespace KromicCommerce.Application.Features.Catalog.Products.GetProducts;

public sealed record GetProductsQuery(ProductQueryRequest Request) : IQuery<PagedResponse<ProductSummaryResponse>>;
public sealed record GetAdminProductsQuery(ProductQueryRequest Request) : IQuery<PagedResponse<ProductSummaryResponse>>;
public sealed record GetProductBySlugQuery(string Slug) : IQuery<ProductResponse>;
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductResponse>;
