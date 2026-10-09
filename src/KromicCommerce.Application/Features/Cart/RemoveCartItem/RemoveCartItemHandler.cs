using KromicCommerce.Application.Features.Cart.GetCart;
using KromicCommerce.Contracts.Cart;
using MediatR;

namespace KromicCommerce.Application.Features.Cart.RemoveCartItem;

/// <summary>
/// Removes an item from the cart and returns the updated cart.
/// </summary>
internal sealed class RemoveCartItemHandler(
    IApplicationDbContext db,
    IMediator mediator)
    : ICommandHandler<RemoveCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> Handle(
        RemoveCartItemCommand command, CancellationToken cancellationToken)
    {
        KromicCommerce.Domain.Cart.Cart? cart = null;
        if (command.CustomerId.HasValue)
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.CustomerId == command.CustomerId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(command.AnonymousCartId))
            cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.AnonymousId == command.AnonymousCartId, cancellationToken);

        if (cart is null)
        {
            // Idempotent: return empty cart response
            var settings = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var currency = settings?.CurrencyCode ?? "INR";
            return Result.Success(new CartResponse(Guid.Empty, [], 0m, currency, 0, true, null));
        }

        cart.RemoveItem(command.CartItemId);
        await db.SaveChangesAsync(cancellationToken);

        // Return updated cart
        var cartResult = await mediator.Send(
            new GetCartQuery(command.CustomerId, command.AnonymousCartId), cancellationToken);
        return cartResult;
    }
}
