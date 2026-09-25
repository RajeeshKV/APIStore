namespace KromicCommerce.Application.Features.Cart.RemoveCartItem;

internal sealed class RemoveCartItemHandler(IApplicationDbContext db)
    : ICommandHandler<RemoveCartItemCommand>
{
    public async Task<Result> Handle(RemoveCartItemCommand command, CancellationToken cancellationToken)
    {
        KromicCommerce.Domain.Cart.Cart? cart = null;
        if (command.CustomerId.HasValue)
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.CustomerId == command.CustomerId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(command.AnonymousCartId))
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.AnonymousId == command.AnonymousCartId, cancellationToken);

        if (cart is null) return Result.Success(); // idempotent
        cart.RemoveItem(command.CartItemId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
