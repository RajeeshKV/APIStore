namespace KromicCommerce.Application.Features.Catalog.Categories.GetCategory;

public sealed record GetCategoryBySlugQuery(string Slug) : IQuery<CategoryResponse>;
public sealed record GetCategoryByIdQuery(Guid Id) : IQuery<CategoryResponse>;
