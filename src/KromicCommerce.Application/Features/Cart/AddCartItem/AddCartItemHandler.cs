using KromicCommerce.Application.Features.Cart.GetCart;

namespace KromicCommerce.Application.Features.Cart.AddCartItem;

/// <summary>
/// Adds an item to the customer's cart using atomic PostgreSQL operations via
/// IApplicationDbContext to prevent concurrency issues.
///
/// Concurrency strategy:
///   - FindOrCreate*CartAsync: INSERT ON CONFLICT DO NOTHING → one active cart per session.
///   - UpsertCartItemAsync: INSERT ON CONFLICT DO UPDATE Quantity += → atomic increment,
///     no read-modify-write race, no lost updates, no duplicate rows.
///   - InventoryItem loaded AsNoTracking: it has an xmin concurrency token. Tracking it
///     would register the xmin snapshot and cause SaveChangesAsync to emit a spurious
///     UPDATE that always fails with DbUpdateConcurrencyException.
/// </summary>
internal sealed class AddCartItemHandler(
    IApplicationDbContext db,
    IMediator mediator,
    ILogger<AddCartItemHandler> logger)
    : ICommandHandler<AddCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> Handle(
        AddCartItemCommand command, CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // Validate product — server is authoritative, never trust frontend
        // -----------------------------------------------------------------------
        var productExists = await db.Products
            .AsNoTracking()
            .AnyAsync(
                p => p.Id == command.ProductId && p.Status == ProductStatus.Active,
                cancellationToken);

        if (!productExists)
            return Result.Failure<CartResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found or is unavailable."));

        if (command.VariantId.HasValue)
        {
            var variantExists = await db.ProductVariants
                .AsNoTracking()
                .AnyAsync(
                    v => v.Id == command.VariantId.Value
                         && v.ProductId == command.ProductId
                         && v.IsActive,
                    cancellationToken);

            if (!variantExists)
                return Result.Failure<CartResponse>(
                    Error.NotFound("VARIANT_NOT_FOUND", "Product variant not found or is unavailable."));
        }

        // -----------------------------------------------------------------------
        // Validate inventory — AsNoTracking: read-only, must not register xmin snapshot.
        // -----------------------------------------------------------------------
        var inventory = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                inv => inv.ProductId == command.ProductId && inv.VariantId == command.VariantId,
                cancellationToken);

        if (inventory is not null && inventory.Available < command.Quantity)
            return Result.Failure<CartResponse>(
                Error.Conflict("INSUFFICIENT_INVENTORY",
                    $"Only {inventory.Available} unit(s) available."));

        // -----------------------------------------------------------------------
        // Atomically find or create the cart, then upsert the item.
        // Both operations use PostgreSQL ON CONFLICT to prevent any race condition.
        // -----------------------------------------------------------------------
        Guid cartId;
        string? anonymousId = command.AnonymousCartId;

        if (command.CustomerId.HasValue)
        {
            cartId = await db.FindOrCreateCustomerCartAsync(
                command.CustomerId.Value, cancellationToken);
        }
        else
        {
            (cartId, anonymousId) = await db.FindOrCreateAnonymousCartAsync(
                command.AnonymousCartId, cancellationToken);
        }

        await db.UpsertCartItemAsync(
            cartId, command.ProductId, command.VariantId,
            command.Quantity, cancellationToken);

        logger.LogInformation(
            "Cart item upserted. CartId: {CartId} ProductId: {ProductId} Qty: {Qty}",
            cartId, command.ProductId, command.Quantity);

        // Return updated cart view
        var cartResult = await mediator.Send(
            new GetCartQuery(command.CustomerId, anonymousId), cancellationToken);
        return cartResult;
    }
}
