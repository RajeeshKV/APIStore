namespace KromicCommerce.Application.Features.Catalog.Categories.GetCategories;

public sealed record GetCategoriesQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<CategoryResponse>>;
