using System.Security.Cryptography;
using KromicCommerce.Application.Features.Cart.GetCart;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Cart.AddCartItem;

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
        // Validate product server-side — never trust frontend
        // -----------------------------------------------------------------------
        var product = await db.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(
                p => p.Id == command.ProductId && p.Status == ProductStatus.Active,
                cancellationToken);

        if (product is null)
            return Result.Failure<CartResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found or is unavailable."));

        ProductVariant? variant = null;
        if (command.VariantId.HasValue)
        {
            variant = await db.ProductVariants.FirstOrDefaultAsync(
                v => v.Id == command.VariantId.Value
                     && v.ProductId == command.ProductId
                     && v.IsActive,
                cancellationToken);

            if (variant is null)
                return Result.Failure<CartResponse>(
                    Error.NotFound("VARIANT_NOT_FOUND", "Product variant not found or is unavailable."));
        }

        // -----------------------------------------------------------------------
        // Validate inventory — server is authoritative
        // -----------------------------------------------------------------------
        var inventory = await db.InventoryItems.FirstOrDefaultAsync(
            inv => inv.ProductId == command.ProductId && inv.VariantId == command.VariantId,
            cancellationToken);

        if (inventory is not null && inventory.Available < command.Quantity)
            return Result.Failure<CartResponse>(
                Error.Conflict("INSUFFICIENT_INVENTORY",
                    $"Only {inventory.Available} unit(s) available."));

        // -----------------------------------------------------------------------
        // Get or create cart — with retry on concurrency conflict.
        //
        // A DbUpdateConcurrencyException here means the cart or one of its items
        // was modified concurrently (e.g. duplicate request, expired-cart cleanup,
        // or a previous cancelled checkout clearing items). We reload and retry once.
        // -----------------------------------------------------------------------
        const int maxAttempts = 2;
        KromicCommerce.Domain.Cart.Cart? cart = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Reload cart on retry so the change tracker has a fresh snapshot
            if (attempt > 0)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(
                    "Retrying AddCartItem after concurrency conflict. " +
                    "Attempt {Attempt}. CustomerId: {CustomerId}",
                    attempt + 1, command.CustomerId);
            }

            cart = await LoadOrCreateCartAsync(command, cancellationToken);

            cart.AddItem(command.ProductId, command.VariantId, command.Quantity);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break; // success
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                // Stale cart data — loop back and reload
                continue;
            }
        }

        logger.LogInformation(
            "Cart item added. CartId: {CartId} ProductId: {ProductId} Qty: {Qty}",
            cart!.Id, command.ProductId, command.Quantity);

        // Return updated cart view
        var cartResult = await mediator.Send(
            new GetCartQuery(command.CustomerId, cart.AnonymousId), cancellationToken);
        return cartResult;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<KromicCommerce.Domain.Cart.Cart> LoadOrCreateCartAsync(
        AddCartItemCommand command, CancellationToken ct)
    {
        KromicCommerce.Domain.Cart.Cart? cart = null;

        if (command.CustomerId.HasValue)
            cart = await db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.CustomerId == command.CustomerId.Value && c.ExpiresAt > DateTime.UtcNow, ct);
        else if (!string.IsNullOrWhiteSpace(command.AnonymousCartId))
            cart = await db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.AnonymousId == command.AnonymousCartId && c.ExpiresAt > DateTime.UtcNow, ct);

        if (cart is not null) return cart;

        cart = command.CustomerId.HasValue
            ? KromicCommerce.Domain.Cart.Cart.CreateForCustomer(command.CustomerId.Value)
            : KromicCommerce.Domain.Cart.Cart.CreateAnonymous(GenerateAnonymousCartId());

        db.Carts.Add(cart);
        return cart;
    }

    private static string GenerateAnonymousCartId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
