using System.Security.Cryptography;
using KromicCommerce.Application.Features.Cart.GetCart;

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
        // Get or create cart
        // -----------------------------------------------------------------------
        KromicCommerce.Domain.Cart.Cart? cart = null;

        if (command.CustomerId.HasValue)
            cart = await db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.CustomerId == command.CustomerId.Value
                         && c.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);
        else if (!string.IsNullOrWhiteSpace(command.AnonymousCartId))
            cart = await db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.AnonymousId == command.AnonymousCartId
                         && c.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);

        if (cart is null)
        {
            cart = command.CustomerId.HasValue
                ? KromicCommerce.Domain.Cart.Cart.CreateForCustomer(command.CustomerId.Value)
                : KromicCommerce.Domain.Cart.Cart.CreateAnonymous(
                    GenerateAnonymousCartId());
            db.Carts.Add(cart);
        }

        cart.AddItem(command.ProductId, command.VariantId, command.Quantity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cart item added. CartId: {CartId} ProductId: {ProductId} Qty: {Qty}",
            cart.Id, command.ProductId, command.Quantity);

        // Return updated cart view
        var cartResult = await mediator.Send(
            new GetCartQuery(command.CustomerId, cart.AnonymousId), cancellationToken);
        return cartResult;
    }

    private static string GenerateAnonymousCartId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
