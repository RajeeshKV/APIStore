namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

public sealed record CreateVariantCommand(
    Guid ProductId,
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    List<Guid>? AttributeValueIds) : ICommand<VariantResponse>;

public sealed record UpdateVariantCommand(
    Guid ProductId,
    Guid VariantId,
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    bool IsActive,
    List<Guid>? AttributeValueIds) : ICommand;

public sealed record DeleteVariantCommand(Guid ProductId, Guid VariantId) : ICommand;
