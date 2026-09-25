namespace KromicCommerce.Application.Features.Cart.RemoveCartItem;

public sealed record RemoveCartItemCommand(
    Guid? CustomerId,
    string? AnonymousCartId,
    Guid CartItemId) : ICommand;
