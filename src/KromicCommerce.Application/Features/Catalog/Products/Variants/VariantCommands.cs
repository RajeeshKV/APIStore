namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

public sealed record CreateVariantCommand(
    Guid ProductId,
    string? Sku,
    decimal? PriceOverride,

    /// <summary>
    /// Explicit display position. Null appends the variant after the current maximum, so the
    /// admin UI does not have to manage ordering during normal use.
    /// </summary>
    int? SortOrder,

    /// <summary>
    /// One attribute value per axis (e.g. the Storage value and the Colour value).
    /// Null or empty creates an unconfigured variant, which is how products that vary on a
    /// single implicit dimension continue to work.
    /// </summary>
    List<Guid>? AttributeValueIds) : ICommand<VariantResponse>;

public sealed record UpdateVariantCommand(
    Guid ProductId,
    Guid VariantId,
    string? Sku,
    decimal? PriceOverride,
    int SortOrder,
    bool IsActive,
    List<Guid>? AttributeValueIds) : ICommand<VariantResponse>;

public sealed record DeleteVariantCommand(Guid ProductId, Guid VariantId) : ICommand;
