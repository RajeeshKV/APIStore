using KromicCommerce.Application.Features.Cart.GetCart;

namespace KromicCommerce.Application.Features.Cart.UpdateCartItem;

internal sealed class UpdateCartItemHandler(
    IApplicationDbContext db,
    IMediator mediator)
    : ICommandHandler<UpdateCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> Handle(
        UpdateCartItemCommand command, CancellationToken cancellationToken)
    {
        var cart = await ResolveCart(command, cancellationToken);
        if (cart is null)
            return Result.Failure<CartResponse>(Error.NotFound("CART_NOT_FOUND", "Cart not found."));

        var item = cart.Items.FirstOrDefault(i => i.Id == command.CartItemId);
        if (item is null)
            return Result.Failure<CartResponse>(Error.NotFound("CART_ITEM_NOT_FOUND", "Cart item not found."));

        // Server-side inventory check
        var inventory = await db.InventoryItems.FirstOrDefaultAsync(
            inv => inv.ProductId == item.ProductId && inv.VariantId == item.VariantId,
            cancellationToken);
        if (inventory is not null && inventory.Available < command.Quantity)
            return Result.Failure<CartResponse>(
                Error.Conflict("INSUFFICIENT_INVENTORY", $"Only {inventory.Available} unit(s) available."));

        cart.UpdateItemQuantity(command.CartItemId, command.Quantity);
        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetCartQuery(command.CustomerId, cart.AnonymousId), cancellationToken);
    }

    private async Task<KromicCommerce.Domain.Cart.Cart?> ResolveCart(
        UpdateCartItemCommand cmd, CancellationToken ct)
    {
        if (cmd.CustomerId.HasValue)
            return await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.CustomerId == cmd.CustomerId.Value && c.ExpiresAt > DateTime.UtcNow, ct);
        if (!string.IsNullOrWhiteSpace(cmd.AnonymousCartId))
            return await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.AnonymousId == cmd.AnonymousCartId && c.ExpiresAt > DateTime.UtcNow, ct);
        return null;
    }
}
