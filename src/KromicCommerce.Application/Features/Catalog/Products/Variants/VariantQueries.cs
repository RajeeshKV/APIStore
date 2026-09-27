namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

public sealed record GetVariantsQuery(Guid ProductId) : IQuery<IReadOnlyList<VariantResponse>>;

public sealed record GetVariantByIdQuery(Guid ProductId, Guid VariantId) : IQuery<VariantResponse>;
