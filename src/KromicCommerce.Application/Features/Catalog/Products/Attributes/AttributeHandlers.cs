namespace KromicCommerce.Application.Features.Catalog.Products.Attributes;

/// <summary>
/// Returns every attribute definition and selectable value for a product, ordered for display.
///
/// This is the endpoint a variant selector is built from: the axes (Storage, Colour, ...)
/// and the values available on each.
/// </summary>
public sealed record GetProductAttributesQuery(Guid ProductId)
    : IQuery<ProductAttributesResponse>;

/// <summary>
/// Creates or replaces one attribute definition and its values.
///
/// ProductAttribute values are replaced wholesale rather than diffed: whatever is not named
/// in the request is deleted. That keeps the endpoint a complete, idempotent description of
/// the attribute and avoids a second diffing endpoint. A deleted value can leave a variant
/// referencing an ID that no longer exists; the storefront resolver simply omits unknown
/// values, so such a variant degrades to an unconfigured variant instead of erroring.
/// </summary>
public sealed record UpsertProductAttributeCommand(
    Guid ProductId,
    string Name,
    IReadOnlyList<ProductAttributeValueRequest> Values)
    : ICommand<ProductAttributesResponse>;

/// <summary>Deletes an attribute definition and all of its values. Idempotent per attribute ID.</summary>
public sealed record DeleteProductAttributeCommand(Guid ProductId, Guid AttributeId)
    : ICommand;

internal sealed class UpsertProductAttributeValidator : AbstractValidator<UpsertProductAttributeCommand>
{
    public UpsertProductAttributeValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Attribute name is required.")
            .MaximumLength(100).WithMessage("Attribute name must not exceed 100 characters.");

        RuleForEach(x => x.Values)
            .Must(v => !string.IsNullOrWhiteSpace(v.Value))
            .WithMessage("Attribute value must not be empty.")
            .Must(v => v.Value.Trim().Length <= 100)
            .WithMessage("Attribute value must not exceed 100 characters.");

        // Duplicate values within one attribute make a variant selector ambiguous.
        RuleFor(x => x.Values)
            .Must(values => values
                .Select(v => v.Value?.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count() == values.Count)
            .WithMessage("Attribute values must be unique within an attribute.")
            .WithName("Values");
    }
}

internal sealed class DeleteProductAttributeValidator : AbstractValidator<DeleteProductAttributeCommand>
{
    public DeleteProductAttributeValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product is required.");
        RuleFor(x => x.AttributeId).NotEmpty().WithMessage("Attribute is required.");
    }
}

internal sealed class GetProductAttributesHandler(IApplicationDbContext db)
    : IQueryHandler<GetProductAttributesQuery, ProductAttributesResponse>
{
    public async Task<Result<ProductAttributesResponse>> Handle(
        GetProductAttributesQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(p => p.Id == query.ProductId, cancellationToken))
            return Result.Failure<ProductAttributesResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        var attributes = await db.ProductAttributes
            .AsNoTracking()
            .Include(a => a.Values)
            .Where(a => a.ProductId == query.ProductId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(new ProductAttributesResponse(
            query.ProductId, AttributeMapper.Map(attributes)));
    }
}

internal sealed class UpsertProductAttributeHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<UpsertProductAttributeHandler> logger)
    : ICommandHandler<UpsertProductAttributeCommand, ProductAttributesResponse>
{
    public async Task<Result<ProductAttributesResponse>> Handle(
        UpsertProductAttributeCommand command, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Where(p => p.Id == command.ProductId)
            .Select(p => new { p.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
            return Result.Failure<ProductAttributesResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        var name = command.Name.Trim();

        // One definition per (product, name) — reuse rather than reject, so the admin UI can
        // save a renamed-typo or re-entered attribute idempotently.
        var attribute = await db.ProductAttributes
            .Include(a => a.Values)
            .FirstOrDefaultAsync(a => a.ProductId == command.ProductId && a.Name == name, cancellationToken);

        if (attribute is null)
        {
            attribute = ProductAttribute.Create(
                command.ProductId, name, await NextAttributeSortOrder(command.ProductId, cancellationToken));
            db.ProductAttributes.Add(attribute);
        }

        // Replace the value set wholesale.
        var requested = command.Values.ToList();
        var requestedIds = requested
            .Where(v => v.Id.HasValue)
            .Select(v => v.Id!.Value)
            .ToHashSet();

        // Drop values that are no longer offered. A variant that still references one simply
        // loses that axis; ProductVariant.ParsedAttributeValueIds simply yields an unknown id
        // that the resolvers skip.
        var removed = attribute.Values
            .Where(v => !requestedIds.Contains(v.Id))
            .ToList();
        foreach (var value in removed)
            db.ProductAttributeValues.Remove(value);

        var sortOrder = 0;
        foreach (var input in requested)
        {
            var text = input.Value.Trim();
            var existing = input.Id.HasValue
                ? attribute.Values.FirstOrDefault(v => v.Id == input.Id.Value)
                : null;

            if (existing is not null)
            {
                existing.Rename(text);
                existing.SetSortOrder(sortOrder);
            }
            else
            {
                var created = ProductAttributeValue.Create(attribute.Id, text, sortOrder);
                attribute.AddValue(created);
                db.ProductAttributeValues.Add(created);
            }
            sortOrder++;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Product {ProductId} attribute '{AttributeName}' saved with {ValueCount} value(s).",
            command.ProductId, name, requested.Count);

        cache.InvalidateProductGraph(command.ProductId, product.Slug);

        return await new GetProductAttributesHandler(db)
            .Handle(new GetProductAttributesQuery(command.ProductId), cancellationToken);
    }

    private async Task<int> NextAttributeSortOrder(Guid productId, CancellationToken ct)
    {
        var max = await db.ProductAttributes
            .Where(a => a.ProductId == productId)
            .Select(a => (int?)a.SortOrder)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
    }
}

internal sealed class DeleteProductAttributeHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<DeleteProductAttributeHandler> logger)
    : ICommandHandler<DeleteProductAttributeCommand>
{
    public async Task<Result> Handle(
        DeleteProductAttributeCommand command, CancellationToken cancellationToken)
    {
        var attribute = await db.ProductAttributes
            .Include(a => a.Values)
            .FirstOrDefaultAsync(
                a => a.Id == command.AttributeId && a.ProductId == command.ProductId, cancellationToken);

        // Idempotent: already absent is the target state.
        if (attribute is null) return Result.Success();

        var slug = await db.Products
            .Where(p => p.Id == command.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var value in attribute.Values.ToList())
            db.ProductAttributeValues.Remove(value);
        db.ProductAttributes.Remove(attribute);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Attribute {AttributeId} removed from product {ProductId}.", command.AttributeId, command.ProductId);

        cache.InvalidateProductGraph(command.ProductId, slug);
        return Result.Success();
    }
}

internal static class AttributeMapper
{
    public static IReadOnlyList<ProductAttributeDto> Map(IEnumerable<ProductAttribute> attributes) =>
        attributes
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new ProductAttributeDto(
                a.Id, a.Name, a.SortOrder,
                a.Values.OrderBy(v => v.SortOrder).ThenBy(v => v.Value)
                    .Select(v => new AttributeValueDto(v.Id, v.Value, v.SortOrder))
                    .ToList()))
            .ToList();
}
