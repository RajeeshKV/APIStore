namespace KromicCommerce.Application.Features.Cart.ClearCart;

internal sealed class ClearCartHandler(IApplicationDbContext db)
    : ICommandHandler<ClearCartCommand>
{
    public async Task<Result> Handle(ClearCartCommand command, CancellationToken cancellationToken)
    {
        KromicCommerce.Domain.Cart.Cart? cart = null;
        if (command.CustomerId.HasValue)
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.CustomerId == command.CustomerId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(command.AnonymousCartId))
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.AnonymousId == command.AnonymousCartId, cancellationToken);

        if (cart is null) return Result.Success();
        cart.Clear();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
